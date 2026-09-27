"use client";

import { useEffect, useState } from "react";
import { getLlmSettings, updateLlmSettings, ApiError } from "@/lib/api";

/**
 * Global settings panel for the LLM provider (OpenRouter) used by the query orchestration
 * loop — lets a user configure the API key/model from the UI instead of environment
 * variables or appsettings.json. Collapsed by default; shows a colored dot indicating
 * whether a key is currently configured so it's obvious at a glance.
 */
export default function LlmSettingsPanel() {
  const [isOpen, setIsOpen] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [saveMessage, setSaveMessage] = useState<string | null>(null);

  const [isApiKeyConfigured, setIsApiKeyConfigured] = useState(false);
  const [apiKeyMasked, setApiKeyMasked] = useState<string | null>(null);
  const [modelId, setModelId] = useState("");
  const [apiKeyInput, setApiKeyInput] = useState("");

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
      setSaveMessage("Saved.");
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

  return (
    <div className="w-full rounded-md border border-border bg-surface">
      <button
        type="button"
        onClick={() => setIsOpen((v) => !v)}
        className="flex w-full items-center justify-between px-4 py-2.5 text-left"
      >
        <span className="flex items-center gap-2 text-sm font-medium text-text">
          <span
            className={`h-1.5 w-1.5 rounded-full ${isApiKeyConfigured ? "bg-teal" : "bg-danger"}`}
            title={isApiKeyConfigured ? "API key configured" : "API key not configured"}
          />
          LLM settings (OpenRouter)
          {!isLoading && (
            <span className="font-mono text-xs font-normal text-muted">
              {isApiKeyConfigured ? `key ${apiKeyMasked} · ${modelId}` : "no API key set"}
            </span>
          )}
        </span>
        <span className="text-xs text-muted">{isOpen ? "▲" : "▼"}</span>
      </button>

      {isOpen && (
        <form onSubmit={handleSave} className="flex flex-col gap-3 border-t border-border px-4 py-3">
          <p className="text-xs leading-relaxed text-muted">
            Applies immediately to this running backend. Not persisted across a restart —
            set <code className="rounded bg-surface2 px-1 py-0.5">OPENROUTER_API_KEY</code> /{" "}
            <code className="rounded bg-surface2 px-1 py-0.5">LLM_MODEL_ID</code> env vars (or
            appsettings.json) for a permanent default.
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
              e.g. <code className="rounded bg-surface2 px-1 py-0.5">openrouter/auto</code>,{" "}
              <code className="rounded bg-surface2 px-1 py-0.5">openai/gpt-4o</code>,{" "}
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
              {isSaving ? "Saving…" : "Save"}
            </button>
            {isApiKeyConfigured && (
              <button
                type="button"
                onClick={handleClearKey}
                disabled={isSaving}
                className="rounded-md border border-danger/40 px-4 py-2 text-sm font-medium text-danger disabled:cursor-not-allowed disabled:opacity-50"
              >
                Clear API key
              </button>
            )}
          </div>
        </form>
      )}
    </div>
  );
}
