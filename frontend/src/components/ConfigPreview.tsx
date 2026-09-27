"use client";

import { useEffect, useRef, useState } from "react";
import type { DatabaseProvider, IntrospectResponse, InstanceStatusResponse } from "@/lib/types";
import { toEntitySelectionDtos, type PickerSelection } from "@/lib/selection";
import { generateDabConfig, startDabInstance, getDabInstanceStatus, stopDabInstance, ApiError } from "@/lib/api";
import InstanceStatusBadge from "@/components/InstanceStatusBadge";
import QueryView from "@/components/QueryView";

interface ConfigPreviewProps {
  provider: DatabaseProvider;
  connectionString: string;
  schema: IntrospectResponse;
  selection: PickerSelection;
  onBack: () => void;
}

const POLL_INTERVAL_MS = 1000;

export default function ConfigPreview({ provider, connectionString, schema, selection, onBack }: ConfigPreviewProps) {
  const [envVarName, setEnvVarName] = useState("ZQ_DB_CONN");
  const [isLoading, setIsLoading] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [configJson, setConfigJson] = useState<string | null>(null);
  const [validationErrors, setValidationErrors] = useState<string[]>([]);
  const [copied, setCopied] = useState(false);

  const [instance, setInstance] = useState<InstanceStatusResponse | null>(null);
  const [isStarting, setIsStarting] = useState(false);
  const [isStopping, setIsStopping] = useState(false);
  const [instanceError, setInstanceError] = useState<string | null>(null);
  const pollHandle = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => {
    return () => {
      if (pollHandle.current) clearInterval(pollHandle.current);
    };
  }, []);

  function startPolling(id: string) {
    if (pollHandle.current) clearInterval(pollHandle.current);
    pollHandle.current = setInterval(async () => {
      try {
        const latest = await getDabInstanceStatus(id);
        setInstance(latest);
        if (latest.status === "Stopped" || latest.status === "Error") {
          if (pollHandle.current) clearInterval(pollHandle.current);
        }
      } catch {
        // Transient poll failure — leave last-known state displayed, next tick will retry.
      }
    }, POLL_INTERVAL_MS);
  }

  async function handleGenerate() {
    setIsLoading(true);
    setErrorMessage(null);
    setConfigJson(null);
    setValidationErrors([]);

    try {
      const entities = toEntitySelectionDtos(schema.tables, selection);
      const result = await generateDabConfig({
        provider,
        connectionStringEnvVarName: envVarName.trim() || "ZQ_DB_CONN",
        entities,
      });
      setConfigJson(result.configJson);
      setValidationErrors(result.validationErrors);
    } catch (err) {
      setErrorMessage(err instanceof ApiError ? err.message : "Unexpected error generating config.");
    } finally {
      setIsLoading(false);
    }
  }

  async function handleStart() {
    if (!configJson) return;
    setIsStarting(true);
    setInstanceError(null);

    try {
      const started = await startDabInstance({
        configJson,
        connectionStringEnvVarName: envVarName.trim() || "ZQ_DB_CONN",
        connectionString,
      });
      setInstance(started);
      startPolling(started.id);
    } catch (err) {
      setInstanceError(err instanceof ApiError ? err.message : "Unexpected error starting the instance.");
    } finally {
      setIsStarting(false);
    }
  }

  async function handleStop() {
    if (!instance) return;
    setIsStopping(true);
    setInstanceError(null);

    try {
      await stopDabInstance(instance.id);
      if (pollHandle.current) clearInterval(pollHandle.current);
      setInstance(null);
    } catch (err) {
      setInstanceError(err instanceof ApiError ? err.message : "Unexpected error stopping the instance.");
    } finally {
      setIsStopping(false);
    }
  }

  function handleCopy() {
    if (!configJson) return;
    navigator.clipboard.writeText(configJson).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    });
  }

  function handleDownload() {
    if (!configJson) return;
    const blob = new Blob([configJson], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = "dab-config.json";
    a.click();
    URL.revokeObjectURL(url);
  }

  return (
    <div className="flex w-full flex-col gap-4">
      <div>
        <h1 className="text-lg font-semibold tracking-tight text-text">Generate dab-config.json</h1>
        <p className="mt-1 text-sm text-muted">
          DAB reads the connection string from an environment variable at startup — Zeroquery
          never writes the raw connection string into the generated file.
        </p>
      </div>

      <label className="flex flex-col gap-1.5">
        <span className="text-xs text-muted">Connection string environment variable name</span>
        <input
          type="text"
          value={envVarName}
          onChange={(e) => setEnvVarName(e.target.value)}
          disabled={isLoading}
          spellCheck={false}
          className="w-full max-w-sm rounded-md border border-border bg-bg px-3 py-2 font-mono text-sm text-text focus:border-teal focus:outline-none disabled:opacity-50"
        />
        <span className="text-xs text-muted">
          Set this variable to your connection string before running <code className="rounded bg-surface2 px-1 py-0.5">dab start</code>.
        </span>
      </label>

      {errorMessage && (
        <div className="rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
          {errorMessage}
        </div>
      )}

      <div className="flex flex-wrap justify-end gap-3 border-t border-border pt-4">
        <button
          type="button"
          onClick={onBack}
          className="rounded-md border border-border px-4 py-2 text-sm font-medium text-text hover:bg-surface2"
        >
          Back
        </button>
        <button
          type="button"
          onClick={handleGenerate}
          disabled={isLoading}
          className="inline-flex items-center justify-center rounded-md bg-teal px-4 py-2 text-sm font-medium text-bg disabled:cursor-not-allowed disabled:opacity-50"
        >
          {isLoading ? "Generating…" : "Generate config"}
        </button>
      </div>

      {configJson && (
        <div className="flex min-w-0 flex-col gap-2">
          <div className="flex flex-wrap items-center gap-2">
            {validationErrors.length === 0 ? (
              <span className="rounded border border-teal/40 bg-surface2 px-2 py-1 text-xs font-medium text-teal">
                ✓ Valid against DAB schema
              </span>
            ) : (
              <span className="rounded border border-danger/40 bg-surface2 px-2 py-1 text-xs font-medium text-danger">
                ✗ {validationErrors.length} validation error{validationErrors.length === 1 ? "" : "s"}
              </span>
            )}
            <div className="flex gap-2 sm:ml-auto">
              <button
                type="button"
                onClick={handleCopy}
                className="rounded-md border border-border px-3 py-1.5 text-xs font-medium text-text hover:bg-surface2"
              >
                {copied ? "Copied!" : "Copy"}
              </button>
              <button
                type="button"
                onClick={handleDownload}
                className="rounded-md border border-border px-3 py-1.5 text-xs font-medium text-text hover:bg-surface2"
              >
                Download dab-config.json
              </button>
            </div>
          </div>

          {validationErrors.length > 0 && (
            <ul className="flex flex-col gap-1 rounded-md border border-danger/40 bg-danger/10 p-3 text-xs text-danger">
              {validationErrors.map((err, i) => (
                <li key={i}>{err}</li>
              ))}
            </ul>
          )}

          <pre className="max-h-96 max-w-full overflow-auto rounded-md border border-border bg-bg p-3 text-left font-mono text-xs text-text">
            {configJson}
          </pre>

          {validationErrors.length === 0 && (
            <div className="flex flex-col gap-3 rounded-md border border-border p-3">
              <div className="flex items-center justify-between">
                <span className="text-sm font-medium text-text">DAB instance</span>
                {instance && <InstanceStatusBadge status={instance.status} />}
              </div>

              {instanceError && (
                <div className="rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
                  {instanceError}
                </div>
              )}

              {instance && instance.status === "Error" && instance.lastError && (
                <div className="rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-xs text-danger">
                  {instance.lastError}
                </div>
              )}

              {instance && (instance.status === "Running" || instance.status === "Idle") && (
                <p className="text-xs text-muted">
                  Serving at <code className="font-mono text-text">{instance.baseUrl}</code>
                </p>
              )}

              <div className="flex gap-2">
                {!instance || instance.status === "Stopped" || instance.status === "Error" ? (
                  <button
                    type="button"
                    onClick={handleStart}
                    disabled={isStarting}
                    className="inline-flex items-center justify-center rounded-md bg-teal px-4 py-2 text-sm font-medium text-bg disabled:cursor-not-allowed disabled:opacity-50"
                  >
                    {isStarting ? "Starting…" : "Start DAB instance"}
                  </button>
                ) : (
                  <button
                    type="button"
                    onClick={handleStop}
                    disabled={isStopping}
                    className="inline-flex items-center justify-center rounded-md border border-danger/40 px-4 py-2 text-sm font-medium text-danger disabled:cursor-not-allowed disabled:opacity-50"
                  >
                    {isStopping ? "Stopping…" : "Disconnect"}
                  </button>
                )}
              </div>

              {instance && (instance.status === "Running" || instance.status === "Idle") && (
                <QueryView instanceId={instance.id} />
              )}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
