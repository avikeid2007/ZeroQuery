using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Zeroquery.Core.Orchestration;

/// <summary>
/// <see cref="ILlmProvider"/> implementation against OpenRouter's OpenAI-compatible chat
/// completions API (doc/Plan.md Section 2.7 / Section 8). OpenRouter mirrors the OpenAI
/// <c>/chat/completions</c> request/response shape including the "tools"/"tool_calls"
/// function-calling contract, so this implementation follows that shape directly rather
/// than depending on an OpenAI SDK package. Because nearly every alternative provider
/// (OpenAI, Azure OpenAI, Groq, Together, DeepSeek, a local Ollama/LM Studio server, etc.)
/// speaks this same shape, <see cref="LlmSettings.BaseUrl"/> can be overridden via Settings
/// to target any of them without needing per-vendor implementations.
/// </summary>
public sealed class OpenRouterLlmProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly OpenRouterOptions _options;
    private readonly ILlmSettingsStore _settingsStore;

    public OpenRouterLlmProvider(HttpClient httpClient, IOptions<OpenRouterOptions> options, ILlmSettingsStore settingsStore)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _settingsStore = settingsStore;
    }

    public async Task<LlmCompletion> CompleteAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<LlmTool> tools,
        CancellationToken cancellationToken = default)
    {
        // ApiKey/ModelId are read from the mutable settings store (seeded from OpenRouterOptions
        // at startup, but updatable at runtime via the settings UI — see LlmSettingsStore) rather
        // than the IOptions snapshot directly, so a user editing settings takes effect immediately
        // without an app restart.
        var settings = _settingsStore.Current;

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException(
                "LLM API key is not configured. Set it in Settings, or via OpenRouter:ApiKey / the OPENROUTER_API_KEY env var.");
        }

        // BaseUrl may be overridden per-request via Settings to target any OpenAI-compatible
        // provider (OpenAI, Azure OpenAI, Groq, Together, DeepSeek, a local Ollama/LM Studio
        // server, etc.) instead of the default OpenRouter endpoint.
        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? _options.BaseUrl : settings.BaseUrl;

        var requestBody = new JsonObject
        {
            ["model"] = settings.ModelId,
            ["messages"] = BuildMessagesArray(messages),
        };

        if (tools.Count > 0)
        {
            requestBody["tools"] = BuildToolsArray(tools);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(requestBody)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        if (!string.IsNullOrWhiteSpace(_options.HttpReferer))
        {
            request.Headers.Add("HTTP-Referer", _options.HttpReferer);
        }
        if (!string.IsNullOrWhiteSpace(_options.AppTitle))
        {
            request.Headers.Add("X-Title", _options.AppTitle);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"LLM provider request to {baseUrl} failed with status {(int)response.StatusCode}: {body}");
        }

        return ParseCompletion(body);
    }

    private static JsonArray BuildMessagesArray(IReadOnlyList<LlmMessage> messages)
    {
        var array = new JsonArray();
        foreach (var message in messages)
        {
            var obj = new JsonObject { ["role"] = message.Role };

            if (message.Content is not null)
            {
                obj["content"] = message.Content;
            }

            if (message.ToolCallId is not null)
            {
                obj["tool_call_id"] = message.ToolCallId;
            }

            if (message.ToolCalls is { Count: > 0 })
            {
                var toolCallsArray = new JsonArray();
                foreach (var call in message.ToolCalls)
                {
                    toolCallsArray.Add(new JsonObject
                    {
                        ["id"] = call.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = call.Name,
                            ["arguments"] = call.ArgumentsJson
                        }
                    });
                }
                obj["tool_calls"] = toolCallsArray;
            }

            array.Add(obj);
        }
        return array;
    }

    private static JsonArray BuildToolsArray(IReadOnlyList<LlmTool> tools)
    {
        var array = new JsonArray();
        foreach (var tool in tools)
        {
            array.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description ?? string.Empty,
                    ["parameters"] = tool.ParametersSchema?.DeepClone() ?? new JsonObject { ["type"] = "object" }
                }
            });
        }
        return array;
    }

    private static LlmCompletion ParseCompletion(string responseBody)
    {
        var root = JsonNode.Parse(responseBody) as JsonObject
            ?? throw new InvalidOperationException("OpenRouter response was not a JSON object.");

        var message = root["choices"]?[0]?["message"] as JsonObject
            ?? throw new InvalidOperationException($"OpenRouter response missing choices[0].message: {responseBody}");

        var content = message["content"]?.GetValue<string>();
        var toolCallsNode = message["tool_calls"] as JsonArray;

        var toolCalls = new List<LlmToolCall>();
        if (toolCallsNode is not null)
        {
            foreach (var node in toolCallsNode)
            {
                if (node is not JsonObject callObj)
                {
                    continue;
                }

                var id = callObj["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("n");
                var function = callObj["function"] as JsonObject;
                var name = function?["name"]?.GetValue<string>()
                    ?? throw new InvalidOperationException("OpenRouter tool call missing function.name.");
                var arguments = function["arguments"]?.GetValue<string>() ?? "{}";

                toolCalls.Add(new LlmToolCall(id, name, arguments));
            }
        }

        return new LlmCompletion(content, toolCalls);
    }
}
