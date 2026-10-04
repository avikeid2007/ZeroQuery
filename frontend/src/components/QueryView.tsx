"use client";

import { useEffect, useRef, useState } from "react";
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Legend,
  Line,
  LineChart,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import type { PieLabelRenderProps } from "recharts";
import type { OrchestrationProgressDto, UiSpecResponse } from "@/lib/types";
import { queryDabInstanceStream, ApiError } from "@/lib/api";
import FormView from "./FormView";
import AuditLogModal from "./AuditLogModal";

interface QueryViewProps {
  instanceId: string;
}

/**
 * Fixed rendering palette for chart series/pie slices — cycles through the app's design
 * tokens (doc/Zeroquery — Mock Design.html) rather than Recharts' defaults, so charts match
 * the rest of the UI instead of looking like a bolted-on library.
 */
const CHART_COLORS = ["#4FB8A0", "#E3A64A", "#D9756B", "#8891A3", "#6FA8DC", "#B48EAD"];

/** Human-readable copy for each SSE progress stage (doc/Plan.md Phase 5). */
function describeProgress(progress: OrchestrationProgressDto): string {
  switch (progress.stage) {
    case "Thinking":
      return "Thinking…";
    case "ToolCall":
      return `Calling ${progress.toolName ?? "a tool"}…`;
    case "ToolResult":
      return `Got results from ${progress.toolName ?? "tool"}, thinking…`;
    case "Rendering":
      return "Rendering result…";
    default:
      return "Working…";
  }
}

/**
 * Phase 5 query view: a prompt box plus a fixed renderer for whatever UiSpec comes back — no
 * arbitrary rendering, no LLM-authored markup, just the fixed "table"/"chart"/"card"/"stat"
 * shapes described in doc/Plan.md Section 5. Chart rendering uses Recharts (doc/Plan.md
 * Section 7 tech stack). Queries stream live progress via SSE
 * (POST /api/instances/{id}/query/stream) instead of a single blocking request, so the user
 * sees what the tool-calling loop is doing instead of a static spinner.
 */
