using Zeroquery.Core.Orchestration;

namespace Zeroquery.Api.Settings;

/// <summary>Response for GET /api/settings/llm — never includes the raw API key.</summary>
public sealed record LlmSettingsResponse(
    string ModelId,
    bool IsApiKeyConfigured,
    string? ApiKeyMasked,
    string? SystemPrompt,
    string DefaultSystemPrompt)
{
    public static LlmSettingsResponse From(LlmSettingsView view) => new(
        view.ModelId,
        view.IsApiKeyConfigured,
        view.ApiKeyMasked,
        view.SystemPrompt,
        view.DefaultSystemPrompt);
}

/// <summary>
/// Request for PUT /api/settings/llm. All fields optional/independent:
/// - Omit or send null for <see cref="ApiKey"/> to leave the current key unchanged.
/// - Send an empty string for <see cref="ApiKey"/> to explicitly clear it.
/// - Omit/empty <see cref="ModelId"/> to leave the current model unchanged.
/// - Send null for <see cref="SystemPrompt"/> to leave unchanged; empty string or "__RESET__" to reset to default.
/// </summary>
public sealed record UpdateLlmSettingsRequest(string? ApiKey, string? ModelId, string? SystemPrompt = null);
