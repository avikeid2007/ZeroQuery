using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ILlmSettingsStore? _settingsStore;
    private readonly ILogger<OrchestrationService> _logger;

    public OrchestrationService(
        ILlmProvider llmProvider,
        IMcpClientFactory mcpClientFactory,
        IOptions<OrchestrationOptions> options,
        ILlmSettingsStore? settingsStore = null,
        ILogger<OrchestrationService>? logger = null)
    {
        _llmProvider = llmProvider;
        _mcpClientFactory = mcpClientFactory;
        _options = options.Value;
        _settingsStore = settingsStore;
        _logger = logger ?? NullLogger<OrchestrationService>.Instance;
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
            LlmMessage.System(ResolveSystemPrompt()),
            LlmMessage.User(userPrompt)
        };

        string? lastSuccessfulToolResult = null;
        string? lastToolName = null;

        for (var iteration = 0; iteration < _options.MaxToolCallIterations; iteration++)
        {
            if (iteration == _options.MaxToolCallIterations - 1)
            {
                // We are on the final allowed iteration! Nudge the model firmly to call render_result now.
                _logger.LogWarning("LLM reached iteration {Iteration} of {Max}. Emitting final render_result nudge.", iteration + 1, _options.MaxToolCallIterations);
                messages.Add(LlmMessage.User(
                    $"WARNING: You are on your final tool iteration (iteration {iteration + 1} of {_options.MaxToolCallIterations}). You MUST call '{RenderResultToolName}' now with your best data or summary. Do NOT call any more data tools."));
            }

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

                if (!resultJson.Contains("\"status\":\"error\"") && !resultJson.Contains("\"isError\":true"))
                {
                    lastSuccessfulToolResult = resultJson;
                    lastToolName = toolCall.Name;
                }

                messages.Add(LlmMessage.ToolResult(toolCall.Id, resultJson));
            }
        }

        // If iteration limit was hit, attempt to synthesize a fallback UiSpec if we retrieved data
        if (lastSuccessfulToolResult is not null)
        {
            var fallbackSpec = TryBuildFallbackUiSpec(lastSuccessfulToolResult, lastToolName, userPrompt);
            if (fallbackSpec is not null)
            {
                _logger.LogInformation("Built fallback UiSpec from last tool result '{ToolName}'.", lastToolName);
                await ReportAsync(onProgress, OrchestrationStage.Rendering, null, cancellationToken).ConfigureAwait(false);
                return fallbackSpec;
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
        "formatted for display. For type 'chart', provide chartType ('bar', 'line', or 'pie'), " +
        "category column first, numeric series column(s) second, and rows with numerical values.";

    private static readonly JsonNode RenderResultSchema = JsonNode.Parse("""
        {
          "type": "object",
          "required": ["type", "title"],
          "properties": {
            "type": { "type": "string", "enum": ["table", "chart", "card", "stat", "form"] },
            "title": { "type": "string" },
            "columns": {
              "type": "array",
              "items": {
                "type": "object",
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

    private string ResolveSystemPrompt()
    {
        if (_settingsStore is not null && !string.IsNullOrWhiteSpace(_settingsStore.Current.SystemPrompt))
        {
            return _settingsStore.Current.SystemPrompt;
        }

        return LlmSettingsStore.DefaultSystemPrompt;
    }

    private static UiSpec ParseUiSpec(string argumentsJson)
    {
        var root = JsonNode.Parse(argumentsJson) as JsonObject
            ?? throw new InvalidOperationException("render_result arguments were not a JSON object.");

        var typeString = root["type"]?.GetValue<string>()
            ?? throw new InvalidOperationException("render_result is missing required 'type'.");

        UiSpecType type;
        if (typeString.Contains("chart", StringComparison.OrdinalIgnoreCase) ||
            typeString.Contains("graph", StringComparison.OrdinalIgnoreCase) ||
            typeString.Contains("plot", StringComparison.OrdinalIgnoreCase))
        {
            type = UiSpecType.Chart;
        }
        else if (typeString.Contains("stat", StringComparison.OrdinalIgnoreCase) ||
                 typeString.Contains("kpi", StringComparison.OrdinalIgnoreCase) ||
                 typeString.Contains("metric", StringComparison.OrdinalIgnoreCase))
        {
            type = UiSpecType.Stat;
        }
        else if (typeString.Contains("card", StringComparison.OrdinalIgnoreCase))
        {
            type = UiSpecType.Card;
        }
        else if (typeString.Contains("form", StringComparison.OrdinalIgnoreCase) ||
                 typeString.Contains("mutation", StringComparison.OrdinalIgnoreCase))
        {
            type = UiSpecType.Form;
        }
        else if (Enum.TryParse<UiSpecType>(typeString, ignoreCase: true, out var parsedType))
        {
            type = parsedType;
        }
        else
        {
            type = UiSpecType.Table;
        }

        var title = root["title"]?.GetValue<string>() ?? "Result";

        var columns = new List<UiSpecColumn>();
        if (root["columns"] is JsonArray colArray)
        {
            foreach (var item in colArray)
            {
                if (item is JsonObject obj)
                {
                    var key = obj["key"]?.GetValue<string>() ?? obj["name"]?.GetValue<string>() ?? string.Empty;
                    var label = obj["label"]?.GetValue<string>() ?? obj["title"]?.GetValue<string>() ?? key;
                    columns.Add(new UiSpecColumn(key, label));
                }
                else if (item is JsonValue val && val.TryGetValue(out string? strVal))
                {
                    columns.Add(new UiSpecColumn(strVal, strVal));
                }
            }
        }

        var rows = (root["rows"] as JsonArray)?
            .OfType<JsonObject>()
            .Select(ToRowDictionary)
            .ToList() ?? new List<Dictionary<string, object?>>();

        if (columns.Count == 0 && rows.Count > 0)
        {
            columns = rows[0].Keys.Select(k => new UiSpecColumn(k, k)).ToList();
        }

        UiSpecChartType? chartType = null;
        if (root["chartType"]?.GetValue<string>() is { } chartTypeString)
        {
            if (chartTypeString.Contains("bar", StringComparison.OrdinalIgnoreCase) ||
                chartTypeString.Contains("column", StringComparison.OrdinalIgnoreCase))
            {
                chartType = UiSpecChartType.Bar;
            }
            else if (chartTypeString.Contains("line", StringComparison.OrdinalIgnoreCase) ||
                     chartTypeString.Contains("area", StringComparison.OrdinalIgnoreCase))
            {
                chartType = UiSpecChartType.Line;
            }
            else if (chartTypeString.Contains("pie", StringComparison.OrdinalIgnoreCase) ||
                     chartTypeString.Contains("donut", StringComparison.OrdinalIgnoreCase))
            {
                chartType = UiSpecChartType.Pie;
            }
            else if (Enum.TryParse<UiSpecChartType>(chartTypeString, ignoreCase: true, out var parsedChart))
            {
                chartType = parsedChart;
            }
            else
            {
                chartType = UiSpecChartType.Bar;
            }
        }
        else if (type == UiSpecType.Chart)
        {
            chartType = UiSpecChartType.Bar;
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

    private static UiSpec? TryBuildFallbackUiSpec(string toolResultJson, string? toolName, string userPrompt)
    {
        try
        {
            var node = JsonNode.Parse(toolResultJson);
            if (node is null) return null;

            JsonArray? rowsArray = null;
            string sourceEntity = string.Empty;

            if (node is JsonObject obj)
            {
                sourceEntity = obj["entity"]?.GetValue<string>() ?? string.Empty;

                if (obj["result"] is JsonObject resObj && resObj["items"] is JsonArray itemsArr)
                {
                    rowsArray = itemsArr;
                }
                else if (obj["result"] is JsonArray resArr)
                {
                    rowsArray = resArr;
                }
                else if (obj["value"] is JsonArray valArr)
                {
                    rowsArray = valArr;
                }
            }
            else if (node is JsonArray arr)
            {
                rowsArray = arr;
            }

            if (rowsArray is null || rowsArray.Count == 0) return null;

            var rows = rowsArray
                .OfType<JsonObject>()
                .Select(ToRowDictionary)
                .ToList();

            if (rows.Count == 0) return null;

            var columns = rows[0].Keys.Select(k => new UiSpecColumn(k, k)).ToList();

            var isChartQuery = userPrompt.Contains("chart", StringComparison.OrdinalIgnoreCase) ||
                               userPrompt.Contains("plot", StringComparison.OrdinalIgnoreCase) ||
                               userPrompt.Contains("bar", StringComparison.OrdinalIgnoreCase) ||
                               userPrompt.Contains("line", StringComparison.OrdinalIgnoreCase) ||
                               userPrompt.Contains("pie", StringComparison.OrdinalIgnoreCase) ||
                               userPrompt.Contains("graph", StringComparison.OrdinalIgnoreCase);

            var type = isChartQuery ? UiSpecType.Chart : UiSpecType.Table;
            var chartType = isChartQuery ? UiSpecChartType.Bar : (UiSpecChartType?)null;

            var title = userPrompt.Length > 60 ? userPrompt[..60] + "…" : userPrompt;

            return new UiSpec(
                type,
                title,
                columns,
                rows,
                chartType,
                new UiSpecMeta(sourceEntity, DateTimeOffset.UtcNow));
        }
        catch
        {
            return null;
        }
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