export default function QueryView({ instanceId }: QueryViewProps) {
  const [prompt, setPrompt] = useState("");
  const [isLoading, setIsLoading] = useState(false);
  const [progress, setProgress] = useState<OrchestrationProgressDto | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [result, setResult] = useState<UiSpecResponse | null>(null);
  const [isAuditOpen, setIsAuditOpen] = useState(false);
  const [elapsedSeconds, setElapsedSeconds] = useState(0);
  const abortControllerRef = useRef<AbortController | null>(null);

  useEffect(() => {
    // Cancel any in-flight stream if the user navigates away/disconnects mid-query.
    return () => abortControllerRef.current?.abort();
  }, []);

  useEffect(() => {
    let timer: ReturnType<typeof setInterval> | null = null;
    if (isLoading) {
      setElapsedSeconds(0);
      const start = Date.now();
      timer = setInterval(() => {
        setElapsedSeconds(Number(((Date.now() - start) / 1000).toFixed(1)));
      }, 100);
    } else {
      setElapsedSeconds(0);
    }
    return () => {
      if (timer) clearInterval(timer);
    };
  }, [isLoading]);

  async function handleSubmit(e?: React.FormEvent) {
    if (e) e.preventDefault();
    if (!prompt.trim() || isLoading) return;

    setIsLoading(true);
    setErrorMessage(null);
    setProgress(null);

    const controller = new AbortController();
    abortControllerRef.current = controller;

    try {
      const spec = await queryDabInstanceStream(
        instanceId,
        { prompt: prompt.trim() },
        (progressEvent) => setProgress(progressEvent),
        controller.signal,
      );
      setResult(spec);
    } catch (err) {
      if (err instanceof DOMException && err.name === "AbortError") return;
      setErrorMessage(err instanceof ApiError ? err.message : "Unexpected error running the query.");
      setResult(null);
    } finally {
      setIsLoading(false);
      setProgress(null);
      abortControllerRef.current = null;
    }
  }

  return (
    <div className="flex min-w-0 flex-col gap-4">
      {/* 1. Prompt Card matching Mockup */}
      <div className="prompt-card">
        <form onSubmit={handleSubmit}>
          <div className="prompt-input-row">
            <input
              type="text"
              value={prompt}
              onChange={(e) => setPrompt(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
                  e.preventDefault();
                  handleSubmit();
                }
              }}
              disabled={isLoading}
              placeholder="Ask anything across your tables in plain English... (e.g. Show top 5 customers by revenue)"
              className="prompt-input"
            />
            <button
              type="submit"
              disabled={isLoading || !prompt.trim()}
              className="btn-run"
            >
              <span>{isLoading ? "Querying…" : "Query"}</span>
              <span>⚡</span>
            </button>
          </div>

          <div className="mt-3 flex flex-wrap items-center justify-between gap-2 text-xs">
            <div className="flex flex-wrap items-center gap-1.5">
              <span className="text-[var(--win-text-muted)] text-[11px] mr-1">
                Press <kbd className="font-mono bg-[var(--win-bg)] border border-[var(--win-border)] px-1 py-0.5 rounded text-[10px]">Ctrl</kbd> + <kbd className="font-mono bg-[var(--win-bg)] border border-[var(--win-border)] px-1 py-0.5 rounded text-[10px]">Enter</kbd> to run
              </span>
              <button
                type="button"
                onClick={() => setPrompt("Show top 5 records by highest value")}
                className="template-chip"
              >
                Top 5 Records
              </button>
              <button
                type="button"
                onClick={() => setPrompt("Count total records grouped by category")}
                className="template-chip"
              >
                Group by Category
              </button>
              <button
                type="button"
                onClick={() => setPrompt("Show recent activity from the last 30 days")}
                className="template-chip"
              >
                Recent Activity
              </button>
              <button
                type="button"
                onClick={() => setPrompt("Summarize total revenue and average values")}
                className="template-chip"
              >
                Summarize Totals
              </button>
            </div>

            <div className="flex items-center gap-2">
              <span className="text-[11px] font-mono text-[var(--win-text-muted)]">
                In-Process IPC (Zero HTTP Ports)
              </span>
              <button
                type="button"
                onClick={() => setIsAuditOpen(true)}
                className="rounded border border-[var(--win-border)] bg-[var(--win-bg)] px-2.5 py-1 text-xs text-[var(--win-text-muted)] hover:text-text transition-colors"
                title="View database write audit trail"
              >
                🛡️ Audit Log
              </button>
            </div>
          </div>
        </form>
      </div>

      {/* 2. Reasoning / Streaming Progress Banner */}
      {isLoading && (
        <div className="reasoning-stream">
          <div className="reasoning-dot" />
          <div className="flex-1 font-mono text-xs flex items-center justify-between">
            <div>
              <span className="text-[var(--win-accent)] font-semibold uppercase mr-2">Streaming Execution:</span>
              <span className="text-[var(--win-text)]">{progress ? describeProgress(progress) : "Reasoning & executing query…"}</span>
            </div>
            <span className="text-[10px] text-[var(--win-text-muted)]">Live Bridge</span>
          </div>
        </div>
      )}

      {/* 3. Error Banner */}
      {errorMessage && (
        <div className="rounded-lg border border-red-500/40 bg-red-500/10 p-3.5 text-xs text-red-400 flex flex-col gap-1.5">
          <div className="font-semibold flex items-center gap-1.5">
            <span>⚠️ Query Error</span>
          </div>
          <div>{errorMessage}</div>
          {errorMessage.toLowerCase().includes("api key") && (
            <div className="text-[var(--win-text-muted)]">
              Tip: Configure your OpenRouter API key in the <strong>LLM Providers</strong> tab.
            </div>
          )}
        </div>
      )}

      {/* 4. Results, Loading Canvas, or Empty State */}
      {result ? (
        <UiSpecRenderer
          spec={result}
          instanceId={instanceId}
          onCancel={() => setResult(null)}
        />
      ) : isLoading ? (
        <div className="query-loading-canvas">
          {/* Top Progress & Stage Status Card */}
          <div className="query-loading-header">
            <div className="flex items-center gap-3">
              <div className="loading-spinner-ring" />
              <div>
                <div className="flex items-center gap-2">
                  <span className="text-sm font-semibold text-text">
                    {progress?.stage === "Thinking" && "Analyzing prompt & schema…"}
                    {progress?.stage === "ToolCall" && `Executing ${progress.toolName || "database tool"}…`}
                    {progress?.stage === "ToolResult" && "Processing results & building views…"}
                    {progress?.stage === "Rendering" && "Rendering visualization…"}
                    {!progress?.stage && "Executing Natural Language Query…"}
                  </span>
                  <span className="loading-pill-timer">{elapsedSeconds}s</span>
                </div>
                <div className="text-xs text-[var(--win-text-muted)] mt-0.5 font-mono">
                  In-Process IPC • Streaming execution live
                </div>
              </div>
            </div>

            {/* Pipeline Stage Indicators */}
            <div className="pipeline-steps-row">
              <div className={`pipeline-step ${!progress || progress.stage === "Thinking" ? "active" : "done"}`}>
                <span className="step-num">1</span>
                <span>Reasoning</span>
              </div>
              <span className="step-arrow">›</span>
              <div
                className={`pipeline-step ${
                  progress?.stage === "ToolCall"
                    ? "active"
                    : progress?.stage === "ToolResult" || progress?.stage === "Rendering"
                    ? "done"
                    : ""
                }`}
              >
                <span className="step-num">2</span>
                <span>DAB Worker</span>
              </div>
              <span className="step-arrow">›</span>
              <div
                className={`pipeline-step ${
                  progress?.stage === "ToolResult" ? "active" : progress?.stage === "Rendering" ? "done" : ""
                }`}
              >
                <span className="step-num">3</span>
                <span>Synthesize</span>
              </div>
              <span className="step-arrow">›</span>
              <div className={`pipeline-step ${progress?.stage === "Rendering" ? "active" : ""}`}>
                <span className="step-num">4</span>
                <span>Render UI</span>
              </div>
            </div>
          </div>

          {/* Shimmer Skeleton Preview */}
          <div className="shimmer-preview-box">
            <div className="shimmer-stats-row">
              <div className="shimmer-stat-card">
                <div className="skeleton-shimmer h-3 w-16 mb-2" />
                <div className="skeleton-shimmer h-6 w-24" />
              </div>
              <div className="shimmer-stat-card">
                <div className="skeleton-shimmer h-3 w-20 mb-2" />
                <div className="skeleton-shimmer h-6 w-16" />
              </div>
              <div className="shimmer-stat-card">
                <div className="skeleton-shimmer h-3 w-14 mb-2" />
                <div className="skeleton-shimmer h-6 w-20" />
              </div>
            </div>

            <div className="shimmer-table">
              <div className="shimmer-table-header">
                <div className="skeleton-shimmer h-3.5 w-24" />
                <div className="skeleton-shimmer h-3.5 w-32" />
                <div className="skeleton-shimmer h-3.5 w-28" />
                <div className="skeleton-shimmer h-3.5 w-20" />
              </div>
              <div className="shimmer-table-body">
                {[1, 2, 3, 4, 5].map((row) => (
                  <div key={row} className="shimmer-table-row">
                    <div className="skeleton-shimmer h-3" style={{ width: `${65 + ((row * 7) % 25)}%` }} />
                    <div className="skeleton-shimmer h-3" style={{ width: `${80 - ((row * 9) % 20)}%` }} />
                    <div className="skeleton-shimmer h-3" style={{ width: `${55 + ((row * 11) % 35)}%` }} />
                    <div className="skeleton-shimmer h-3" style={{ width: `${45 + ((row * 6) % 30)}%` }} />
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      ) : !errorMessage ? (
        <div className="flex flex-col items-center justify-center rounded-lg border border-dashed border-[var(--win-border)] bg-[var(--win-card)]/50 p-10 text-center">
          <span className="text-3xl mb-2">⚡</span>
          <div className="text-sm font-semibold text-text">Ready to Query Live Database</div>
          <p className="text-xs text-[var(--win-text-muted)] mt-1 max-w-md">
            Type any question in plain English or select a template chip above to introspect, aggregate, or visualize your live database records.
          </p>
        </div>
      ) : null}

      <AuditLogModal isOpen={isAuditOpen} onClose={() => setIsAuditOpen(false)} />
    </div>
  );
}

function UiSpecRenderer({
  spec,
  instanceId,
  onCancel,
}: {
  spec: UiSpecResponse;
  instanceId: string;
  onCancel?: () => void;
}) {
  if (spec.type === "Form") {
    return <FormView spec={spec} instanceId={instanceId} onCancel={onCancel} />;
  }

  return (
    <div className="flex flex-col gap-2">
      <div>
        <h3 className="text-sm font-medium text-text">{spec.title}</h3>
        {spec.meta.sourceEntity && (
          <div className="mb-1 text-xs text-muted">
            from <span className="text-amber">{spec.meta.sourceEntity}</span> · {spec.rows.length} row
            {spec.rows.length === 1 ? "" : "s"}
          </div>
        )}
      </div>

      {spec.type === "Table" && spec.rows.length > 0 && <TableRenderer spec={spec} />}
      {spec.type === "Chart" && spec.rows.length > 0 && <ChartRenderer spec={spec} />}
      {spec.type === "Card" && <CardRenderer spec={spec} />}
      {spec.type === "Stat" && <StatRenderer spec={spec} />}

      {((spec.type === "Table" || spec.type === "Chart") && spec.rows.length === 0) && (
        <p className="text-sm text-muted">No rows returned.</p>
      )}
    </div>
  );
}

function resolveColumns(spec: UiSpecResponse) {
  return spec.columns.length > 0
    ? spec.columns
    : Object.keys(spec.rows[0] ?? {}).map((k) => ({ key: k, label: k }));
}

function downloadCsv(columns: { key: string; label: string }[], rows: Record<string, unknown>[], filename: string) {
  const header = columns.map((c) => `"${c.label.replace(/"/g, '""')}"`).join(",");
  const lines = rows.map((r) =>
    columns.map((c) => {
      const v = r[c.key];
      if (v == null) return '""';
      return `"${String(v).replace(/"/g, '""')}"`;
    }).join(",")
  );
  const csvContent = "\uFEFF" + [header, ...lines].join("\r\n");
  const blob = new Blob([csvContent], { type: "text/csv;charset=utf-8;" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `${filename || "query_results"}.csv`;
  a.click();
  URL.revokeObjectURL(url);
}

function TableRenderer({ spec }: { spec: UiSpecResponse }) {
  const columns = resolveColumns(spec);

  return (
    <div className="rounded-lg border border-[var(--win-border)] bg-[var(--win-card)] overflow-hidden shadow-xs">
      <div className="flex flex-wrap items-center justify-between border-b border-[var(--win-border)] px-4 py-2.5 bg-[var(--win-card)] gap-2">
        <div className="flex items-center gap-2 text-xs">
          <span className="font-semibold text-text">{spec.title || "Query Results"}</span>
          <span className="rounded bg-[var(--win-bg)] border border-[var(--win-border)] px-2 py-0.5 text-[11px] font-mono text-[var(--win-accent)]">
            {spec.rows.length} {spec.rows.length === 1 ? "record" : "records"}
          </span>
          {spec.meta.sourceEntity && (
            <span className="text-[var(--win-text-muted)] text-[11px]">
              &bull; entity: <code className="font-mono text-text">{spec.meta.sourceEntity}</code>
            </span>
          )}
        </div>

        <button
          type="button"
          onClick={() => downloadCsv(columns, spec.rows, spec.title || "query_results")}
          className="rounded border border-[var(--win-border)] bg-[var(--win-bg)] px-2.5 py-1 text-xs text-[var(--win-text-muted)] hover:text-text hover:border-[var(--win-accent)] transition-colors flex items-center gap-1.5"
          title="Download records as CSV"
        >
          <span>📥</span>
          <span>Export CSV</span>
        </button>
      </div>

      <div className="max-w-full overflow-x-auto max-h-[500px]">
        <table className="desktop-grid w-full">
          <thead>
            <tr>
              {columns.map((col) => (
                <th key={col.key}>{col.label}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {spec.rows.map((row, i) => (
              <tr key={i}>
                {columns.map((col) => {
                  const val = row[col.key];
                  const strVal = val != null ? String(val) : "—";
                  const isNumeric = typeof val === "number" || (!isNaN(Number(val)) && val !== "" && typeof val !== "boolean");
                  return (
                    <td key={col.key} className={isNumeric ? "font-mono text-xs" : "text-xs"}>
                      {strVal}
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

/**
 * Helper to look up a property on a row object case-insensitively, handling LLMs that
 * produce column keys in lowercase and row properties in PascalCase (or vice-versa).
 */
function getRowValue(row: Record<string, unknown>, key: string): unknown {
  if (key in row) return row[key];
  const lowerKey = key.toLowerCase();
  for (const [k, v] of Object.entries(row)) {
    if (k.toLowerCase() === lowerKey) return v;
  }
  return undefined;
}

/**
 * Robust numeric parser that handles real numbers, numbers in strings, and numbers with
 * currencies/commas/units (e.g. "$1,250.00" -> 1250, "45 orders" -> 45).
 */
function parseNumeric(val: unknown): number {
  if (typeof val === "number") return isNaN(val) ? 0 : val;
  if (typeof val === "string") {
    // Avoid parsing ISO dates like 1996-07-04 as numbers
    if (/^\d{4}-\d{2}/.test(val)) return 0;
    const cleaned = val.replace(/[^0-9.-]+/g, "");
    if (!cleaned) return 0;
    const parsed = parseFloat(cleaned);
    return isNaN(parsed) ? 0 : parsed;
  }
  return 0;
}

/** Formats dates and category timestamps into clean, human-readable labels. */
function formatCategoryLabel(value: unknown): string {
  if (value == null) return "—";
  const str = String(value);

  // Match ISO timestamps e.g. 1996-07-04T00:00:00 or 1996-07-04
  if (/^\d{4}-\d{2}-\d{2}/.test(str)) {
    const d = new Date(str);
    if (!isNaN(d.getTime())) {
      const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
      return `${months[d.getUTCMonth()]} ${d.getUTCFullYear()}`;
    }
    return str.substring(0, 10);
  }

  // Match YYYY-MM
  if (/^\d{4}-\d{2}$/.test(str)) {
    const [y, m] = str.split("-");
    const months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
    const mIdx = parseInt(m, 10) - 1;
    if (mIdx >= 0 && mIdx < 12) {
      return `${months[mIdx]} ${y}`;
    }
  }

  return str;
}

/**
 * Renders "chart" UiSpecs with Recharts with interactive Bar, Line, Pie, and Table view toggles.
 * Features smart column categorization (detects string category vs numeric metrics),
 * case-insensitive property lookup, and numeric cleaning.
 */
function ChartRenderer({ spec }: { spec: UiSpecResponse }) {
  const [activeType, setActiveType] = useState<"Bar" | "Line" | "Pie" | "Table">(
    spec.chartType === "Line" ? "Line" : spec.chartType === "Pie" ? "Pie" : "Bar"
  );

  const rawColumns = resolveColumns(spec);
  if (rawColumns.length === 0 || spec.rows.length === 0) {
    return <p className="text-sm text-muted">No data rows available to plot.</p>;
  }

  // Intelligently classify which column is Category and which are Series metrics
  let categoryCol = rawColumns[0];
  let seriesCols = rawColumns.slice(1);

  if (rawColumns.length >= 2) {
    // Check if column 0 looks numeric while column 1 looks textual/categorical
    const sample = spec.rows.slice(0, 5);
    const col0IsNum = sample.some((r) => typeof getRowValue(r, rawColumns[0].key) === "number");
    const col1IsNum = sample.some((r) => typeof getRowValue(r, rawColumns[1].key) === "number");

    if (col0IsNum && !col1IsNum && rawColumns.length === 2) {
      // Invert: category is col 1, series is col 0
      categoryCol = rawColumns[1];
      seriesCols = [rawColumns[0]];
    }
  } else if (rawColumns.length === 1) {
    // Only 1 column provided (e.g. LLM just sent counts)
    categoryCol = { key: "__idx", label: "#" };
    seriesCols = [rawColumns[0]];
  }

  // Map rows with case-insensitive and numeric cleaning
  const data = spec.rows.map((row, idx) => {
    const rawCat = categoryCol.key === "__idx" ? `#${idx + 1}` : getRowValue(row, categoryCol.key);
    const mapped: Record<string, string | number> = {
      [categoryCol.key]: formatCategoryLabel(rawCat),
    };
    for (const col of seriesCols) {
      mapped[col.key] = parseNumeric(getRowValue(row, col.key));
    }
    return mapped;
  });

  return (
    <div className="flex flex-col gap-2 rounded-md border border-border/80 bg-surface/50 p-3">
      {/* Chart Toolbar */}
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border/60 pb-2">
        <div className="flex items-center gap-2 text-xs text-muted">
          <span className="font-semibold text-text">{spec.rows.length}</span> data points
          <span>·</span>
          <span>Axis: <strong className="text-text">{categoryCol.label}</strong></span>
          {seriesCols.length > 0 && (
            <>
              <span>·</span>
              <span>Metric: <strong className="text-text">{seriesCols.map((c) => c.label).join(", ")}</strong></span>
            </>
          )}
        </div>

        {/* View Switcher: Bar | Line | Pie | Table */}
        <div className="inline-flex rounded-md border border-border bg-surface2/60 p-0.5 text-xs">
          <button
            type="button"
            onClick={() => setActiveType("Bar")}
            className={`rounded px-2.5 py-1 transition-colors ${
              activeType === "Bar" ? "bg-teal font-semibold text-bg shadow-sm" : "text-muted hover:text-text"
            }`}
          >
            📊 Bar
          </button>
          <button
            type="button"
            onClick={() => setActiveType("Line")}
            className={`rounded px-2.5 py-1 transition-colors ${
              activeType === "Line" ? "bg-teal font-semibold text-bg shadow-sm" : "text-muted hover:text-text"
            }`}
          >
            📈 Line
          </button>
          <button
            type="button"
            onClick={() => setActiveType("Pie")}
            className={`rounded px-2.5 py-1 transition-colors ${
              activeType === "Pie" ? "bg-teal font-semibold text-bg shadow-sm" : "text-muted hover:text-text"
            }`}
          >
            🥧 Pie
          </button>
          <button
            type="button"
            onClick={() => setActiveType("Table")}
            className={`rounded px-2.5 py-1 transition-colors ${
              activeType === "Table" ? "bg-teal font-semibold text-bg shadow-sm" : "text-muted hover:text-text"
            }`}
          >
            📋 Table
          </button>
        </div>
      </div>

      {activeType === "Table" ? (
        <TableRenderer spec={spec} />
      ) : activeType === "Pie" ? (
        <div className="h-72 w-full min-w-0">
          <ResponsiveContainer width="100%" height="100%">
            <PieChart>
              <Pie
                data={data}
                dataKey={seriesCols[0]?.key ?? categoryCol.key}
                nameKey={categoryCol.key}
                cx="50%"
                cy="50%"
                outerRadius="75%"
                label={(props: PieLabelRenderProps) => String(props.name ?? "")}
              >
                {data.map((_, i) => (
                  <Cell key={i} fill={CHART_COLORS[i % CHART_COLORS.length]} />
                ))}
              </Pie>
              <Tooltip
                contentStyle={{
                  background: "var(--surface)",
                  border: "1px solid var(--border)",
                  borderRadius: "0.375rem",
                  fontSize: 12,
                  color: "var(--text)",
                }}
                formatter={(val: unknown) => [typeof val === "number" ? val.toLocaleString() : String(val), ""]}
              />
              <Legend wrapperStyle={{ fontSize: 12 }} />
            </PieChart>
          </ResponsiveContainer>
        </div>
      ) : (
        <div className="h-72 w-full min-w-0">
          <ResponsiveContainer width="100%" height="100%">
            {activeType === "Line" ? (
              <LineChart data={data} margin={{ top: 12, right: 12, left: 0, bottom: data.length > 5 ? 20 : 8 }}>
                <CartesianGrid stroke="var(--border)" strokeDasharray="3 3" />
                <XAxis
                  dataKey={categoryCol.key}
                  stroke="var(--muted)"
                  fontSize={11}
                  tickLine={false}
                  interval={data.length > 15 ? "preserveStartEnd" : 0}
                  angle={data.length > 5 ? -25 : 0}
                  textAnchor={data.length > 5 ? "end" : "middle"}
                  height={data.length > 5 ? 45 : 30}
                />
                <YAxis stroke="var(--muted)" fontSize={11} tickLine={false} />
                <Tooltip
                  contentStyle={{
                    background: "var(--surface)",
                    border: "1px solid var(--border)",
                    borderRadius: "0.375rem",
                    fontSize: 12,
                    color: "var(--text)",
                  }}
                  formatter={(val: unknown) => [typeof val === "number" ? val.toLocaleString() : String(val), ""]}
                />
                {seriesCols.length > 1 && <Legend wrapperStyle={{ fontSize: 12 }} />}
                {seriesCols.map((col, i) => (
                  <Line
                    key={col.key}
                    type="monotone"
                    dataKey={col.key}
                    name={col.label}
                    stroke={CHART_COLORS[i % CHART_COLORS.length]}
                    strokeWidth={2.5}
                    dot={{ r: 3, fill: CHART_COLORS[i % CHART_COLORS.length] }}
                  />
                ))}
              </LineChart>
            ) : (
              <BarChart data={data} margin={{ top: 12, right: 12, left: 0, bottom: data.length > 5 ? 20 : 8 }}>
                <CartesianGrid stroke="var(--border)" strokeDasharray="3 3" />
                <XAxis
                  dataKey={categoryCol.key}
                  stroke="var(--muted)"
                  fontSize={11}
                  tickLine={false}
                  interval={data.length > 15 ? "preserveStartEnd" : 0}
                  angle={data.length > 5 ? -25 : 0}
                  textAnchor={data.length > 5 ? "end" : "middle"}
                  height={data.length > 5 ? 45 : 30}
                />
                <YAxis stroke="var(--muted)" fontSize={11} tickLine={false} />
                <Tooltip
                  contentStyle={{
                    background: "var(--surface)",
                    border: "1px solid var(--border)",
                    borderRadius: "0.375rem",
                    fontSize: 12,
                    color: "var(--text)",
                  }}
                  formatter={(val: unknown) => [typeof val === "number" ? val.toLocaleString() : String(val), ""]}
                />
                {seriesCols.length > 1 && <Legend wrapperStyle={{ fontSize: 12 }} />}
                {seriesCols.map((col, i) => (
                  <Bar
                    key={col.key}
                    dataKey={col.key}
                    name={col.label}
                    fill={CHART_COLORS[i % CHART_COLORS.length]}
                    radius={[4, 4, 0, 0]}
                  />
                ))}
              </BarChart>
            )}
          </ResponsiveContainer>
        </div>
      )}
    </div>
  );
}

function CardRenderer({ spec }: { spec: UiSpecResponse }) {
  const row = spec.rows[0];
  if (!row) {
    return <p className="text-sm text-[var(--win-text-muted)]">No data to display.</p>;
  }

  const columns = resolveColumns(spec);

  return (
    <div className="rounded-lg border border-[var(--win-border)] bg-[var(--win-card)] p-4 shadow-xs">
      <div className="text-xs font-semibold text-text mb-3 border-b border-[var(--win-border)] pb-2">
        {spec.title || "Record Details"}
      </div>
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        {columns.map((col) => (
          <div key={col.key} className="flex flex-col gap-1 rounded border border-[var(--win-border)]/60 bg-[var(--win-bg)] p-2.5">
            <span className="text-[11px] text-[var(--win-text-muted)]">{col.label}</span>
            <span className="font-mono text-xs text-text font-medium break-all">{String(row[col.key] ?? "—")}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

/** Renders "stat" UiSpecs — one big highlighted number/value, e.g. an aggregate count. */
function StatRenderer({ spec }: { spec: UiSpecResponse }) {
  const row = spec.rows[0];
  const columns = resolveColumns(spec);
  const primaryColumn = columns[0];
  const value = row && primaryColumn ? row[primaryColumn.key] : undefined;

  return (
    <div className="kpi-box max-w-sm">
      <div className="kpi-label">{primaryColumn?.label || spec.title || "Statistic"}</div>
      <div className="kpi-value">{value != null ? String(value) : "—"}</div>
      {spec.meta.sourceEntity && (
        <div className="kpi-trend text-[var(--win-text-muted)] mt-1">
          from <span className="text-[var(--win-accent)]">{spec.meta.sourceEntity}</span>
        </div>
      )}
    </div>
  );
}
