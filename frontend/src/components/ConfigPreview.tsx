"use client";

import { useEffect, useRef, useState } from "react";
import type { DatabaseProvider, IntrospectResponse, InstanceStatusResponse, DabStatusInfo } from "@/lib/types";
import { toEntitySelectionDtos, type PickerSelection } from "@/lib/selection";
import {
  generateDabConfig,
  startDabInstance,
  getDabInstanceStatus,
  stopDabInstance,
  saveConnection,
  getDabStatus,
  installDab,
  ApiError,
} from "@/lib/api";
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

  const [shouldSave, setShouldSave] = useState(true);
  const [connectionName, setConnectionName] = useState("");

  const [instance, setInstance] = useState<InstanceStatusResponse | null>(null);
  const [isStarting, setIsStarting] = useState(false);
  const [isStopping, setIsStopping] = useState(false);
  const [instanceError, setInstanceError] = useState<string | null>(null);
  const [dabStatus, setDabStatus] = useState<DabStatusInfo | null>(null);
  const [isInstallingDab, setIsInstallingDab] = useState(false);
  const [dabInstallMessage, setDabInstallMessage] = useState<string | null>(null);
  const [enableRest, setEnableRest] = useState(true);
  const [enableGraphQL, setEnableGraphQL] = useState(true);
  const pollHandle = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => {
    checkDabStatus();
    return () => {
      if (pollHandle.current) clearInterval(pollHandle.current);
    };
  }, []);

  async function checkDabStatus() {
    try {
      const status = await getDabStatus();
      setDabStatus(status);
    } catch {
      // Ignore if endpoint is unavailable
    }
  }

  async function handleInstallDab() {
    setIsInstallingDab(true);
    setDabInstallMessage(null);
    setInstanceError(null);
    try {
      const res = await installDab();
      setDabInstallMessage(res.message || "Microsoft.DataApiBuilder installed successfully!");
      await checkDabStatus();
    } catch (err) {
      setDabInstallMessage(err instanceof ApiError ? err.message : "Failed to install DAB.");
    } finally {
      setIsInstallingDab(false);
    }
  }

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
        enableRest,
        enableGraphQL,
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
      if (shouldSave) {
        try {
          await saveConnection({
            name: connectionName.trim() || undefined,
            provider,
            connectionString,
            configJson,
            connectionStringEnvVarName: envVarName.trim() || "ZQ_DB_CONN",
          });
        } catch {
          // If saving fails (e.g. storage disabled), don't block instance start
        }
      }

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

      {/* REST & GraphQL Options */}
      <div className="flex flex-col gap-2 rounded-md border border-border bg-surface2/30 p-3">
        <span className="text-xs font-semibold text-text">DAB Runtime Endpoints</span>
        <p className="text-xs text-muted">
          Choose which additional endpoints Microsoft Data API builder will expose alongside SQL MCP:
        </p>
        <div className="flex flex-wrap gap-4 pt-1">
          <label className="flex items-center gap-2 text-xs font-medium text-text cursor-pointer">
            <input
              type="checkbox"
              checked={enableRest}
              onChange={(e) => setEnableRest(e.target.checked)}
              disabled={isLoading}
              className="h-3.5 w-3.5 rounded border-border text-teal focus:ring-teal"
            />
            <span>Enable REST API (<code className="font-mono text-[11px] text-muted">/api</code>)</span>
          </label>
          <label className="flex items-center gap-2 text-xs font-medium text-text cursor-pointer">
            <input
              type="checkbox"
              checked={enableGraphQL}
              onChange={(e) => setEnableGraphQL(e.target.checked)}
              disabled={isLoading}
              className="h-3.5 w-3.5 rounded border-border text-teal focus:ring-teal"
            />
            <span>Enable GraphQL API (<code className="font-mono text-[11px] text-muted">/graphql</code>)</span>
          </label>
        </div>
      </div>

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

              {dabStatus && !dabStatus.isInstalled && (
                <div className="flex flex-col gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 p-3 text-xs text-amber-200">
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-semibold text-amber-300">Microsoft Data API builder (dab) is not installed</span>
                    <button
                      type="button"
                      onClick={handleInstallDab}
                      disabled={isInstallingDab}
                      className="rounded bg-teal px-3 py-1 text-xs font-semibold text-bg hover:opacity-90 disabled:opacity-50"
                    >
                      {isInstallingDab ? "Installing DAB..." : "Install DAB CLI"}
                    </button>
                  </div>
                  <p className="text-muted">
                    ZeroQuery uses the official Microsoft Data API builder (<code className="text-text font-mono">dab</code>) CLI to serve your database via MCP. Run <code className="text-text font-mono">dotnet tool install -g Microsoft.DataApiBuilder</code> or click Install above.
                  </p>
                  {dabInstallMessage && (
                    <div className="mt-1 font-mono text-[11px] text-text whitespace-pre-wrap rounded bg-bg p-2 border border-border">
                      {dabInstallMessage}
                    </div>
                  )}
                </div>
              )}

              {dabStatus && dabStatus.isInstalled && (
                <div className="flex items-center gap-1.5 text-xs text-muted">
                  <span className="text-teal font-medium">✓</span>
                  <span>DAB CLI ready: <code className="font-mono text-text">{dabStatus.version || "installed"}</code></span>
                </div>
              )}

              {instanceError && (
                <div className="flex flex-col gap-2 rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
                  <div>{instanceError}</div>
                  {(instanceError.toLowerCase().includes("dab") || instanceError.toLowerCase().includes("not find the file") || instanceError.toLowerCase().includes("not installed")) && (
                    <div className="flex flex-wrap items-center gap-2 pt-1">
                      <button
                        type="button"
                        onClick={handleInstallDab}
                        disabled={isInstallingDab}
                        className="rounded bg-teal px-3 py-1 text-xs font-semibold text-bg hover:opacity-90 disabled:opacity-50"
                      >
                        {isInstallingDab ? "Installing DAB..." : "Install DAB CLI"}
                      </button>
                      <span className="text-xs text-muted">or run: <code className="font-mono text-text">dotnet tool install -g Microsoft.DataApiBuilder</code></span>
                    </div>
                  )}
                  {dabInstallMessage && (
                    <div className="mt-1 font-mono text-xs text-text whitespace-pre-wrap rounded bg-bg p-2 border border-border">
                      {dabInstallMessage}
                    </div>
                  )}
                </div>
              )}

              {instance && instance.status === "Error" && instance.lastError && (
                <div className="rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-xs text-danger">
                  {instance.lastError}
                </div>
              )}

              {instance && (instance.status === "Running" || instance.status === "Idle") && (
                <div className="flex flex-col gap-2 rounded-md border border-border/80 bg-surface2/30 p-3">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="flex items-center gap-2">
                      <span className="h-2 w-2 rounded-full bg-teal animate-pulse" />
                      <span className="text-xs font-semibold text-text">DAB Server Online</span>
                      <code className="rounded bg-bg px-2 py-0.5 font-mono text-xs text-teal border border-border">
                        {instance.baseUrl}
                      </code>
                    </div>
                  </div>

                  <div className="flex flex-wrap items-center gap-2 pt-2 border-t border-border/50">
                    {instance.restUrl ? (
                      <a
                        href={instance.restUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="inline-flex items-center gap-1.5 rounded-md border border-border bg-surface px-2.5 py-1.5 text-xs font-medium text-text hover:border-teal hover:text-teal transition-all shadow-xs"
                        title="Browse REST API in a new browser tab"
                      >
                        <span>🌐 REST API</span>
                        <span className="font-mono text-[11px] text-muted">{instance.restUrl}</span>
                        <span className="text-[10px] text-teal">↗</span>
                      </a>
                    ) : (
                      <span className="inline-flex items-center gap-1.5 rounded-md border border-border/40 bg-surface/40 px-2.5 py-1.5 text-xs text-muted">
                        <span>🌐 REST API</span>
                        <span className="text-[10px] italic">Disabled</span>
                      </span>
                    )}

                    {instance.graphqlUrl ? (
                      <a
                        href={instance.graphqlUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="inline-flex items-center gap-1.5 rounded-md border border-border bg-surface px-2.5 py-1.5 text-xs font-medium text-text hover:border-teal hover:text-teal transition-all shadow-xs"
                        title="Open interactive GraphQL Banana Cake Pop IDE in a new tab"
                      >
                        <span>⚡ GraphQL IDE</span>
                        <span className="font-mono text-[11px] text-muted">{instance.graphqlUrl}</span>
                        <span className="text-[10px] text-teal">↗</span>
                      </a>
                    ) : (
                      <span className="inline-flex items-center gap-1.5 rounded-md border border-border/40 bg-surface/40 px-2.5 py-1.5 text-xs text-muted">
                        <span>⚡ GraphQL IDE</span>
                        <span className="text-[10px] italic">Disabled</span>
                      </span>
                    )}

                    <a
                      href={instance.healthUrl || `${instance.baseUrl}/healthz`}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="inline-flex items-center gap-1.5 rounded-md border border-border bg-surface px-2.5 py-1.5 text-xs font-medium text-text hover:border-teal hover:text-teal transition-all shadow-xs"
                      title="Check DAB healthz endpoint in a new browser tab"
                    >
                      <span>🩺 Health Check</span>
                      <span className="font-mono text-[11px] text-muted">{instance.healthUrl || `${instance.baseUrl}/healthz`}</span>
                      <span className="text-[10px] text-teal">↗</span>
                    </a>
                  </div>
                </div>
              )}

              {(!instance || instance.status === "Stopped" || instance.status === "Error") && (
                <div className="flex flex-col gap-2 rounded-md border border-border/80 bg-surface2/30 p-3">
                  <label className="flex items-center gap-2 text-xs font-medium text-text cursor-pointer">
                    <input
                      type="checkbox"
                      checked={shouldSave}
                      onChange={(e) => setShouldSave(e.target.checked)}
                      className="h-3.5 w-3.5 rounded border-border text-teal focus:ring-teal"
                    />
                    Save this connection profile for future return visits
                  </label>
                  {shouldSave && (
                    <input
                      type="text"
                      placeholder={`Connection name (e.g. My ${provider} Database)`}
                      value={connectionName}
                      onChange={(e) => setConnectionName(e.target.value)}
                      className="rounded-md border border-border bg-bg px-2.5 py-1.5 text-xs text-text focus:border-teal focus:outline-none"
                    />
                  )}
                </div>
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
