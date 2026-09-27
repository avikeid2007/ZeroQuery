using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Zeroquery.Core.Mcp;

namespace Zeroquery.Core.Orchestration;

/// <summary>
/// The prompt -&gt; tool-calling loop -&gt; UI Spec pipeline (doc/Plan.md Section 2.6/2.7,
/// Phase 4 "Orchestration skeleton"). For a given running DAB MCP instance:
///
/// 1. Opens an MCP session and lists its tools (describe_entities, read_records, etc).
/// 2. Exposes those tools to the LLM verbatim, PLUS one synthetic <c>render_result</c> tool
///    whose parameters ARE the <see cref="UiSpec"/> shape — this is how the LLM emits
///    structured final output instead of loose freeform text, since OpenAI-style
///    tool-calling gives much more reliable structured output than asking a model to "return
///    JSON" in message content.
/// 3. Loops: LLM asks for a tool call -&gt; we execute it against MCP -&gt; feed the result back
///    -&gt; repeat, until the LLM calls <c>render_result</c> (success) or the iteration cap is
///    hit (doc/Plan.md's "DAB's ceiling on complex queries" limitation, Section 9).
///
/// Read-only in Phase 4 — DAB DML tools other than read_records/aggregate_records are only
/// reachable if the underlying entity's permissions allow it (Zeroquery defaults to
/// read-only per Section 3), so write tools are effectively inert until Phase 8's
/// confirm-before-execute flow exists.
/// </summary>
public sealed class OrchestrationService
{
    private const string RenderResultToolName = "render_result";

    private readonly ILlmProvider _llmProvider;
    private readonly IMcpClientFactory _mcpClientFactory;
    private readonly OrchestrationOptions _options;

    public OrchestrationService(ILlmProvider llmProvider, IMcpClientFactory mcpClientFactory, IOptions<OrchestrationOptions> options)
    {
        _llmProvider = llmProvider;
        _mcpClientFactory = mcpClientFactory;
        _options = options.Value;
    }

    /// <summary>
    /// Runs one query end-to-end against the DAB instance at <paramref name="mcpBaseUrl"/>.
    /// Opens (and disposes) a fresh MCP session per call — Phase 4 scope; a future phase may
    /// pool sessions per instance if per-query handshake overhead matters at scale.
    /// </summary>
    public Task<UiSpec> RunQueryAsync(string mcpBaseUrl, string userPrompt, CancellationToken cancellationToken = default) =>
        RunQueryAsync(mcpBaseUrl, userPrompt, onProgress: null, cancellationToken);

    /// <summary>
    /// Same as <see cref="RunQueryAsync(string, string, CancellationToken)"/>, but invokes
    /// <paramref name="onProgress"/> at each stage of the loop (doc/Plan.md Phase 5 — SSE
    /// streaming) so a caller can surface live progress instead of a static spinner while
    /// the LLM thinks and calls MCP tools. <paramref name="onProgress"/> is optional and may
    /// be <c>null</c>, in which case this behaves identically to the non-streaming overload.
    /// </summary>
    public async Task<UiSpec> RunQueryAsync(
        string mcpBaseUrl,
        string userPrompt,
        OrchestrationProgressCallback? onProgress,
        CancellationToken cancellationToken = default)
    {
        var mcpClient = _mcpClientFactory.Create(mcpBaseUrl);
        await using var _ = mcpClient;
        await mcpClient.InitializeAsync("zeroquery-orchestrator", "0.1.0", cancellationToken).ConfigureAwait(false);

        var mcpTools = await mcpClient.ListToolsAsync(cancellationToken).ConfigureAwait(false);
        var llmTools = BuildLlmTools(mcpTools);

        var messages = new List<LlmMessage>
        {
            LlmMessage.System(BuildSystemPrompt()),
            LlmMessage.User(userPrompt)
        };

        for (var iteration = 0; iteration < _options.MaxToolCallIterations; iteration++)
        {
            await ReportAsync(onProgress, OrchestrationStage.Thinking, null, cancellationToken).ConfigureAwait(false);
            var completion = await _llmProvider.CompleteAsync(messages, llmTools, cancellationToken).ConfigureAwait(false);

            if (completion.ToolCalls.Count == 0)
            {
                // Model answered in plain text instead of calling render_result — nudge it
                // once rather than failing outright, since some models occasionally skip the
                // final tool call on simple prompts.
                messages.Add(LlmMessage.Assistant(completion.Content));
                messages.Add(LlmMessage.User(
                    $"Call the '{RenderResultToolName}' tool now with your final answer — do not respond in plain text."));
                continue;
            }

            messages.Add(LlmMessage.Assistant(completion.Content, completion.ToolCalls));

            foreach (var toolCall in completion.ToolCalls)
            {
                if (toolCall.Name == RenderResultToolName)
                {
                    await ReportAsync(onProgress, OrchestrationStage.Rendering, null, cancellationToken).ConfigureAwait(false);
                    return ParseUiSpec(toolCall.ArgumentsJson);
                }

                await ReportAsync(onProgress, OrchestrationStage.ToolCall, toolCall.Name, cancellationToken).ConfigureAwait(false);
                var resultJson = await ExecuteMcpToolCallAsync(mcpClient, toolCall, cancellationToken).ConfigureAwait(false);
                await ReportAsync(onProgress, OrchestrationStage.ToolResult, toolCall.Name, cancellationToken).ConfigureAwait(false);
                messages.Add(LlmMessage.ToolResult(toolCall.Id, resultJson));
            }
        }

        throw new InvalidOperationException(
            $"Query did not resolve to a result within {_options.MaxToolCallIterations} tool-call iterations.");
    }

