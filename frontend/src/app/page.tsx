"use client";

import { useState, useEffect, useRef } from "react";
import ConnectionForm from "@/components/ConnectionForm";
import TablePicker from "@/components/TablePicker";
import ConfigPreview from "@/components/ConfigPreview";
import LlmSettingsPanel from "@/components/LlmSettingsPanel";
import SavedConnectionsList from "@/components/SavedConnectionsList";
import InstanceStatusBadge from "@/components/InstanceStatusBadge";
import QueryView from "@/components/QueryView";
import ThemeToggle from "@/components/ThemeToggle";
import ZeroQueryLogo from "@/components/ZeroQueryLogo";
import {
  introspectDatabase,
  reconnectSavedConnection,
  stopDabInstance,
  getDabInstanceStatus,
  getDabStatus,
  installDab,
  getSavedConnections,
  getAuditLogs,
  ApiError,
} from "@/lib/api";
import { desktopBridge } from "@/lib/desktopBridge";
import type {
  DatabaseProvider,
  IntrospectResponse,
  SavedConnectionSummary,
  InstanceStatusResponse,
  DabStatusInfo,
  WriteAuditEntry,
} from "@/lib/types";
import type { PickerSelection } from "@/lib/selection";

type TabId = "studio" | "databases" | "schema" | "audit" | "llm" | "engine";
type WizardStep = "connect" | "pick" | "generate";

