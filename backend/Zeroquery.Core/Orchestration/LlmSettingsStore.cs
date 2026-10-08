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
/// <param name="SystemPrompt">Optional custom master system prompt. When null or empty, the default Zeroquery prompt is used.</param>
/// <param name="BaseUrl">
/// Optional override of the chat-completions API base URL, letting a user point at any
/// OpenAI-compatible provider (OpenAI, Azure OpenAI, Groq, Together, DeepSeek, a local
/// Ollama/LM Studio server, etc.) instead of just OpenRouter. Null/empty means "use the
/// default OpenRouter endpoint".
/// </param>
public sealed record LlmSettings(string ApiKey, string ModelId, string? SystemPrompt = null, string? BaseUrl = null);

/// <summary>Safe-to-expose view of <see cref="LlmSettings"/> — the raw key is never included.</summary>
/// <param name="ModelId">Currently configured model id.</param>
/// <param name="IsApiKeyConfigured">Whether an API key is currently set (by env var, config, or the settings UI).</param>
/// <param name="ApiKeyMasked">Last 4 characters of the key (e.g. "••••av3x"), or null if none is configured.</param>
/// <param name="SystemPrompt">Currently configured custom system prompt, or null if using default.</param>
/// <param name="DefaultSystemPrompt">Built-in default system prompt for display and resets.</param>
/// <param name="BaseUrl">Currently configured custom API base URL, or null if using the default provider.</param>
/// <param name="DefaultBaseUrl">Built-in default API base URL (OpenRouter) for display and resets.</param>
public sealed record LlmSettingsView(
    string ModelId,
    bool IsApiKeyConfigured,
    string? ApiKeyMasked,
    string? SystemPrompt,
    string DefaultSystemPrompt,
    string? BaseUrl,
    string DefaultBaseUrl);

/// <summary>
/// Holds the LLM provider settings (API key, model id, base URL, master prompt) that
/// <see cref="OpenRouterLlmProvider"/> reads on every call — any OpenAI-compatible provider
/// can be targeted by overriding <see cref="LlmSettings.BaseUrl"/>. Seeded at startup from
/// <see cref="OpenRouterOptions"/> (env vars / appsettings — see doc/Plan.md Section 8), but
/// can be updated at runtime through the settings UI. When <see cref="PersistenceOptions.IsSaveMode"/>
/// is true, settings persist to disk encrypted across backend restarts (Phase 7).
/// </summary>
public interface ILlmSettingsStore
{
    LlmSettings Current { get; }

    /// <summary>Safe-to-expose view for API responses — never leaks the raw key.</summary>
    LlmSettingsView GetMasked();

    /// <summary>
    /// Updates settings. Pass null to leave a field unchanged; pass an empty string for
    /// <paramref name="apiKey"/> to explicitly clear it. Pass empty string or "__RESET__"
    /// for <paramref name="systemPrompt"/> or <paramref name="baseUrl"/> to reset to default.
    /// </summary>
    void Update(string? apiKey, string? modelId, string? systemPrompt = null, string? baseUrl = null);
}

