namespace Zeroquery.Core.Orchestration;

/// <summary>
/// Configuration for <see cref="OpenRouterLlmProvider"/> (doc/Plan.md Section 8 — model
/// recommendation). Bind from configuration section "OpenRouter" / env vars
/// <c>OpenRouter__ApiKey</c>, <c>LLM_MODEL_ID</c>, etc.
/// </summary>
public sealed class OpenRouterOptions
{
    /// <summary>OpenRouter API key. Required — the provider throws at call time if unset.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Model id to request. Defaults to OpenRouter's ":exacto" auto-routing suffix on a
    /// capable default, per doc/Plan.md Section 8 ("don't hardcode one model"). Self-hosters
    /// should override via <c>LLM_MODEL_ID</c>.
    /// </summary>
    public string ModelId { get; set; } = "openrouter/auto";

    /// <summary>Base URL for the OpenRouter API.</summary>
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    /// <summary>Optional — sets the OpenRouter-recommended HTTP-Referer header for app attribution.</summary>
    public string? HttpReferer { get; set; }

    /// <summary>Optional — sets the OpenRouter-recommended X-Title header for app attribution.</summary>
    public string? AppTitle { get; set; }
}
