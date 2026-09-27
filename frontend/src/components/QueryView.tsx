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
  const abortControllerRef = useRef<AbortController | null>(null);

  useEffect(() => {
    // Cancel any in-flight stream if the user navigates away/disconnects mid-query.
    return () => abortControllerRef.current?.abort();
  }, []);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
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
    <div className="flex min-w-0 flex-col gap-3 rounded-md border border-border">
      <div className="flex items-center gap-2 border-b border-border p-3">
        <form onSubmit={handleSubmit} className="flex min-w-0 flex-1 gap-2">
          <input
            type="text"
            value={prompt}
            onChange={(e) => setPrompt(e.target.value)}
            disabled={isLoading}
            placeholder="e.g. Show me the 5 most expensive products, or Change price of Chai to $19.99"
            className="flex-1 rounded-md border border-border bg-bg px-3 py-2 text-sm text-text focus:border-teal focus:outline-none disabled:opacity-50"
          />
          <button
            type="submit"
            disabled={isLoading || !prompt.trim()}
            className="inline-flex items-center justify-center rounded-md bg-teal px-4 py-2 text-sm font-medium text-bg disabled:cursor-not-allowed disabled:opacity-50"
          >
            {isLoading ? "Working…" : "Ask"}
          </button>
        </form>

        <button
          type="button"
          onClick={() => setIsAuditOpen(true)}
          className="rounded-md border border-border px-3 py-2 text-xs font-medium text-muted hover:text-text hover:bg-surface2 transition-colors whitespace-nowrap"
          title="View database write audit trail"
        >
          Audit Log
        </button>
      </div>

      <div className="flex flex-col gap-3 p-3 pt-0">
        {isLoading && (
          <div className="flex items-center gap-2 text-sm text-muted">
            <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-amber" />
            {progress ? describeProgress(progress) : "Starting…"}
          </div>
        )}
        {errorMessage && (
          <div className="flex flex-col gap-1.5 rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
            <span>{errorMessage}</span>
            {errorMessage.toLowerCase().includes("api key") && (
              <span className="text-xs text-muted">
                Tip: Enter your OpenRouter API key in the <strong>LLM settings</strong> bar at the top of the page, or set the <code className="font-mono text-text">OPENROUTER_API_KEY</code> environment variable.
              </span>
            )}
          </div>
        )}

        {result && (
          <UiSpecRenderer
            spec={result}
            instanceId={instanceId}
            onCancel={() => setResult(null)}
          />
        )}
      </div>

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

function TableRenderer({ spec }: { spec: UiSpecResponse }) {
  const columns = resolveColumns(spec);

  return (
    <div className="max-w-full overflow-x-auto">
      <table className="w-full min-w-max border-collapse text-left text-sm">
        <thead>
          <tr>
            {columns.map((col) => (
              <th key={col.key} className="border-b border-border px-2.5 py-2 text-xs font-medium text-muted">
                {col.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {spec.rows.map((row, i) => (
            <tr key={i}>
              {columns.map((col) => (
                <td key={col.key} className="border-b border-border px-2.5 py-2 font-mono text-xs text-text">
                  {String(row[col.key] ?? "")}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
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

/** Renders "card" UiSpecs — a small key/value grid built from the first row, e.g. a single record's details. */
function CardRenderer({ spec }: { spec: UiSpecResponse }) {
  const row = spec.rows[0];
  if (!row) {
    return <p className="text-sm text-muted">No data to display.</p>;
  }

  const columns = resolveColumns(spec);

  return (
    <div className="grid grid-cols-1 gap-2 rounded-md border border-border p-3 sm:grid-cols-2">
      {columns.map((col) => (
        <div key={col.key} className="flex flex-col gap-0.5">
          <span className="text-xs text-muted">{col.label}</span>
          <span className="font-mono text-sm text-text">{String(row[col.key] ?? "—")}</span>
        </div>
      ))}
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
    <div className="flex flex-col items-start gap-1 rounded-md border border-border p-4">
      {primaryColumn && <span className="text-xs text-muted">{primaryColumn.label}</span>}
      <span className="text-3xl font-semibold tracking-tight text-teal">{value != null ? String(value) : "—"}</span>
    </div>
  );
}
