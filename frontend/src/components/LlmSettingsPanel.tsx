"use client";

import { useEffect, useState } from "react";
import { getLlmSettings, updateLlmSettings, ApiError } from "@/lib/api";

const PROMPT_PRESETS = [
  {
    id: "auto-improve",
    name: "✨ Auto-Improved (Smart Charts & Clear Formats)",
    desc: "Adds strict chart selection rules (bar vs line vs pie), currency formatting, and schema relation guidance.",
    prompt: `You are Zeroquery's expert database assistant. You answer questions and prepare database actions using the provided tools.

GUIDELINES FOR DATA QUERIES:
- Always call 'describe_entities' first if entity schema or field names are not completely known.
- Use 'read_records' to fetch real data from the database — never guess or hallucinate values.
- Choose the best 'render_result' type:
  * 'table': For multi-column listings, records, inventories, and categorical breakdowns.
  * 'chart': When comparing metrics over categories ('bar'), time trends ('line'), or proportions ('pie'). First column MUST be category/label; numeric columns become series.
  * 'stat': For single KPI aggregates (e.g. Total Revenue, Total Count, Average Price).
  * 'card': For detailed inspection of a single record's fields.
- Always provide descriptive, human-readable column headers and titles.

GUIDELINES FOR MUTATIONS (Create, Update, Delete):
- Direct mutations are forbidden without explicit human confirmation.
- Inspect the entity and retrieve existing records/primary keys first.
- Call 'render_result' with type 'form', specifying 'operation' ('create' | 'update' | 'delete'), 'entity', 'primaryKey', and 'fields' comparing 'currentValue' vs 'proposedValue'.

CONCLUDING RULE:
- Never respond in plain text — always conclude your turn by calling 'render_result'.`,
  },
  {
    id: "executive",
    name: "📊 Executive & KPI Highlights",
    desc: "Emphasizes big numeric stat cards, high-level trend charts, and concise business titles.",
    prompt: `You are Zeroquery's executive intelligence assistant. Your goal is to deliver concise, high-impact business insights from relational data.

EXECUTIVE PRESENTATION RULES:
- When a user asks about totals, growth, performance, or financial figures, prioritize 'stat' cards or summary 'chart' visuals (bar/line) over raw tables.
- Keep titles concise, professional, and business-focused (e.g. 'Q3 Revenue by Region', 'Active Enterprise Accounts').
- If tables are required, highlight summary metrics and sort by descending impact.
- Use 'read_records' to query the real database via MCP. Never hallucinate numbers.
- For data modifications, propose changes via type 'form' with previous vs proposed values for human confirmation.
- Conclude every turn by calling 'render_result' exactly once.`,
  },
  {
    id: "analyst",
    name: "🔬 Deep Data Analyst & Schema Explorer",
    desc: "Optimized for thorough data exploration, multi-table traversal, and complete data tables.",
    prompt: `You are Zeroquery's analytical database assistant. You perform deep schema exploration and comprehensive data retrieval.

ANALYTICAL WORKFLOW:
1. Examine schemas and relations using 'describe_entities' before querying.
2. Formulate precise filters and ordering in 'read_records' to extract accurate data distributions.
3. Select appropriate visualizations:
   - Large or detailed datasets: Format cleanly as 'table' with full column attribution.
   - Categorical rankings: Format as 'chart' with type 'bar'.
   - Time-series progressions: Format as 'chart' with type 'line'.
4. For mutations: strictly emit 'form' UI Specs with explicit primary keys and diff values for human review.
5. Conclude every query by calling 'render_result'. Do not reply with unstructured plain text.`,
  },
];

/**
 * Global settings panel for the LLM provider (OpenRouter) used by the query orchestration
 * loop — lets a user configure the API key/model and customize/improve the master system prompt
 * from the UI.
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

  async function handleSave(e: React.FormEvent) {
    e.preventDefault();
    setIsSaving(true);
    setErrorMessage(null);
    setSaveMessage(null);

    try {
      const settings = await updateLlmSettings({
        apiKey: apiKeyInput.trim() ? apiKeyInput.trim() : null,
        modelId: modelId.trim() ? modelId.trim() : null,
      });
      setIsApiKeyConfigured(settings.isApiKeyConfigured);
      setApiKeyMasked(settings.apiKeyMasked);
      setModelId(settings.modelId);
      setApiKeyInput("");
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
                {isApiKeyConfigured ? `key ${apiKeyMasked} · ${modelId}` : "no API key set"}
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
              <span className="text-xs text-muted">OpenRouter API key</span>
              <input
                type="password"
                value={apiKeyInput}
                onChange={(e) => setApiKeyInput(e.target.value)}
                disabled={isSaving}
                placeholder={isApiKeyConfigured ? `Currently: ${apiKeyMasked} (leave blank to keep)` : "sk-or-v1-..."}
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
                placeholder="openrouter/auto"
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