    private static Task ReportAsync(
        OrchestrationProgressCallback? onProgress,
        OrchestrationStage stage,
        string? toolName,
        CancellationToken cancellationToken) =>
        onProgress is null
            ? Task.CompletedTask
            : onProgress(new OrchestrationProgressEvent(stage, toolName), cancellationToken);

    private static async Task<string> ExecuteMcpToolCallAsync(IMcpClient mcpClient, LlmToolCall toolCall, CancellationToken cancellationToken)
    {
        JsonObject arguments;
        try
        {
            arguments = JsonNode.Parse(toolCall.ArgumentsJson) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return """{"status":"error","message":"Tool call arguments were not valid JSON."}""";
        }

        try
        {
            var result = await mcpClient.CallToolAsync(toolCall.Name, arguments, cancellationToken).ConfigureAwait(false);
            return result.Text;
        }
        catch (McpClientException ex)
        {
            // Feed the failure back to the model as a tool result (not a thrown exception) so
            // it can retry with corrected arguments instead of the whole query blowing up.
            return JsonSerializer.Serialize(new { status = "error", message = ex.Message });
        }
    }

    private static readonly HashSet<string> MutationToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "create_record", "update_record", "delete_record", "execute_mutation"
    };

    private static List<LlmTool> BuildLlmTools(IReadOnlyList<McpTool> mcpTools)
    {
        // Guard against autonomous unconfirmed writes: only read-oriented tools are exposed to the LLM.
        var tools = mcpTools
            .Where(t => !IsMutationTool(t.Name))
            .Select(t => new LlmTool(t.Name, t.Description, t.InputSchema))
            .ToList();
        tools.Add(new LlmTool(RenderResultToolName, RenderResultDescription, RenderResultSchema));
        return tools;
    }

    private static bool IsMutationTool(string toolName) =>
        MutationToolNames.Contains(toolName) ||
        toolName.StartsWith("create_", StringComparison.OrdinalIgnoreCase) ||
        toolName.StartsWith("update_", StringComparison.OrdinalIgnoreCase) ||
        toolName.StartsWith("delete_", StringComparison.OrdinalIgnoreCase) ||
        toolName.StartsWith("insert_", StringComparison.OrdinalIgnoreCase) ||
        toolName.EndsWith("_create", StringComparison.OrdinalIgnoreCase) ||
        toolName.EndsWith("_update", StringComparison.OrdinalIgnoreCase) ||
        toolName.EndsWith("_delete", StringComparison.OrdinalIgnoreCase);

    private const string RenderResultDescription =
        "Call this exactly once, as your final step, to return the answer to the user. Do not " +
        "respond in plain text — always finish by calling this tool with the query results " +
        "formatted for display.";

    private static readonly JsonNode RenderResultSchema = JsonNode.Parse("""
        {
          "type": "object",
          "required": ["type", "title", "columns", "rows", "meta"],
          "properties": {
            "type": { "type": "string", "enum": ["table", "chart", "card", "stat", "form"] },
            "title": { "type": "string" },
            "columns": {
              "type": "array",
              "items": {
                "type": "object",
                "required": ["key", "label"],
                "properties": {
                  "key": { "type": "string" },
                  "label": { "type": "string" }
                }
              }
            },
            "rows": { "type": "array", "items": { "type": "object" } },
            "chartType": { "type": "string", "enum": ["bar", "line", "pie"] },
            "meta": {
              "type": "object",
              "required": ["sourceEntity"],
              "properties": {
                "sourceEntity": { "type": "string" }
              }
            },
            "form": {
              "type": "object",
              "required": ["operation", "entity", "fields"],
              "properties": {
                "operation": { "type": "string", "enum": ["create", "update", "delete"] },
                "entity": { "type": "string" },
                "primaryKey": { "type": "object" },
                "fields": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "required": ["name", "label"],
                    "properties": {
                      "name": { "type": "string" },
                      "label": { "type": "string" },
                      "currentValue": {},
                      "proposedValue": {},
                      "isPrimaryKey": { "type": "boolean" }
                    }
                  }
                }
              }
            }
          }
        }
        """)!;

    private static string BuildSystemPrompt() => """
        You are Zeroquery's query assistant. You answer questions and prepare database actions using
        the provided tools.
        - For questions about data: Always call 'describe_entities' first if you don't already know
          the exact entity/field names. Use 'read_records' to fetch real data — never guess at data
          you haven't retrieved. When you have the answer, call 'render_result' exactly once with
          type 'table', 'chart', 'card', or 'stat'.
        - For data modification requests (create, update, delete): Direct mutations are forbidden
          without explicit human confirmation. First inspect the entity with 'describe_entities'
          or 'read_records' (to find existing values/keys if updating or deleting). Then call
          'render_result' with type 'form', providing the 'form' object with 'operation'
          ('create' | 'update' | 'delete'), 'entity', 'primaryKey' (for update/delete), and 'fields'
          showing previous vs proposed values.
        - Never respond in plain text — always conclude by calling 'render_result'.
        """;

    private static UiSpec ParseUiSpec(string argumentsJson)
    {
        var root = JsonNode.Parse(argumentsJson) as JsonObject
            ?? throw new InvalidOperationException("render_result arguments were not a JSON object.");

        var typeString = root["type"]?.GetValue<string>()
            ?? throw new InvalidOperationException("render_result is missing required 'type'.");
        var type = Enum.Parse<UiSpecType>(typeString, ignoreCase: true);

        var title = root["title"]?.GetValue<string>() ?? "Result";

        var columns = (root["columns"] as JsonArray)?
            .OfType<JsonObject>()
            .Select(c => new UiSpecColumn(
                c["key"]?.GetValue<string>() ?? string.Empty,
                c["label"]?.GetValue<string>() ?? string.Empty))
            .ToList() ?? new List<UiSpecColumn>();

        var rows = (root["rows"] as JsonArray)?
            .OfType<JsonObject>()
            .Select(ToRowDictionary)
            .ToList() ?? new List<Dictionary<string, object?>>();

        UiSpecChartType? chartType = null;
        if (root["chartType"]?.GetValue<string>() is { } chartTypeString)
        {
            chartType = Enum.Parse<UiSpecChartType>(chartTypeString, ignoreCase: true);
        }

        var sourceEntity = root["meta"]?["sourceEntity"]?.GetValue<string>() ?? string.Empty;
        var meta = new UiSpecMeta(sourceEntity, DateTimeOffset.UtcNow);

        UiSpecForm? form = null;
        if (root["form"] is JsonObject formObj)
        {
            var op = formObj["operation"]?.GetValue<string>() ?? "update";
            var entity = formObj["entity"]?.GetValue<string>() ?? sourceEntity;
            var pk = formObj["primaryKey"] is JsonObject pkObj ? ToRowDictionary(pkObj) : null;
            var fields = (formObj["fields"] as JsonArray)?
                .OfType<JsonObject>()
                .Select(f => new UiSpecFormField(
                    f["name"]?.GetValue<string>() ?? string.Empty,
                    f["label"]?.GetValue<string>() ?? f["name"]?.GetValue<string>() ?? string.Empty,
                    ExtractValue(f["currentValue"]),
                    ExtractValue(f["proposedValue"]),
                    f["isPrimaryKey"]?.GetValue<bool>() ?? false))
                .ToList() ?? new List<UiSpecFormField>();

            form = new UiSpecForm(op, entity, pk, fields);
        }

        return new UiSpec(type, title, columns, rows, chartType, meta, form);
    }

    private static object? ExtractValue(JsonNode? node) => node switch
    {
        null => null,
        JsonValue v when v.TryGetValue(out string? s) => s,
        JsonValue v when v.TryGetValue(out double d) => d,
        JsonValue v when v.TryGetValue(out long l) => l,
        JsonValue v when v.TryGetValue(out bool b) => b,
        _ => node.ToJsonString()
    };

    private static Dictionary<string, object?> ToRowDictionary(JsonObject row)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var (key, value) in row)
        {
            dict[key] = ExtractValue(value);
        }
        return dict;
    }
}
