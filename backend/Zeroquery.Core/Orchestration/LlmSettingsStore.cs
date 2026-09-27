using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Zeroquery.Core.Persistence;
using Zeroquery.Core.Security.DataProtection;

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
/// settings UI. When <see cref="PersistenceOptions.IsSaveMode"/> is true, settings persist
/// to disk encrypted across backend restarts (Phase 7).
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
    private sealed record PersistedSettings(string? EncryptedApiKey, string? ModelId);

    private readonly Lock _lock = new();
    private readonly PersistenceOptions? _persistenceOptions;
    private readonly IConnectionStringProtector? _protector;
    private readonly ILogger<LlmSettingsStore> _logger;
    private readonly string? _filePath;
    private LlmSettings _current;

    public LlmSettingsStore(
        IOptions<OpenRouterOptions> options,
        IOptions<PersistenceOptions>? persistenceOptions = null,
        IConnectionStringProtector? protector = null,
        ILogger<LlmSettingsStore>? logger = null)
    {
        _persistenceOptions = persistenceOptions?.Value;
        _protector = protector;
        _logger = logger ?? NullLogger<LlmSettingsStore>.Instance;

        _current = new LlmSettings(options.Value.ApiKey, options.Value.ModelId);

        if (_persistenceOptions?.IsSaveMode == true)
        {
            _filePath = Path.Combine(_persistenceOptions.StorageDirectory, "llm-settings.json");
            LoadFromDisk();
        }
    }

    private void LoadFromDisk()
    {
        if (_filePath is null || !File.Exists(_filePath)) return;

        try
        {
            var json = File.ReadAllText(_filePath);
            var persisted = JsonSerializer.Deserialize<PersistedSettings>(json);
            if (persisted is not null)
            {
                var apiKey = !string.IsNullOrWhiteSpace(persisted.EncryptedApiKey) && _protector is not null
                    ? _protector.Unprotect(persisted.EncryptedApiKey)
                    : _current.ApiKey;

                var modelId = !string.IsNullOrWhiteSpace(persisted.ModelId)
                    ? persisted.ModelId
                    : _current.ModelId;

                // Only override if startup config did not explicitly define them
                if (string.IsNullOrWhiteSpace(_current.ApiKey) && !string.IsNullOrWhiteSpace(apiKey))
                {
                    _current = _current with { ApiKey = apiKey };
                }

                if (!string.IsNullOrWhiteSpace(modelId))
                {
                    _current = _current with { ModelId = modelId };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load LLM settings from {FilePath}.", _filePath);
        }
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

            if (_persistenceOptions?.IsSaveMode == true && _filePath is not null)
            {
                SaveToDisk(nextApiKey, nextModelId);
            }
        }
    }

    private void SaveToDisk(string apiKey, string modelId)
    {
        try
        {
            if (_persistenceOptions is null || _filePath is null) return;
            Directory.CreateDirectory(_persistenceOptions.StorageDirectory);

            var encryptedKey = !string.IsNullOrWhiteSpace(apiKey) && _protector is not null
                ? _protector.Protect(apiKey)
                : null;

            var persisted = new PersistedSettings(encryptedKey, modelId);
            var json = JsonSerializer.Serialize(persisted, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist LLM settings to {FilePath}.", _filePath);
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
