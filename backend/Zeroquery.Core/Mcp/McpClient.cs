using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Zeroquery.Core.Mcp;

/// <summary>
/// Minimal MCP client speaking DAB's "streamable HTTP" transport (protocol version
/// 2025-06-18), confirmed live against a running DAB 2.0 SQL MCP Server instance:
///
/// - Every request is a single JSON-RPC 2.0 object POSTed to <c>{baseUrl}/mcp</c> with
///   <c>Accept: application/json, text/event-stream</c>.
/// - DAB always responds with <c>Content-Type: text/event-stream</c> (even for a single,
///   non-streamed reply) as a single <c>event: message\ndata: {json}\n\n</c> frame — this
///   client parses that one frame and does not attempt true multi-event streaming, since
///   DAB's DML tools return one reply per call.
/// - <c>initialize</c> returns an <c>Mcp-Session-Id</c> response header that MUST be sent
///   back as a request header on every subsequent call on this connection.
/// - <c>notifications/initialized</c> is a notification (no <c>id</c>) and gets a bare 202
///   Accepted with no body — required by the MCP handshake before tool calls are answered.
/// - Tool call results arrive as <c>result.content[0].text</c>, a JSON-encoded string of the
///   tool's actual payload (not a native JSON object) — callers must parse that text
///   themselves as JSON if they need structured access to it.
///
/// One <see cref="McpClient"/> instance is scoped to one DAB instance's session; create a
/// new instance (and re-run <see cref="InitializeAsync"/>) if a session becomes invalid.
/// </summary>
public sealed class McpClient : IMcpClient
{
    private const string ProtocolVersion = "2025-06-18";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private string? _sessionId;
    private int _nextRequestId = 1;

    public McpClient(string baseUrl, HttpClient? httpClient = null)
    {
        if (httpClient is null)
        {
            _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
            _httpClient.BaseAddress = new Uri(baseUrl);
            _ownsHttpClient = false;
        }
    }

    /// <summary>
    /// Performs the MCP handshake: <c>initialize</c> (captures the session id) followed by
    /// the required <c>notifications/initialized</c> notification. Must be called once
    /// before <see cref="ListToolsAsync"/> or <see cref="CallToolAsync"/>.
    /// </summary>
    public async Task InitializeAsync(string clientName, string clientVersion, CancellationToken cancellationToken = default)
    {
        var initParams = new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject
            {
                ["name"] = clientName,
                ["version"] = clientVersion
            }
        };

        await SendRequestAsync("initialize", initParams, cancellationToken).ConfigureAwait(false);

        // Notification: no "id" field, DAB responds 202 with an empty body.
        await SendNotificationAsync("notifications/initialized", new JsonObject(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Calls <c>tools/list</c> and returns every MCP tool the current session/role can see.</summary>
    public async Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync("tools/list", new JsonObject(), cancellationToken).ConfigureAwait(false);

        var toolsArray = result?["tools"] as JsonArray
            ?? throw new McpClientException("tools/list response did not contain a 'tools' array.");

        var tools = new List<McpTool>(toolsArray.Count);
        foreach (var node in toolsArray)
        {
            if (node is not JsonObject obj)
            {
                continue;
            }

            var name = obj["name"]?.GetValue<string>()
                ?? throw new McpClientException("A tool in tools/list response is missing 'name'.");
            var description = obj["description"]?.GetValue<string>();
            var inputSchema = obj["inputSchema"]?.DeepClone();

            tools.Add(new McpTool(name, description, inputSchema));
        }

        return tools;
    }

    /// <summary>
    /// Calls <c>tools/call</c> for <paramref name="toolName"/> with <paramref name="arguments"/>.
    /// Returns the raw text payload DAB embeds in <c>result.content[0].text</c> — parse it as
    /// JSON yourself if you need structured access (it's typically a JSON object as text).
    /// </summary>
    public async Task<McpToolCallResult> CallToolAsync(
        string toolName,
        JsonObject arguments,
        CancellationToken cancellationToken = default)
    {
        var callParams = new JsonObject
        {
            ["name"] = toolName,
            ["arguments"] = arguments
        };

        var result = await SendRequestAsync("tools/call", callParams, cancellationToken).ConfigureAwait(false);

        var isError = result?["isError"]?.GetValue<bool>() ?? false;
        var contentArray = result?["content"] as JsonArray;
        var firstText = contentArray?
            .OfType<JsonObject>()
            .FirstOrDefault(c => c["type"]?.GetValue<string>() == "text")?["text"]?.GetValue<string>();

        return new McpToolCallResult(isError, firstText ?? string.Empty);
    }

    private async Task SendNotificationAsync(string method, JsonObject @params, CancellationToken cancellationToken)
    {
        var envelope = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = @params
        };

        using var request = BuildRequest(envelope);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new McpClientException($"MCP notification '{method}' failed with status {(int)response.StatusCode}.");
        }
    }

    private async Task<JsonObject?> SendRequestAsync(string method, JsonObject @params, CancellationToken cancellationToken)
    {
        var requestId = _nextRequestId++;
        var envelope = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = requestId,
            ["method"] = method,
            ["params"] = @params
        };

        using var request = BuildRequest(envelope);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (method == "initialize" && response.Headers.TryGetValues("Mcp-Session-Id", out var sessionValues))
        {
            _sessionId = sessionValues.FirstOrDefault();
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new McpClientException($"MCP request '{method}' failed with status {(int)response.StatusCode}: {body}");
        }

        var payload = ExtractJsonRpcPayload(body);
        var jsonRpc = JsonNode.Parse(payload) as JsonObject
            ?? throw new McpClientException($"MCP response for '{method}' was not a JSON object: {payload}");

        if (jsonRpc["error"] is JsonObject errorObj)
        {
            var message = errorObj["message"]?.GetValue<string>() ?? "Unknown MCP error.";
            throw new McpClientException($"MCP request '{method}' returned an error: {message}");
        }

        return jsonRpc["result"] as JsonObject;
    }

    private HttpRequestMessage BuildRequest(JsonObject envelope)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "mcp")
        {
            Content = new StringContent(envelope.ToJsonString(), Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (_sessionId is not null)
        {
            request.Headers.Add("Mcp-Session-Id", _sessionId);
        }

        return request;
    }

    /// <summary>
    /// DAB's streamable HTTP transport always replies with a single SSE frame of the form
    /// <c>event: message\ndata: {json}\n\n</c> — extracts the JSON payload from the "data:"
    /// line. Falls back to treating the whole body as raw JSON for robustness (e.g. if a
    /// future DAB version replies with plain <c>application/json</c> instead).
    /// </summary>
    private static string ExtractJsonRpcPayload(string sseOrJsonBody)
    {
        foreach (var line in sseOrJsonBody.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith("data:", StringComparison.Ordinal))
            {
                return trimmed["data:".Length..].Trim();
            }
        }

        return sseOrJsonBody.Trim();
    }

    public ValueTask DisposeAsync()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
