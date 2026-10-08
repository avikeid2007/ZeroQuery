"use client";

import { useEffect, useState } from "react";
import { getLlmSettings, updateLlmSettings, ApiError } from "@/lib/api";

// Known OpenAI-compatible chat-completions providers. The backend only needs a base URL +
// API key + model id to talk to any of these — "Custom" lets a user point at anything else
// (self-hosted vLLM, LM Studio, a company gateway, etc.) that speaks the same API shape.
const PROVIDER_PRESETS = [
  { id: "openrouter", name: "OpenRouter (default)", baseUrl: "https://openrouter.ai/api/v1", modelPlaceholder: "openrouter/auto" },
  { id: "openai", name: "OpenAI", baseUrl: "https://api.openai.com/v1", modelPlaceholder: "gpt-4o" },
  { id: "groq", name: "Groq", baseUrl: "https://api.groq.com/openai/v1", modelPlaceholder: "llama-3.3-70b-versatile" },
  { id: "together", name: "Together AI", baseUrl: "https://api.together.xyz/v1", modelPlaceholder: "meta-llama/Llama-3.3-70B-Instruct-Turbo" },
  { id: "deepseek", name: "DeepSeek", baseUrl: "https://api.deepseek.com/v1", modelPlaceholder: "deepseek-chat" },
  { id: "ollama", name: "Ollama (local)", baseUrl: "http://localhost:11434/v1", modelPlaceholder: "llama3.1" },
  { id: "custom", name: "Custom / self-hosted", baseUrl: "", modelPlaceholder: "model-id" },
] as const;

function matchProviderPreset(baseUrl: string): (typeof PROVIDER_PRESETS)[number]["id"] {
  const match = PROVIDER_PRESETS.find((p) => p.id !== "custom" && p.baseUrl === baseUrl.trim());
  return match ? match.id : "custom";
}

const PROMPT_PRESETS = [
  {
    id: "auto-improve",
    name: "✨ Auto-Improved (Smart Charts & Aggregations)",
    desc: "Strict chart rules (bar/line/pie), safe groupby handling, monthly date bucketing, and fast conclusion.",
    prompt: `You are Zeroquery's expert database assistant. You answer questions and prepare database actions using the provided tools.

DATA RETRIEVAL & AGGREGATIONS:
- Always call 'describe_entities' first if entity schema or field names are not completely known.
- Use 'read_records' to fetch records from the database — never guess or hallucinate values.
- Use 'aggregate_records' for counts, sums, averages, mins, and maxs:
  * 'groupby' ONLY accepts exact column names from the entity (e.g. 'ShipCountry', 'CustomerID').
    SQL expressions like 'MONTH(OrderDate)' in 'groupby' are NOT supported.
  * For date-based / time-series grouping (monthly, quarterly, yearly volume):
    Fetch records using 'read_records' (with select e.g. 'OrderDate' and first=200-500) OR call 'aggregate_records'
    grouped by the raw date column, then perform monthly/yearly bucketing and counting in your reasoning!
- Conclude promptly: retrieve necessary data and call 'render_result' within 2 to 4 iterations.

OUTPUT & VISUALIZATION ('render_result'):
- Never respond in plain text — always conclude by calling 'render_result'.
- Choose the best 'render_result' type:
  * 'chart': For comparing categories ('bar'), time trends ('line'), or proportions ('pie').
    The FIRST column MUST be the category/label (e.g. Month, Country, Category).
    The subsequent column(s) MUST be numeric metrics with actual numbers (e.g. 42, not "$42").
  * 'stat': For single KPI aggregates (e.g. Total Revenue, Total Count, Average Price).
  * 'card': For detailed inspection of a single record's fields.
  * 'table': For multi-column listings and detailed record sets.

GUIDELINES FOR MUTATIONS (Create, Update, Delete):
- Direct mutations are forbidden without explicit human confirmation.
- Inspect the entity and retrieve existing records/primary keys first.
- Call 'render_result' with type 'form', specifying 'operation' ('create' | 'update' | 'delete'), 'entity', 'primaryKey', and 'fields' comparing 'currentValue' vs 'proposedValue'.`,
  },
  {
    id: "executive",
    name: "📊 Executive & KPI Highlights",
    desc: "Emphasizes big numeric stat cards, high-level trend charts, and concise business titles.",
    prompt: `You are Zeroquery's executive intelligence assistant. Your goal is to deliver concise, high-impact business insights from relational data.

EXECUTIVE PRESENTATION RULES:
- When a user asks about totals, volume, growth, or KPIs, prioritize 'stat' cards or summary 'chart' visuals (bar/line) over raw tables.
- Use 'aggregate_records' or 'read_records' to compute verified figures.
  * Note: 'groupby' only supports existing column names. For monthly volume, bucket dates in reasoning.
- Keep titles concise, professional, and business-focused (e.g. 'Monthly Order Volume', 'Active Accounts by Country').
- In charts: first column is the category label (e.g. Month), numeric columns are the values.
- For data modifications, propose changes via type 'form' with previous vs proposed values for human confirmation.
- Conclude every turn by calling 'render_result' promptly.`,
  },
  {
    id: "analyst",
    name: "🔬 Deep Data Analyst & Schema Explorer",
    desc: "Optimized for thorough data exploration, multi-table traversal, and complete data tables.",
    prompt: `You are Zeroquery's analytical database assistant. You perform deep schema exploration and comprehensive data retrieval.

ANALYTICAL WORKFLOW:
1. Examine schemas and relations using 'describe_entities' before querying.
2. Query data with 'read_records' or 'aggregate_records'.
   * For aggregations: 'groupby' accepts exact column names. For temporal bucketing (by month/year), fetch dates and aggregate in reasoning.
3. Select appropriate visualizations:
   - Categorical rankings and distributions: Format as 'chart' with type 'bar'.
   - Time-series progressions: Format as 'chart' with type 'line'.
   - Large or detailed datasets: Format cleanly as 'table'.
4. For mutations: strictly emit 'form' UI Specs with explicit primary keys and diff values for human review.
5. Conclude every query by calling 'render_result' within 2-4 tool calls. Do not reply with plain text.`,
  },
];

