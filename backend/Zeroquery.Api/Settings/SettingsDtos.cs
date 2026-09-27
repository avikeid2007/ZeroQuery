using Zeroquery.Core.Orchestration;

namespace Zeroquery.Api.Settings;

/// <summary>Response for GET /api/settings/llm — never includes the raw API key.</summary>
public sealed record LlmSettingsResponse(string ModelId, bool IsApiKeyConfigured, string? ApiKeyMasked)
{
    public static LlmSettingsResponse From(LlmSettingsView view) => new(view.ModelId, view.IsApiKeyConfigured, view.ApiKeyMasked);
}

/// <summary>
/// Request for PUT /api/settings/llm. Both fields optional/independent:
/// - Omit or send null for <see cref="ApiKey"/> to leave the current key unchanged.
/// - Send an empty string for <see cref="ApiKey"/> to explicitly clear it.
/// - Omit/empty <see cref="ModelId"/> to leave the current model unchanged.
/// </summary>
public sealed record UpdateLlmSettingsRequest(string? ApiKey, string? ModelId);
