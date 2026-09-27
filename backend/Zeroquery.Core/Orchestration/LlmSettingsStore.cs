using Microsoft.Extensions.Options;

namespace Zeroquery.Core.Orchestration;

/// <summary>Current, possibly runtime-updated LLM provider settings.</summary>
/// <param name="ApiKey">Raw API key. Never serialize this back to a client — see <see cref="ILlmSettingsStore.GetMasked"/>.</param>
/// <param name="ModelId">Model id to request from the provider.</param>
public sealed record LlmSettings(string ApiKey, string ModelId);

/// <summary>Safe-to-expose view of <see cref="LlmSettings"/> — the raw key is never included.</summary>
/// <param name="ModelId">Currently configured model id.</param>
/// <param name="IsApiKeyConfigured">Whether an API key is currently set (by env var, config, or the settings UI).</param>
/// <param name="ApiKeyMasked">Last 4 characters of the key (e.g. "••••av3x"), or null if none is configured.</param>
public sealed record LlmSettingsView(string ModelId, bool IsApiKeyConfigured, string? ApiKeyMasked);

/// <summary>
/// Holds the LLM provider settings (API key, model id) that <see cref="OpenRouterLlmProvider"/>
/// reads on every call. Seeded at startup from <see cref="OpenRouterOptions"/> (env vars /
/// appsettings — see doc/Plan.md Section 8), but can be updated at runtime through the
/// settings UI without restarting the backend, so self-hosters don't have to touch
/// environment variables or config files at all if they don't want to.
///
/// Runtime updates are process-local and in-memory only — they don't persist across an app
/// restart, and only apply to this single backend instance. That's an intentional v1 scope
/// limit; if persistence across restarts is needed later, this is the seam to add it behind
/// (e.g. write-through to a local settings file or secret store).
/// </summary>
public interface ILlmSettingsStore
{
    LlmSettings Current { get; }

    /// <summary>Safe-to-expose view for API responses — never leaks the raw key.</summary>
    LlmSettingsView GetMasked();

    /// <summary>
    /// Updates settings. Pass null to leave a field unchanged; pass an empty string for
    /// <paramref name="apiKey"/> to explicitly clear it.
    /// </summary>
    void Update(string? apiKey, string? modelId);
}

public sealed class LlmSettingsStore : ILlmSettingsStore
{
    private readonly Lock _lock = new();
    private LlmSettings _current;

    public LlmSettingsStore(IOptions<OpenRouterOptions> options)
    {
        _current = new LlmSettings(options.Value.ApiKey, options.Value.ModelId);
    }

    public LlmSettings Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    public LlmSettingsView GetMasked()
    {
        var current = Current;
        var isConfigured = !string.IsNullOrWhiteSpace(current.ApiKey);
        var masked = isConfigured ? Mask(current.ApiKey) : null;
        return new LlmSettingsView(current.ModelId, isConfigured, masked);
    }

    public void Update(string? apiKey, string? modelId)
    {
        lock (_lock)
        {
            var nextApiKey = apiKey ?? _current.ApiKey;
            var nextModelId = string.IsNullOrWhiteSpace(modelId) ? _current.ModelId : modelId;
            _current = new LlmSettings(nextApiKey, nextModelId);
        }
    }

    private static string Mask(string apiKey)
    {
        const int visibleSuffixLength = 4;
        if (apiKey.Length <= visibleSuffixLength)
        {
            return new string('•', apiKey.Length);
        }

        return new string('•', 8) + apiKey[^visibleSuffixLength..];
    }
}