/**
 * Global settings panel for the LLM provider used by the query orchestration loop — lets a
 * user pick OpenRouter or another OpenAI-compatible provider (OpenAI, Groq, Together,
 * DeepSeek, a local Ollama server, or a custom endpoint), configure the API key/model, and
 * customize/improve the master system prompt from the UI.
 */
export default function LlmSettingsPanel() {
  const [isOpen, setIsOpen] = useState(false);
  const [isPromptModalOpen, setIsPromptModalOpen] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [saveMessage, setSaveMessage] = useState<string | null>(null);

  const [isApiKeyConfigured, setIsApiKeyConfigured] = useState(false);
  const [apiKeyMasked, setApiKeyMasked] = useState<string | null>(null);
  const [modelId, setModelId] = useState("");
  const [apiKeyInput, setApiKeyInput] = useState("");
  const [baseUrl, setBaseUrl] = useState("");
  const [defaultBaseUrl, setDefaultBaseUrl] = useState<string>("");
  const [providerId, setProviderId] = useState<(typeof PROVIDER_PRESETS)[number]["id"]>("openrouter");
  const [systemPrompt, setSystemPrompt] = useState<string | null>(null);
  const [defaultSystemPrompt, setDefaultSystemPrompt] = useState<string>("");
  const [promptDraft, setPromptDraft] = useState<string>("");
  const [promptSaveSuccess, setPromptSaveSuccess] = useState<string | null>(null);

  useEffect(() => {
    let isCancelled = false;

    async function loadSettings() {
      setIsLoading(true);
      setErrorMessage(null);
      try {
        const settings = await getLlmSettings();
        if (isCancelled) return;
        setIsApiKeyConfigured(settings.isApiKeyConfigured);
        setApiKeyMasked(settings.apiKeyMasked);
        setModelId(settings.modelId);
        setSystemPrompt(settings.systemPrompt ?? null);
        setDefaultSystemPrompt(settings.defaultSystemPrompt ?? "");
        setPromptDraft(settings.systemPrompt || settings.defaultSystemPrompt || "");
        const effectiveBaseUrl = settings.baseUrl || settings.defaultBaseUrl || "";
        setBaseUrl(effectiveBaseUrl);
        setDefaultBaseUrl(settings.defaultBaseUrl ?? "");
        setProviderId(matchProviderPreset(effectiveBaseUrl));
      } catch (err) {
        if (isCancelled) return;
        setErrorMessage(err instanceof ApiError ? err.message : "Unexpected error loading LLM settings.");
      } finally {
        if (!isCancelled) setIsLoading(false);
      }
    }

    void loadSettings();

    return () => {
      isCancelled = true;
    };
  }, []);

  function handleProviderChange(nextProviderId: (typeof PROVIDER_PRESETS)[number]["id"]) {
    setProviderId(nextProviderId);
    const preset = PROVIDER_PRESETS.find((p) => p.id === nextProviderId);
    if (preset && preset.id !== "custom") {
      setBaseUrl(preset.baseUrl);
    }
  }

  async function handleSave(e: React.FormEvent) {
    e.preventDefault();
    setIsSaving(true);
    setErrorMessage(null);
    setSaveMessage(null);

    try {
      const trimmedBaseUrl = baseUrl.trim();
      const settings = await updateLlmSettings({
        apiKey: apiKeyInput.trim() ? apiKeyInput.trim() : null,
        modelId: modelId.trim() ? modelId.trim() : null,
        baseUrl: trimmedBaseUrl === defaultBaseUrl.trim() ? "" : trimmedBaseUrl,
      });
      setIsApiKeyConfigured(settings.isApiKeyConfigured);
      setApiKeyMasked(settings.apiKeyMasked);
      setModelId(settings.modelId);
      setApiKeyInput("");
      const effectiveBaseUrl = settings.baseUrl || settings.defaultBaseUrl || "";
      setBaseUrl(effectiveBaseUrl);
      setProviderId(matchProviderPreset(effectiveBaseUrl));
      setSaveMessage("Settings saved.");
      setTimeout(() => setSaveMessage(null), 2000);
    } catch (err) {
      setErrorMessage(err instanceof ApiError ? err.message : "Unexpected error saving LLM settings.");
    } finally {
      setIsSaving(false);
    }
  }

  async function handleClearKey() {
    setIsSaving(true);
    setErrorMessage(null);
    try {
      const settings = await updateLlmSettings({ apiKey: "", modelId: null });
      setIsApiKeyConfigured(settings.isApiKeyConfigured);
      setApiKeyMasked(settings.apiKeyMasked);
    } catch (err) {
      setErrorMessage(err instanceof ApiError ? err.message : "Unexpected error clearing the API key.");
    } finally {
      setIsSaving(false);
    }
  }

  async function handleSavePrompt() {
    setIsSaving(true);
    setErrorMessage(null);
    setPromptSaveSuccess(null);
    try {
      const isDefault = promptDraft.trim() === defaultSystemPrompt.trim();
      const settings = await updateLlmSettings({
        apiKey: null,
        modelId: null,
        systemPrompt: isDefault ? "" : promptDraft.trim(),
      });
      setSystemPrompt(settings.systemPrompt ?? null);
      setPromptDraft(settings.systemPrompt || settings.defaultSystemPrompt || "");
      setPromptSaveSuccess("Master prompt updated & active.");
      setTimeout(() => {
        setPromptSaveSuccess(null);
        setIsPromptModalOpen(false);
      }, 1500);
    } catch (err) {
      setErrorMessage(err instanceof ApiError ? err.message : "Failed to update master prompt.");
    } finally {
      setIsSaving(false);
    }
  }

  function handleResetPrompt() {
    setPromptDraft(defaultSystemPrompt);
  }

  function handleApplyPreset(promptText: string) {
    setPromptDraft(promptText);
  }

  const isCustomPromptActive = Boolean(systemPrompt && systemPrompt.trim() !== defaultSystemPrompt.trim());

  return (
    <>
      <div className="w-full rounded-md border border-border bg-surface">
        <div className="flex w-full items-center justify-between px-4 py-2.5">
          <button
            type="button"
            onClick={() => setIsOpen((v) => !v)}
            className="flex items-center gap-2 text-left"
          >
            <span
              className={`h-2 w-2 rounded-full ${isApiKeyConfigured ? "bg-teal" : "bg-danger"}`}
              title={isApiKeyConfigured ? "API key configured" : "API key not configured"}
            />
            <span className="text-sm font-medium text-text">LLM Settings</span>
            {!isLoading && (
              <span className="font-mono text-xs font-normal text-muted">
                {isApiKeyConfigured
                  ? `${PROVIDER_PRESETS.find((p) => p.id === providerId)?.name ?? "Custom"} · ${apiKeyMasked} · ${modelId}`
                  : "no API key set"}
              </span>
            )}
            <span className="text-xs text-muted ml-1">{isOpen ? "▲" : "▼"}</span>
          </button>

          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => {
                setPromptDraft(systemPrompt || defaultSystemPrompt || "");
                setIsPromptModalOpen(true);
              }}
              className="inline-flex items-center gap-1.5 rounded-md border border-border px-2.5 py-1 text-xs font-medium text-text hover:bg-surface2 hover:border-teal transition-colors shadow-sm"
              title="Edit and improve the master system prompt used for AI database reasoning"
            >
              <span>✏️ Master Prompt</span>
              {isCustomPromptActive ? (
                <span className="rounded bg-teal/20 px-1 py-0.2 text-[10px] font-semibold text-teal">Custom</span>
              ) : (
                <span className="rounded bg-surface2 px-1 py-0.2 text-[10px] text-muted">Default</span>
              )}
            </button>
          </div>
        </div>

        {isOpen && (
          <form onSubmit={handleSave} className="flex flex-col gap-3 border-t border-border px-4 py-3">
            <p className="text-xs leading-relaxed text-muted">
              Applies immediately to this running backend. Encrypted at rest via ASP.NET Data Protection API
              and automatically persists across restarts.
            </p>

            <label className="flex flex-col gap-1.5">
              <span className="text-xs text-muted">Provider</span>
              <select
                value={providerId}
                onChange={(e) => handleProviderChange(e.target.value as (typeof PROVIDER_PRESETS)[number]["id"])}
                disabled={isSaving}
                className="rounded-md border border-border bg-bg px-3 py-2 text-sm text-text focus:border-teal focus:outline-none disabled:opacity-50"
              >
                {PROVIDER_PRESETS.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name}
                  </option>
                ))}
              </select>
            </label>

            {providerId === "custom" && (
              <label className="flex flex-col gap-1.5">
                <span className="text-xs text-muted">API base URL</span>
                <input
                  type="text"
                  value={baseUrl}
                  onChange={(e) => setBaseUrl(e.target.value)}
                  disabled={isSaving}
                  placeholder="https://your-provider.example.com/v1"
                  spellCheck={false}
                  autoComplete="off"
                  className="rounded-md border border-border bg-bg px-3 py-2 font-mono text-sm text-text focus:border-teal focus:outline-none disabled:opacity-50"
                />
                <span className="text-xs text-muted">
                  Any OpenAI-compatible <code className="rounded bg-surface2 px-1 py-0.5">/chat/completions</code> endpoint
                  (self-hosted vLLM, LM Studio, a company gateway, etc.).
                </span>
              </label>
            )}

            <label className="flex flex-col gap-1.5">
              <span className="text-xs text-muted">API key</span>
              <input
                type="password"
                value={apiKeyInput}
                onChange={(e) => setApiKeyInput(e.target.value)}
                disabled={isSaving}
                placeholder={isApiKeyConfigured ? `Currently: ${apiKeyMasked} (leave blank to keep)` : "sk-..."}
                spellCheck={false}
                autoComplete="off"
                className="rounded-md border border-border bg-bg px-3 py-2 font-mono text-sm text-text focus:border-teal focus:outline-none disabled:opacity-50"
              />
            </label>

            <label className="flex flex-col gap-1.5">
              <span className="text-xs text-muted">Model id</span>
              <input
                type="text"
                value={modelId}
                onChange={(e) => setModelId(e.target.value)}
                disabled={isSaving}
                placeholder={PROVIDER_PRESETS.find((p) => p.id === providerId)?.modelPlaceholder ?? "model-id"}
                spellCheck={false}
                className="rounded-md border border-border bg-bg px-3 py-2 font-mono text-sm text-text focus:border-teal focus:outline-none disabled:opacity-50"
              />
              <span className="text-xs text-muted">
                e.g. <code className="rounded bg-surface2 px-1 py-0.5">openrouter/auto:exacto</code>,{" "}
                <code className="rounded bg-surface2 px-1 py-0.5">nvidia/nemotron-3-ultra-550b-a55b:free</code>,{" "}
                <code className="rounded bg-surface2 px-1 py-0.5">anthropic/claude-3.5-sonnet</code>
              </span>
            </label>

            {errorMessage && (
              <div className="rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
                {errorMessage}
              </div>
            )}

            {saveMessage && <div className="text-sm text-teal">{saveMessage}</div>}

            <div className="flex gap-2">
              <button
                type="submit"
                disabled={isSaving}
                className="inline-flex items-center justify-center rounded-md bg-teal px-4 py-2 text-sm font-medium text-bg disabled:cursor-not-allowed disabled:opacity-50"
              >
                {isSaving ? "Saving…" : "Save Settings"}
              </button>
              {isApiKeyConfigured && (
                <button
                  type="button"
                  onClick={handleClearKey}
                  disabled={isSaving}
                  className="rounded-md border border-danger/40 px-4 py-2 text-sm font-medium text-danger disabled:cursor-not-allowed disabled:opacity-50 hover:bg-danger/10"
                >
                  Clear API key
                </button>
              )}
            </div>
          </form>
        )}
      </div>

      {/* Master System Prompt Modal */}
      {isPromptModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-xs">
          <div className="flex max-h-[90vh] w-full max-w-2xl flex-col rounded-lg border border-border bg-surface shadow-2xl">
            <div className="flex items-center justify-between border-b border-border px-5 py-4">
              <div>
                <h3 className="text-base font-semibold text-text">Edit Master System Prompt</h3>
                <p className="text-xs text-muted">
                  Controls how the AI assistant queries tables, chooses charts/tables/stats, and proposes mutations.
                </p>
              </div>
              <button
                type="button"
                onClick={() => setIsPromptModalOpen(false)}
                className="rounded-md p-1.5 text-muted hover:bg-surface2 hover:text-text"
              >
                ✕
              </button>
            </div>

            <div className="flex flex-1 flex-col gap-3 overflow-y-auto p-5">
              {/* Prompt Improvement Presets */}
              <div className="flex flex-col gap-1.5 rounded-md border border-border/80 bg-surface2/40 p-3">
                <span className="text-xs font-semibold text-text">✨ Quick Presets & Improvements</span>
                <p className="text-[11px] text-muted">
                  Select an optimized template or enhance instructions for your specific database workflow:
                </p>
                <div className="grid grid-cols-1 gap-2 pt-1 sm:grid-cols-3">
                  {PROMPT_PRESETS.map((preset) => (
                    <button
                      key={preset.id}
                      type="button"
                      onClick={() => handleApplyPreset(preset.prompt)}
                      className="flex flex-col items-start rounded border border-border bg-surface p-2 text-left hover:border-teal hover:bg-surface2 transition-all shadow-xs"
                    >
                      <span className="text-xs font-medium text-text">{preset.name}</span>
                      <span className="text-[10px] text-muted leading-tight mt-0.5">{preset.desc}</span>
                    </button>
                  ))}
                </div>
              </div>

              {/* Editable Prompt Area */}
              <div className="flex flex-col gap-1">
                <div className="flex items-center justify-between">
                  <span className="text-xs font-medium text-muted">System Instructions</span>
                  <button
                    type="button"
                    onClick={handleResetPrompt}
                    className="text-xs font-medium text-amber hover:underline"
                  >
                    ↺ Reset to Default Prompt
                  </button>
                </div>
                <textarea
                  rows={14}
                  value={promptDraft}
                  onChange={(e) => setPromptDraft(e.target.value)}
                  spellCheck={false}
                  className="w-full rounded-md border border-border bg-bg p-3 font-mono text-xs text-text focus:border-teal focus:outline-none leading-relaxed"
                />
              </div>

              {promptSaveSuccess && (
                <div className="rounded-md border border-teal/40 bg-teal/10 px-3 py-2 text-xs font-medium text-teal">
                  ✓ {promptSaveSuccess}
                </div>
              )}
            </div>

            <div className="flex items-center justify-between border-t border-border px-5 py-3">
              <span className="text-xs text-muted">
                {promptDraft.trim() === defaultSystemPrompt.trim() ? "Using default prompt" : "Custom prompt active"}
              </span>
              <div className="flex gap-2">
                <button
                  type="button"
                  onClick={() => setIsPromptModalOpen(false)}
                  className="rounded-md border border-border px-3 py-1.5 text-xs font-medium text-text hover:bg-surface2"
                >
                  Cancel
                </button>
                <button
                  type="button"
                  onClick={handleSavePrompt}
                  disabled={isSaving}
                  className="rounded-md bg-teal px-4 py-1.5 text-xs font-medium text-bg hover:opacity-90 disabled:opacity-50"
                >
                  {isSaving ? "Saving…" : "Save & Apply Prompt"}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