public sealed class LlmSettingsStore : ILlmSettingsStore
{
    public const string DefaultSystemPrompt = """
        You are Zeroquery's query assistant. You answer questions and prepare database actions using
        the provided tools.

        DATA RETRIEVAL & AGGREGATIONS:
        - Call 'describe_entities' first if you do not know the exact entity names or field names.
        - Use 'read_records' to query rows (supports select, filter, orderby, first).
        - Use 'aggregate_records' for computing totals, averages, counts, mins, maxs.
          * NOTE: 'groupby' ONLY accepts exact column names from the entity (e.g. 'ShipCountry', 'CustomerID').
            SQL functions or expressions like 'MONTH(OrderDate)' in 'groupby' will FAIL.
          * For date-based / time-series grouping (monthly, quarterly, yearly volume):
            Fetch the records using 'read_records' (with select e.g. 'OrderDate' and first=200-500) OR
            use 'aggregate_records' with groupby on the raw date column, then perform the monthly/yearly
            bucketing and counting in your reasoning!
        - Keep tool calls focused: retrieve necessary data and call 'render_result' promptly within
          2 to 4 iterations. Do not loop repeatedly.

        OUTPUT & VISUALIZATION ('render_result'):
        - Never respond in plain text — always conclude by calling 'render_result'.
        - When the user asks for a chart, plot, volume, trend, or distribution:
          * Set 'type': 'chart' and 'chartType': 'bar', 'line', or 'pie'.
          * In 'columns': The FIRST column must be the category/label (e.g. Month, Category, Country).
            The subsequent column(s) must be the numeric metric (e.g. OrderCount, TotalAmount).
          * In 'rows': Provide row objects where the numeric metric values are actual numbers (e.g. 42, not "$42").
        - For single KPI numbers (e.g. total count, total revenue): Set 'type': 'stat'.
        - For single record details: Set 'type': 'card'.
        - For general listings: Set 'type': 'table'.

        MUTATIONS (Create, Update, Delete):
        - Direct mutations are forbidden without explicit human confirmation.
        - First inspect the entity with 'describe_entities' or 'read_records' to verify existing fields and keys.
        - Then call 'render_result' with type 'form', providing the 'form' object with 'operation'
          ('create' | 'update' | 'delete'), 'entity', 'primaryKey', and 'fields' comparing 'currentValue' vs 'proposedValue'.
        """;

    private sealed record PersistedSettings(string? EncryptedApiKey, string? ModelId, string? SystemPrompt = null, string? BaseUrl = null);

    private readonly Lock _lock = new();
    private readonly PersistenceOptions? _persistenceOptions;
    private readonly IConnectionStringProtector? _protector;
    private readonly ILogger<LlmSettingsStore> _logger;
    private readonly string? _filePath;
    private readonly string _defaultBaseUrl;
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
        _defaultBaseUrl = options.Value.BaseUrl;

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

                if (!string.IsNullOrWhiteSpace(persisted.SystemPrompt))
                {
                    _current = _current with { SystemPrompt = persisted.SystemPrompt };
                }

                if (!string.IsNullOrWhiteSpace(persisted.BaseUrl))
                {
                    _current = _current with { BaseUrl = persisted.BaseUrl };
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
        return new LlmSettingsView(current.ModelId, isConfigured, masked, current.SystemPrompt, DefaultSystemPrompt, current.BaseUrl, _defaultBaseUrl);
    }

    public void Update(string? apiKey, string? modelId, string? systemPrompt = null, string? baseUrl = null)
    {
        lock (_lock)
        {
            var nextApiKey = apiKey ?? _current.ApiKey;
            var nextModelId = string.IsNullOrWhiteSpace(modelId) ? _current.ModelId : modelId;
            var nextSystemPrompt = systemPrompt switch
            {
                null => _current.SystemPrompt,
                "" => null,
                "__RESET__" => null,
                _ => systemPrompt
            };
            var nextBaseUrl = baseUrl switch
            {
                null => _current.BaseUrl,
                "" => null,
                "__RESET__" => null,
                _ => baseUrl
            };

            _current = new LlmSettings(nextApiKey, nextModelId, nextSystemPrompt, nextBaseUrl);

            if (_persistenceOptions?.IsSaveMode == true && _filePath is not null)
            {
                SaveToDisk(nextApiKey, nextModelId, nextSystemPrompt, nextBaseUrl);
            }
        }
    }

    private void SaveToDisk(string apiKey, string modelId, string? systemPrompt, string? baseUrl)
    {
        try
        {
            if (_persistenceOptions is null || _filePath is null) return;
            Directory.CreateDirectory(_persistenceOptions.StorageDirectory);

            var encryptedKey = !string.IsNullOrWhiteSpace(apiKey) && _protector is not null
                ? _protector.Protect(apiKey)
                : null;

            var persisted = new PersistedSettings(encryptedKey, modelId, systemPrompt, baseUrl);
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
