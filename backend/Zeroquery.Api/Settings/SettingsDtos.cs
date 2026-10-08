using Zeroquery.Core.Orchestration;

namespace Zeroquery.Api.Settings;

/// <summary>Response for GET /api/settings/llm — never includes the raw API key.</summary>
public sealed record LlmSettingsResponse(
    string ModelId,
    bool IsApiKeyConfigured,
    string? ApiKeyMasked,
    string? SystemPrompt,
    string DefaultSystemPrompt,
    string? BaseUrl,
    string DefaultBaseUrl)
{
    public static LlmSettingsResponse From(LlmSettingsView view) => new(
        view.ModelId,
        view.IsApiKeyConfigured,
        view.ApiKeyMasked,
        view.SystemPrompt,
        view.DefaultSystemPrompt,
        view.BaseUrl,
        view.DefaultBaseUrl);
}

/// <summary>
/// Request for PUT /api/settings/llm. All fields optional/independent:
/// - Omit or send null for <see cref="ApiKey"/> to leave the current key unchanged.
/// - Send an empty string for <see cref="ApiKey"/> to explicitly clear it.
/// - Omit/empty <see cref="ModelId"/> to leave the current model unchanged.
/// - Send null for <see cref="SystemPrompt"/> to leave unchanged; empty string or "__RESET__" to reset to default.
/// - Send null for <see cref="BaseUrl"/> to leave unchanged; empty string or "__RESET__" to reset to the default
///   (OpenRouter) endpoint. Set to any OpenAI-compatible chat-completions base URL to switch providers.
/// </summary>
public sealed record UpdateLlmSettingsRequest(string? ApiKey, string? ModelId, string? SystemPrompt = null, string? BaseUrl = null);