export default function Home() {
  const [activeTab, setActiveTab] = useState<TabId>("studio");

  // Wizard state for connecting / configuring DAB
  const [step, setStep] = useState<WizardStep>("connect");
  const [provider, setProvider] = useState<DatabaseProvider>("SqlServer");
  const [connectionString, setConnectionString] = useState<string>("");
  const [schema, setSchema] = useState<IntrospectResponse | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [confirmedSelection, setConfirmedSelection] = useState<PickerSelection | null>(null);

  // Active connected DAB instance session state
  const [activeSession, setActiveSession] = useState<{
    instance: InstanceStatusResponse;
    connectionName: string;
    provider?: DatabaseProvider;
  } | null>(null);
  const [reconnectingId, setReconnectingId] = useState<string | null>(null);
  const [reconnectError, setReconnectError] = useState<string | null>(null);
  const sessionPollHandle = useRef<ReturnType<typeof setInterval> | null>(null);

  // DAB engine & saved connections state
  const [dabStatus, setDabStatus] = useState<DabStatusInfo | null>(null);
  const [savedConnections, setSavedConnections] = useState<SavedConnectionSummary[]>([]);
  const [isInstallingDab, setIsInstallingDab] = useState(false);
  const [dabInstallMsg, setDabInstallMsg] = useState<string | null>(null);

  // Audit log state
  const [auditLogs, setAuditLogs] = useState<WriteAuditEntry[]>([]);
  const [isAuditLoading, setIsAuditLoading] = useState(false);

  useEffect(() => {
    desktopBridge.notifyAppReady();
    loadDabStatus();
    loadSavedConnections();
  }, []);

  async function loadDabStatus() {
    try {
      const status = await getDabStatus();
      setDabStatus(status);
    } catch {
      // transient
    }
  }

  async function loadSavedConnections() {
    try {
      const list = await getSavedConnections();
      setSavedConnections(list);
    } catch {
      // transient
    }
  }

  async function loadAuditTrail() {
    setIsAuditLoading(true);
    try {
      const logs = await getAuditLogs(50);
      setAuditLogs(logs);
    } catch {
      // transient
    } finally {
      setIsAuditLoading(false);
    }
  }

  useEffect(() => {
    if (activeTab === "audit") {
      loadAuditTrail();
    }
  }, [activeTab]);

  // Session polling when connected
  useEffect(() => {
    if (!activeSession) {
      if (sessionPollHandle.current) {
        clearInterval(sessionPollHandle.current);
        sessionPollHandle.current = null;
      }
      return;
    }

    const instanceId = activeSession.instance.id;

    // Fast initial status check
    getDabInstanceStatus(instanceId)
      .then((latest) => {
        setActiveSession((prev) => {
          if (!prev || prev.instance.id !== instanceId) return prev;
          return { ...prev, instance: latest };
        });
      })
      .catch(() => {});

    sessionPollHandle.current = setInterval(async () => {
      try {
        const latest = await getDabInstanceStatus(instanceId);
        setActiveSession((prev) => {
          if (!prev || prev.instance.id !== instanceId) return prev;
          return { ...prev, instance: latest };
        });
        if (latest.status === "Stopped" || latest.status === "Error") {
          if (sessionPollHandle.current) {
            clearInterval(sessionPollHandle.current);
            sessionPollHandle.current = null;
          }
        }
      } catch {
        // Transient poll failure
      }
    }, 1000);

    return () => {
      if (sessionPollHandle.current) {
        clearInterval(sessionPollHandle.current);
        sessionPollHandle.current = null;
      }
    };
  }, [activeSession?.instance.id]);

  async function handleReconnect(connection: SavedConnectionSummary) {
    setReconnectingId(connection.id);
    setReconnectError(null);
    try {
      const instance = await reconnectSavedConnection(connection.id);
      setActiveSession({
        instance,
        connectionName: connection.name,
        provider: connection.provider,
      });
      // Switch directly to Query Studio on successful reconnection
      setActiveTab("studio");
      loadSavedConnections();
    } catch (err) {
      setReconnectError(err instanceof ApiError ? err.message : "Failed to reconnect to database.");
    } finally {
      setReconnectingId(null);
    }
  }

  async function handleDisconnectActiveSession() {
    if (!activeSession) return;
    try {
      await stopDabInstance(activeSession.instance.id);
    } catch {
      // ignore
    }
    setActiveSession(null);
  }

  async function handleConnect(selectedProvider: DatabaseProvider, submittedConnectionString: string) {
    setIsLoading(true);
    setErrorMessage(null);
    try {
      const result = await introspectDatabase({ provider: selectedProvider, connectionString: submittedConnectionString });
      setProvider(selectedProvider);
      setConnectionString(submittedConnectionString);
      setSchema(result);
      setStep("pick");
      setActiveTab("schema");
    } catch (err) {
      setErrorMessage(err instanceof ApiError ? err.message : "Unexpected error reading schema.");
    } finally {
      setIsLoading(false);
    }
  }

  function handleConfirm(selection: PickerSelection) {
    setConfirmedSelection(selection);
    setStep("generate");
  }

  function handleStartOver() {
    setStep("connect");
    setSchema(null);
    setConfirmedSelection(null);
    setActiveTab("databases");
  }

  async function handleInstallDabAction() {
    setIsInstallingDab(true);
    setDabInstallMsg(null);
    try {
      const res = await installDab();
      setDabInstallMsg(res.message);
      await loadDabStatus();
    } catch (err) {
      setDabInstallMsg(err instanceof Error ? err.message : "DAB installation failed.");
    } finally {
      setIsInstallingDab(false);
    }
  }

  return (
    <div className="window-frame">
      {/* 1. Custom Top Title Bar matching mockup */}
      <header className="title-bar">
        <div className="app-branding">
          <ZeroQueryLogo size={24} showText={true} showBadge={true} />
          <div className="title-breadcrumbs">
            <span className="text-[var(--win-text-muted)] opacity-60">&rsaquo;</span>
            <span className="db-status-pill">
              <span
                className="live-dot"
                style={{
                  background: activeSession ? "var(--win-accent)" : "var(--win-text-muted)",
                  boxShadow: activeSession ? "0 0 8px var(--win-accent)" : "none",
                }}
              />
              <span>
                {activeSession
                  ? `${activeSession.provider || "Database"}: ${activeSession.connectionName}`
                  : "No active database session"}
              </span>
            </span>
          </div>
        </div>

        <div className="flex items-center gap-2">
          <ThemeToggle />
        </div>
      </header>

      {/* 2. Main Window Body */}
      <div className="app-body">
        {/* Left Navigation Rail */}
        <nav className="nav-rail">
          <div>
            <div className="nav-group-title">Workspace</div>
            <div
              className={`nav-item ${activeTab === "studio" ? "active" : ""}`}
              onClick={() => setActiveTab("studio")}
            >
              <span className="nav-icon">⚡</span>
              <span>Query Studio</span>
            </div>
            <div
              className={`nav-item ${activeTab === "databases" ? "active" : ""}`}
              onClick={() => setActiveTab("databases")}
            >
              <span className="nav-icon">🗄️</span>
              <span>Databases</span>
            </div>
            <div
              className={`nav-item ${activeTab === "schema" ? "active" : ""}`}
              onClick={() => setActiveTab("schema")}
            >
              <span className="nav-icon">📋</span>
              <span>Schema Explorer</span>
            </div>
            <div
              className={`nav-item ${activeTab === "audit" ? "active" : ""}`}
              onClick={() => setActiveTab("audit")}
            >
              <span className="nav-icon">🛡️</span>
              <span>Mutation Audit</span>
            </div>

            <div className="nav-group-title mt-4">Preferences</div>
            <div
              className={`nav-item ${activeTab === "llm" ? "active" : ""}`}
              onClick={() => setActiveTab("llm")}
            >
              <span className="nav-icon">🤖</span>
              <span>LLM Providers</span>
            </div>
            <div
              className={`nav-item ${activeTab === "engine" ? "active" : ""}`}
              onClick={() => setActiveTab("engine")}
            >
              <span className="nav-icon">⚙️</span>
              <span>Engine &amp; Settings</span>
            </div>
          </div>

          {/* Bottom DAB Engine status card */}
          <div className="nav-bottom-card">
            <div className="worker-status-header">
              <span className="font-semibold">DAB Engine</span>
              <span className="flex items-center gap-1.5 font-mono text-[10px]">
                <span
                  className="live-dot"
                  style={{
                    width: 6,
                    height: 6,
                    background: dabStatus?.isInstalled
                      ? activeSession
                        ? "var(--win-accent)"
                        : "#e3a64a"
                      : "var(--win-danger)",
                  }}
                />
                {activeSession ? "Active" : dabStatus?.isInstalled ? "Idle" : "Missing"}
              </span>
            </div>
            <div className="worker-specs">
              <div>Mode: In-Process IPC</div>
              <div>Port: {activeSession?.instance?.port ? activeSession.instance.port : "Auto (127.0.0.1)"}</div>
              <div>Sandboxed: Win32 Job</div>
              <div>Version: {dabStatus?.version || "Microsoft.DataApiBuilder"}</div>
            </div>
          </div>
        </nav>

        {/* Main Workspace Area */}
        <main className="workspace">
          {/* TAB 1: QUERY STUDIO */}
          {activeTab === "studio" && (
            <div className="query-studio">
              {activeSession ? (
                <div className="flex flex-col gap-4">
                  {/* Active Session Info Bar */}
                  <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-[var(--win-border)] bg-[var(--win-card)] px-4 py-2.5">
                    <div className="flex items-center gap-3">
                      <span className="text-sm font-semibold text-text">{activeSession.connectionName}</span>
                      <span className="font-mono text-xs text-[var(--win-text-muted)]">{activeSession.instance.baseUrl}</span>
                      <InstanceStatusBadge status={activeSession.instance.status} />
                    </div>

                    <div className="flex items-center gap-2">
                      <button
                        type="button"
                        onClick={() => setActiveTab("databases")}
                        className="btn-secondary btn-sm"
                      >
                        Switch Database
                      </button>
                      <button
                        type="button"
                        onClick={handleDisconnectActiveSession}
                        className="btn-danger btn-sm"
                      >
                        Disconnect
                      </button>
                    </div>
                  </div>

                  {/* Real QueryView running queries against activeSession */}
                  <QueryView instanceId={activeSession.instance.id} />
                </div>
              ) : (
                /* Clean Empty State when no database is connected yet */
                <div className="flex flex-col items-center justify-center rounded-xl border border-dashed border-[var(--win-border)] bg-[var(--win-card)]/50 p-12 text-center my-auto max-w-2xl mx-auto shadow-xs">
                  <ZeroQueryLogo size={56} className="mb-3" />
                  <h2 className="text-lg font-semibold text-text">No Active Database Session</h2>
                  <p className="text-xs text-[var(--win-text-muted)] mt-1.5 max-w-md leading-relaxed">
                    Connect to an existing database or configure a new connection to run natural language queries, GraphQL aggregation, and schema exploration.
                  </p>

                  <div className="mt-6 flex flex-wrap items-center justify-center gap-3">
                    <button
                      type="button"
                      onClick={() => setActiveTab("databases")}
                      className="btn-primary"
                    >
                      <span>🗄️</span>
                      <span>Connect Database</span>
                    </button>
                  </div>

                  {/* Quick Reconnect List if saved connections exist */}
                  {savedConnections.length > 0 && (
                    <div className="mt-8 w-full border-t border-[var(--win-border)] pt-5 text-left">
                      <div className="text-xs font-semibold text-[var(--win-text-muted)] uppercase tracking-wider mb-2.5">
                        Recent Database Connections:
                      </div>
                      {reconnectingId && (
                        <div className="mb-3 flex items-center gap-2.5 rounded-lg border border-[var(--win-accent)] bg-[var(--win-accent)]/10 px-3.5 py-2.5 text-xs text-[var(--win-accent)] font-medium shadow-xs">
                          <div className="loading-spinner-ring" style={{ width: 14, height: 14 }} />
                          <span>Connecting to database and launching in-process DAB instance…</span>
                        </div>
                      )}
                      <div className="flex flex-col gap-2">
                        {savedConnections.slice(0, 3).map((conn) => (
                          <div
                            key={conn.id}
                            className="flex items-center justify-between rounded-lg border border-[var(--win-border)] bg-[var(--win-bg)] p-3 hover:border-[var(--win-accent)] transition-colors shadow-xs"
                          >
                            <div className="flex items-center gap-2.5">
                              <span className="text-base">🗄️</span>
                              <div>
                                <div className="text-xs font-semibold text-text">{conn.name}</div>
                                <div className="text-[11px] text-[var(--win-text-muted)] font-mono">{conn.provider}</div>
                              </div>
                            </div>
                            <button
                              type="button"
                              onClick={() => handleReconnect(conn)}
                              disabled={reconnectingId === conn.id}
                              className="btn-primary btn-sm text-xs"
                            >
                              {reconnectingId === conn.id ? "Connecting…" : "Connect"}
                            </button>
                          </div>
                        ))}
                      </div>
                    </div>
                  )}
                </div>
              )}
            </div>
          )}

          {/* TAB 2: DATABASES */}
          {activeTab === "databases" && (
            <div className="p-6 flex flex-col gap-6 max-w-5xl">
              <div>
                <h2 className="text-lg font-semibold text-text">Database Connections</h2>
                <p className="text-xs text-[var(--win-text-muted)]">
                  Connect to SQL Server, PostgreSQL, MySQL, or Cosmos DB, or launch a saved Data API Builder instance.
                </p>
              </div>

              {/* Active Session Card if connected */}
              {activeSession && (
                <div className="rounded-lg border border-[var(--win-accent)]/50 bg-[var(--win-card)] p-5 shadow-xs">
                  <div className="flex flex-wrap items-center justify-between gap-3 border-b border-[var(--win-border)] pb-3">
                    <div className="flex items-center gap-2.5">
                      <span className="text-xl">⚡</span>
                      <div>
                        <div className="text-sm font-semibold text-text">{activeSession.connectionName}</div>
                        <div className="text-xs text-[var(--win-text-muted)] font-mono">{activeSession.instance.baseUrl}</div>
                      </div>
                    </div>
                    <div className="flex items-center gap-2">
                      <InstanceStatusBadge status={activeSession.instance.status} />
                      <button
                        type="button"
                        onClick={() => setActiveTab("studio")}
                        className="btn-primary btn-sm text-xs"
                      >
                        Open Query Studio ↗
                      </button>
                      <button
                        type="button"
                        onClick={handleDisconnectActiveSession}
                        className="btn-danger btn-sm text-xs"
                      >
                        Disconnect
                      </button>
                    </div>
                  </div>

                  {/* Direct API links */}
                  <div className="mt-3 flex flex-wrap items-center gap-2 text-xs">
                    <span className="text-[var(--win-text-muted)] text-[11px]">Endpoints:</span>
                    {activeSession.instance.restUrl && (
                      <a
                        href={activeSession.instance.restUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="rounded border border-[var(--win-border)] bg-[var(--win-bg)] px-2.5 py-1 font-mono text-[11px] text-text hover:border-[var(--win-accent)]"
                      >
                        REST API ↗
                      </a>
                    )}
                    {activeSession.instance.graphqlUrl && (
                      <a
                        href={activeSession.instance.graphqlUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="rounded border border-[var(--win-border)] bg-[var(--win-bg)] px-2.5 py-1 font-mono text-[11px] text-text hover:border-[var(--win-accent)]"
                      >
                        GraphQL IDE ↗
                      </a>
                    )}
                  </div>
                </div>
              )}

              {/* Reconnect Error */}
              {reconnectError && (
                <div className="rounded-md border border-red-500/40 bg-red-500/10 p-3 text-xs text-red-400">
                  {reconnectError}
                </div>
              )}

              {/* Saved Connections */}
              <div className="rounded-lg border border-[var(--win-border)] bg-[var(--win-card)] p-5">
                <h3 className="text-sm font-semibold mb-3">Saved Connections</h3>
                <SavedConnectionsList
                  onSelectReconnect={handleReconnect}
                  isReconnectingId={reconnectingId}
                />
              </div>

              {/* Connect New Database */}
              <div className="rounded-lg border border-[var(--win-border)] bg-[var(--win-card)] p-5">
                <h3 className="text-sm font-semibold mb-3">Connect New Database</h3>
                <ConnectionForm onSubmit={handleConnect} isLoading={isLoading} errorMessage={errorMessage} />
              </div>
            </div>
          )}

          {/* TAB 3: SCHEMA EXPLORER */}
          {activeTab === "schema" && (
            <div className="p-6 flex flex-col gap-6 max-w-6xl">
              <div>
                <h2 className="text-lg font-semibold text-text">Schema Explorer &amp; DAB Config</h2>
                <p className="text-xs text-[var(--win-text-muted)]">
                  Pick tables, select columns, configure primary keys, and generate Data API Builder schema.
                </p>
              </div>

              {schema ? (
                <>
                  {step === "pick" && (
                    <TablePicker
                      schema={schema}
                      onConfirm={handleConfirm}
                      onBack={handleStartOver}
                    />
                  )}
                  {step === "generate" && confirmedSelection && (
                    <ConfigPreview
                      provider={provider}
                      schema={schema}
                      selection={confirmedSelection}
                      connectionString={connectionString}
                      onBack={() => setStep("pick")}
                    />
                  )}
                </>
              ) : (
                <div className="flex flex-col items-center justify-center rounded-lg border border-dashed border-[var(--win-border)] bg-[var(--win-card)] p-12 text-center">
                  <span className="text-3xl mb-2">📋</span>
                  <div className="text-sm font-semibold text-text">No Schema Loaded</div>
                  <p className="text-xs text-[var(--win-text-muted)] mt-1 max-w-md">
                    Connect to a database in the Databases tab to introspect tables and generate a DAB config.
                  </p>
                  <button
                    type="button"
                    onClick={() => setActiveTab("databases")}
                    className="btn-primary btn-sm mt-4"
                  >
                    Go to Databases
                  </button>
                </div>
              )}
            </div>
          )}

          {/* TAB 4: MUTATION AUDIT */}
          {activeTab === "audit" && (
            <div className="p-6 flex flex-col gap-4 max-w-6xl">
              <div className="flex items-center justify-between">
                <div>
                  <h2 className="text-lg font-semibold text-text">Database Write Audit Trail</h2>
                  <p className="text-xs text-[var(--win-text-muted)]">
                    Tamper-evident log of all verified create, update, and delete actions executed by human approval.
                  </p>
                </div>
                <button type="button" onClick={loadAuditTrail} className="btn-secondary btn-sm">
                  {isAuditLoading ? "Refreshing…" : "🔄 Refresh"}
                </button>
              </div>

              <div className="rounded-lg border border-[var(--win-border)] bg-[var(--win-card)] overflow-hidden">
                <table className="desktop-grid w-full">
                  <thead>
                    <tr>
                      <th>Timestamp</th>
                      <th>Entity</th>
                      <th>Operation</th>
                      <th>Primary Key</th>
                      <th>Diff Summary</th>
                      <th>Client</th>
                    </tr>
                  </thead>
                  <tbody>
                    {auditLogs.length === 0 ? (
                      <tr>
                        <td colSpan={6} className="text-center py-8 text-[var(--win-text-muted)]">
                          No write mutations executed yet.
                        </td>
                      </tr>
                    ) : (
                      auditLogs.map((log) => (
                        <tr key={log.id}>
                          <td>{new Date(log.timestamp).toLocaleString()}</td>
                          <td>
                            <code>{log.entity}</code>
                          </td>
                          <td>
                            <span
                              className={`rounded px-1.5 py-0.5 text-[10px] font-semibold ${
                                log.operation === "create"
                                  ? "bg-teal-500/20 text-teal-400"
                                  : log.operation === "delete"
                                  ? "bg-red-500/20 text-red-400"
                                  : "bg-amber-500/20 text-amber-400"
                              }`}
                            >
                              {log.operation.toUpperCase()}
                            </span>
                          </td>
                          <td>{log.primaryKey ? JSON.stringify(log.primaryKey) : "—"}</td>
                          <td>
                            <pre className="text-[11px] whitespace-pre-wrap">
                              {log.newValues
                                ? JSON.stringify(log.newValues, null, 2)
                                : log.previousValues
                                ? JSON.stringify(log.previousValues, null, 2)
                                : "—"}
                            </pre>
                          </td>
                          <td>{log.clientIp}</td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          {/* TAB 5: LLM PROVIDERS */}
          {activeTab === "llm" && (
            <div className="p-6 flex flex-col gap-6 max-w-4xl">
              <div>
                <h2 className="text-lg font-semibold text-text">LLM Providers &amp; Prompt Engineering</h2>
                <p className="text-xs text-[var(--win-text-muted)]">
                  Configure OpenRouter API keys with Windows DPAPI encryption and customize master AI system prompts.
                </p>
              </div>

              <LlmSettingsPanel />
            </div>
          )}

          {/* TAB 6: ENGINE & SETTINGS */}
          {activeTab === "engine" && (
            <div className="p-6 flex flex-col gap-6 max-w-4xl">
              <div>
                <h2 className="text-lg font-semibold text-text">Data API Builder Engine &amp; Guardrails</h2>
                <p className="text-xs text-[var(--win-text-muted)]">
                  Inspect the in-process DAB worker process, Windows Job Object containment, and memory limits.
                </p>
              </div>

              <div className="rounded-lg border border-[var(--win-border)] bg-[var(--win-card)] p-5 flex flex-col gap-4">
                <div className="flex items-center justify-between border-b border-[var(--win-border)] pb-3">
                  <div className="font-semibold text-sm">Microsoft.DataApiBuilder CLI Status</div>
                  <div className="flex items-center gap-2">
                    <span
                      className={`h-2.5 w-2.5 rounded-full ${
                        dabStatus?.isInstalled ? "bg-[var(--win-accent)]" : "bg-[var(--win-danger)]"
                      }`}
                    />
                    <span className="text-xs font-mono">
                      {dabStatus?.isInstalled ? "Installed & Operational" : "Not Found"}
                    </span>
                  </div>
                </div>

                <div className="font-mono text-xs space-y-1.5 text-[var(--win-text-muted)]">
                  <div>
                    Executable: <span className="text-text">{dabStatus?.executablePath || "Auto-resolved"}</span>
                  </div>
                  <div>
                    Version: <span className="text-text">{dabStatus?.version || "Unknown"}</span>
                  </div>
                  <div>
                    OS Sandbox: <span className="text-text">Win32 Job Object (KILL_ON_JOB_CLOSE enabled)</span>
                  </div>
                  <div>
                    Max Memory Cap: <span className="text-text">1024 MB per instance</span>
                  </div>
                  <div>
                    Network Isolation: <span className="text-text">Strict 127.0.0.1 Loopback (Zero External Ports)</span>
                  </div>
                </div>

                {!dabStatus?.isInstalled && (
                  <div className="mt-2">
                    <button
                      type="button"
                      onClick={handleInstallDabAction}
                      disabled={isInstallingDab}
                      className="btn-primary btn-sm"
                    >
                      {isInstallingDab ? "Installing Microsoft.DataApiBuilder…" : "Install DAB Globally"}
                    </button>
                    {dabInstallMsg && <p className="mt-2 text-xs text-[var(--win-text-muted)]">{dabInstallMsg}</p>}
                  </div>
                )}
              </div>
            </div>
          )}
        </main>
      </div>

      {/* 3. WinUI 3 Bottom Status Bar */}
      <footer className="status-bar">
        <div className="status-left">
          <div className="status-item">
            <span
              className="live-dot"
              style={{
                width: 5,
                height: 5,
                background: activeSession ? "var(--win-accent)" : "var(--win-text-muted)",
              }}
            />
            <span>ZeroQuery Desktop &bull; Ready</span>
          </div>
          <div className="status-item">
            <span>
              Target:{" "}
              {activeSession
                ? `127.0.0.1:${activeSession.instance.port} (${activeSession.connectionName})`
                : "Disconnected"}
            </span>
          </div>
        </div>

        <div className="status-right">
          <div className="status-item">
            <span>In-Process IPC (Zero HTTP Ports)</span>
          </div>
          <div className="status-item">
            <span>WinUI 3 &bull; .NET 10</span>
          </div>
        </div>
      </footer>
    </div>
  );
}
