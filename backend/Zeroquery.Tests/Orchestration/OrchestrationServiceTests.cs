using Microsoft.Extensions.Options;
using Zeroquery.Core.Mcp;
using Zeroquery.Core.Orchestration;

namespace Zeroquery.Tests.Orchestration;

public class OrchestrationServiceTests
{
    private static readonly IReadOnlyList<McpTool> SampleTools = new[]
    {
        new McpTool("read_records", "Reads records from an entity.", null),
        new McpTool("describe_entities", "Describes available entities.", null)
    };

    private static OrchestrationService CreateService(FakeLlmProvider llmProvider, FakeMcpClient mcpClient) =>
        new(llmProvider, new FakeMcpClientFactory(mcpClient), Options.Create(new OrchestrationOptions { MaxToolCallIterations = 6 }));

    [Fact]
    public async Task RunQueryAsync_CallsRenderResult_ReturnsUiSpec_WhenModelAnswersImmediately()
    {
        var mcpClient = new FakeMcpClient(SampleTools, new());
        var llmProvider = new FakeLlmProvider(new[]
        {
            new LlmCompletion(null, new[]
            {
                new LlmToolCall(
                    "call-1",
                    "render_result",
                    """
                    {
                      "type": "table",
                      "title": "Top products",
                      "columns": [{ "key": "name", "label": "Name" }],
                      "rows": [{ "name": "Chai" }],
                      "meta": { "sourceEntity": "Products" }
                    }
                    """)
            })
        });

        var service = CreateService(llmProvider, mcpClient);

        var result = await service.RunQueryAsync("http://localhost:9999", "list products");

        Assert.Equal(UiSpecType.Table, result.Type);
        Assert.Equal("Top products", result.Title);
        Assert.Single(result.Rows);
        Assert.Equal("Chai", result.Rows[0]["name"]);
        Assert.Equal("Products", result.Meta.SourceEntity);
        Assert.True(mcpClient.WasInitialized);
    }

    [Fact]
    public async Task RunQueryAsync_ExecutesMcpToolCall_ThenFeedsResultBack_BeforeRendering()
    {
        var mcpClient = new FakeMcpClient(SampleTools, new()
        {
            ["read_records"] = new McpToolCallResult(false, """{"value":[{"ProductName":"Chai"}]}""")
        });

        var llmProvider = new FakeLlmProvider(new[]
        {
            // Turn 1: ask for data
            new LlmCompletion(null, new[]
            {
                new LlmToolCall("call-1", "read_records", """{"entity":"Products","first":1}""")
            }),
            // Turn 2: render final result using the tool's data
            new LlmCompletion(null, new[]
            {
                new LlmToolCall(
                    "call-2",
                    "render_result",
                    """
                    {
                      "type": "table",
                      "title": "Products",
                      "columns": [{ "key": "ProductName", "label": "Name" }],
                      "rows": [{ "ProductName": "Chai" }],
                      "meta": { "sourceEntity": "Products" }
                    }
                    """)
            })
        });

        var service = CreateService(llmProvider, mcpClient);

        var result = await service.RunQueryAsync("http://localhost:9999", "list products");

        Assert.Equal("Products", result.Title);
        Assert.Contains("read_records", mcpClient.CalledToolNames);

        // Second call to the LLM should include a "tool" role message with the MCP result.
        var secondCallMessages = llmProvider.RecordedCalls[1];
        Assert.Contains(secondCallMessages, m => m.Role == "tool" && m.Content!.Contains("Chai"));
    }

    [Fact]
    public async Task RunQueryAsync_NudgesModel_WhenItRespondsWithPlainTextInsteadOfToolCall()
    {
        var mcpClient = new FakeMcpClient(SampleTools, new());
        var llmProvider = new FakeLlmProvider(new[]
        {
            // Turn 1: model answers in plain text (no tool call) — should get nudged.
            new LlmCompletion("The answer is 42.", Array.Empty<LlmToolCall>()),
            // Turn 2: model complies and calls render_result.
            new LlmCompletion(null, new[]
            {
                new LlmToolCall(
                    "call-1",
                    "render_result",
                    """
                    {
                      "type": "stat",
                      "title": "Answer",
                      "columns": [],
                      "rows": [],
                      "meta": { "sourceEntity": "" }
                    }
                    """)
            })
        });

        var service = CreateService(llmProvider, mcpClient);

        var result = await service.RunQueryAsync("http://localhost:9999", "what is the answer?");

        Assert.Equal(UiSpecType.Stat, result.Type);
        Assert.Equal(2, llmProvider.RecordedCalls.Count);
    }

    [Fact]
    public async Task RunQueryAsync_Throws_WhenMaxIterationsExceededWithoutRenderResult()
    {
        var mcpClient = new FakeMcpClient(SampleTools, new()
        {
            ["read_records"] = new McpToolCallResult(false, "{}")
        });

        // Always asks for more data, never renders — should hit the iteration cap.
        var completions = Enumerable.Range(0, 10)
            .Select(_ => new LlmCompletion(null, new[] { new LlmToolCall(Guid.NewGuid().ToString(), "read_records", "{}") }));

        var llmProvider = new FakeLlmProvider(completions);
        var service = new OrchestrationService(
            llmProvider,
            new FakeMcpClientFactory(mcpClient),
            Options.Create(new OrchestrationOptions { MaxToolCallIterations = 3 }));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RunQueryAsync("http://localhost:9999", "infinite loop query"));
    }

    [Fact]
    public async Task RunQueryAsync_FeedsMcpErrorBackAsToolResult_InsteadOfThrowing()
    {
        var mcpClient = new FakeMcpClient(SampleTools, new()); // no scripted result -> throws McpClientException
        var llmProvider = new FakeLlmProvider(new[]
        {
            new LlmCompletion(null, new[]
            {
                new LlmToolCall("call-1", "read_records", """{"entity":"Unknown"}""")
            }),
            new LlmCompletion(null, new[]
            {
                new LlmToolCall(
                    "call-2",
                    "render_result",
                    """
                    {
                      "type": "card",
                      "title": "Error handled",
                      "columns": [],
                      "rows": [],
                      "meta": { "sourceEntity": "" }
                    }
                    """)
            })
        });

        var service = CreateService(llmProvider, mcpClient);

        var result = await service.RunQueryAsync("http://localhost:9999", "query with bad entity");

        Assert.Equal("Error handled", result.Title);
        var secondCallMessages = llmProvider.RecordedCalls[1];
        Assert.Contains(secondCallMessages, m => m.Role == "tool" && m.Content!.Contains("error"));
    }

    [Fact]
    public async Task RunQueryAsync_WithProgressCallback_ReportsThinkingAndRendering_WhenModelAnswersImmediately()
    {
        var mcpClient = new FakeMcpClient(SampleTools, new());
        var llmProvider = new FakeLlmProvider(new[]
        {
            new LlmCompletion(null, new[]
            {
                new LlmToolCall(
                    "call-1",
                    "render_result",
                    """
                    {
                      "type": "table",
                      "title": "Top products",
                      "columns": [],
                      "rows": [],
                      "meta": { "sourceEntity": "Products" }
                    }
                    """)
            })
        });

        var service = CreateService(llmProvider, mcpClient);
        var reportedStages = new List<OrchestrationProgressEvent>();

        await service.RunQueryAsync(
            "http://localhost:9999",
            "list products",
            onProgress: (progressEvent, _) =>
            {
                reportedStages.Add(progressEvent);
                return Task.CompletedTask;
            });

        Assert.Equal(
            new[] { OrchestrationStage.Thinking, OrchestrationStage.Rendering },
            reportedStages.Select(e => e.Stage));
    }

    [Fact]
    public async Task RunQueryAsync_WithProgressCallback_ReportsToolCallAndToolResult_WithToolName()
    {
        var mcpClient = new FakeMcpClient(SampleTools, new()
        {
            ["read_records"] = new McpToolCallResult(false, """{"value":[{"ProductName":"Chai"}]}""")
        });

        var llmProvider = new FakeLlmProvider(new[]
        {
            new LlmCompletion(null, new[]
            {
                new LlmToolCall("call-1", "read_records", """{"entity":"Products","first":1}""")
            }),
            new LlmCompletion(null, new[]
            {
                new LlmToolCall(
                    "call-2",
                    "render_result",
                    """
                    {
                      "type": "table",
                      "title": "Products",
                      "columns": [],
                      "rows": [],
                      "meta": { "sourceEntity": "Products" }
                    }
                    """)
            })
        });

        var service = CreateService(llmProvider, mcpClient);
        var reportedStages = new List<OrchestrationProgressEvent>();

        await service.RunQueryAsync(
            "http://localhost:9999",
            "list products",
            onProgress: (progressEvent, _) =>
            {
                reportedStages.Add(progressEvent);
                return Task.CompletedTask;
            });

        Assert.Equal(
            new[]
            {
                OrchestrationStage.Thinking,
                OrchestrationStage.ToolCall,
                OrchestrationStage.ToolResult,
                OrchestrationStage.Thinking,
                OrchestrationStage.Rendering
            },
            reportedStages.Select(e => e.Stage));

        var toolCallEvent = reportedStages.Single(e => e.Stage == OrchestrationStage.ToolCall);
        Assert.Equal("read_records", toolCallEvent.ToolName);
        var toolResultEvent = reportedStages.Single(e => e.Stage == OrchestrationStage.ToolResult);
        Assert.Equal("read_records", toolResultEvent.ToolName);
    }

    [Fact]
    public async Task RunQueryAsync_WithoutProgressCallback_StillReturnsUiSpec_UnaffectedByOverload()
    {
        // The non-streaming callers (existing tests above, and the non-streaming query
        // endpoint) go through the 3-arg overload, which forwards onProgress: null to the
        // 4-arg overload — confirms that delegation doesn't change observable behavior.
        var mcpClient = new FakeMcpClient(SampleTools, new());
        var llmProvider = new FakeLlmProvider(new[]
        {
            new LlmCompletion(null, new[]
            {
                new LlmToolCall(
                    "call-1",
                    "render_result",
                    """
                    {
                      "type": "stat",
                      "title": "Count",
                      "columns": [],
                      "rows": [],
                      "meta": { "sourceEntity": "" }
                    }
                    """)
            })
        });

        var service = CreateService(llmProvider, mcpClient);

        var result = await service.RunQueryAsync("http://localhost:9999", "how many products?");

        Assert.Equal(UiSpecType.Stat, result.Type);
        Assert.Equal("Count", result.Title);
    }

    [Fact]
    public async Task RunQueryAsync_ParsesFormUiSpec_WhenModelProposesMutation()
    {
        var mcpClient = new FakeMcpClient(SampleTools, new());
        var llmProvider = new FakeLlmProvider(new[]
        {
            new LlmCompletion(null, new[]
            {
                new LlmToolCall(
                    "call-1",
                    "render_result",
                    """
                    {
                      "type": "form",
                      "title": "Update Chai Price",
                      "columns": [],
                      "rows": [],
                      "meta": { "sourceEntity": "Products" },
                      "form": {
                        "operation": "update",
                        "entity": "Products",
                        "primaryKey": { "ProductID": 1 },
                        "fields": [
                          { "name": "ProductID", "label": "Product ID", "currentValue": 1, "proposedValue": 1, "isPrimaryKey": true },
                          { "name": "UnitPrice", "label": "Price", "currentValue": 18.0, "proposedValue": 19.99 }
                        ]
                      }
                    }
                    """)
            })
        });

        var service = CreateService(llmProvider, mcpClient);
        var result = await service.RunQueryAsync("http://localhost:9999", "change price of Chai to 19.99");

        Assert.Equal(UiSpecType.Form, result.Type);
        Assert.NotNull(result.Form);
        Assert.Equal("update", result.Form.Operation);
        Assert.Equal("Products", result.Form.Entity);
        Assert.Equal(2, result.Form.Fields.Count);
        Assert.Equal("UnitPrice", result.Form.Fields[1].Name);
        Assert.Equal(19.99, result.Form.Fields[1].ProposedValue);
    }

    [Fact]
    public async Task RunQueryAsync_FiltersOutMutationToolsFromLlm()
    {
        var tools = new[]
        {
            new McpTool("read_records", "Reads records", null),
            new McpTool("create_record", "Creates records", null),
            new McpTool("update_record", "Updates records", null),
            new McpTool("delete_record", "Deletes records", null),
            new McpTool("describe_entities", "Describes entities", null)
        };

        var mcpClient = new FakeMcpClient(tools, new());
        var llmProvider = new FakeLlmProvider(new[]
        {
            new LlmCompletion(null, new[]
            {
                new LlmToolCall("call-1", "render_result", """{"type":"stat","title":"Test","columns":[],"rows":[],"meta":{"sourceEntity":""}}""")
            })
        });

        var service = CreateService(llmProvider, mcpClient);
        await service.RunQueryAsync("http://localhost:9999", "test query");

        var toolsPassedToLlm = llmProvider.LastTools;
        Assert.NotNull(toolsPassedToLlm);
        Assert.Contains(toolsPassedToLlm, t => t.Name == "read_records");
        Assert.Contains(toolsPassedToLlm, t => t.Name == "describe_entities");
        Assert.Contains(toolsPassedToLlm, t => t.Name == "render_result");
        Assert.DoesNotContain(toolsPassedToLlm, t => t.Name == "create_record");
        Assert.DoesNotContain(toolsPassedToLlm, t => t.Name == "update_record");
        Assert.DoesNotContain(toolsPassedToLlm, t => t.Name == "delete_record");
    }

    [Fact]
    public async Task RunQueryAsync_ParsesChart_WithStringColumnsAndTolerantChartType()
    {
        var mcpClient = new FakeMcpClient(SampleTools, new());
        var llmProvider = new FakeLlmProvider(new[]
        {
            new LlmCompletion(null, new[]
            {
                new LlmToolCall(
                    "call-1",
                    "render_result",
                    """
                    {
                      "type": "chart",
                      "title": "Monthly Volume",
                      "chartType": "column",
                      "columns": ["Month", "Volume"],
                      "rows": [
                        { "Month": "Jan 1997", "Volume": 45 },
                        { "Month": "Feb 1997", "Volume": 52 }
                      ],
                      "meta": { "sourceEntity": "Orders" }
                    }
                    """)
            })
        });

        var service = CreateService(llmProvider, mcpClient);
        var result = await service.RunQueryAsync("http://localhost:9999", "Plot monthly order volume as a bar chart");

        Assert.Equal(UiSpecType.Chart, result.Type);
        Assert.Equal(UiSpecChartType.Bar, result.ChartType);
        Assert.Equal(2, result.Columns.Count);
        Assert.Equal("Month", result.Columns[0].Key);
        Assert.Equal(2, result.Rows.Count);
    }

    [Fact]
    public async Task RunQueryAsync_GeneratesFallbackUiSpec_WhenDataRetrievedBeforeMaxIterations()
    {
        var mcpClient = new FakeMcpClient(SampleTools, new()
        {
            ["aggregate_records"] = new McpToolCallResult(false, """
                {
                  "entity": "Orders",
                  "result": {
                    "items": [
                      { "ShipCountry": "USA", "count": 122 },
                      { "ShipCountry": "Germany", "count": 122 }
                    ]
                  }
                }
                """)
        });

        // Simulates an LLM that retrieves data on turn 1, but loops on turn 2 without calling render_result
        var completions = new[]
        {
            new LlmCompletion(null, new[] { new LlmToolCall("call-1", "aggregate_records", "{}") }),
            new LlmCompletion(null, new[] { new LlmToolCall("call-2", "aggregate_records", "{}") })
        };

        var llmProvider = new FakeLlmProvider(completions);
        var service = new OrchestrationService(
            llmProvider,
            new FakeMcpClientFactory(mcpClient),
            Options.Create(new OrchestrationOptions { MaxToolCallIterations = 2 }));

        var result = await service.RunQueryAsync("http://localhost:9999", "Plot orders by country as a bar chart");

        Assert.Equal(UiSpecType.Chart, result.Type);
        Assert.Equal(UiSpecChartType.Bar, result.ChartType);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("Orders", result.Meta.SourceEntity);
    }
}

