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
      <div className="flex gap-2 border-b border-border p-3">
        <form onSubmit={handleSubmit} className="flex min-w-0 flex-1 gap-2">
          <input
            type="text"
            value={prompt}
            onChange={(e) => setPrompt(e.target.value)}
            disabled={isLoading}
            placeholder="e.g. Show me the 5 most expensive products"
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
      </div>

      <div className="flex flex-col gap-3 p-3 pt-0">
        {isLoading && (
          <div className="flex items-center gap-2 text-sm text-muted">
            <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-amber" />
            {progress ? describeProgress(progress) : "Starting…"}
          </div>
        )}
        {errorMessage && (
          <div className="rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
            {errorMessage}
          </div>
        )}

        {result && <UiSpecRenderer spec={result} />}
      </div>
    </div>
  );
}

function UiSpecRenderer({ spec }: { spec: UiSpecResponse }) {
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
 * Renders "chart" UiSpecs with Recharts. First column is treated as the category/label axis,
 * every other numeric column becomes its own series/slice — a deliberately simple, generic
 * mapping since the LLM only emits the fixed UiSpec shape (key/label columns + row data), not
 * chart-library-specific config (doc/Plan.md Section 5 — "no arbitrary rendering").
 */
function ChartRenderer({ spec }: { spec: UiSpecResponse }) {
  const columns = resolveColumns(spec);
  if (columns.length === 0) {
    return <p className="text-sm text-muted">Chart result has no columns to plot.</p>;
  }

  const [categoryColumn, ...seriesColumns] = columns;
  const data = spec.rows.map((row) => {
    const mapped: Record<string, string | number> = { [categoryColumn.key]: String(row[categoryColumn.key] ?? "") };
    for (const col of seriesColumns) {
      const raw = row[col.key];
      mapped[col.key] = typeof raw === "number" ? raw : Number(raw) || 0;
    }
    return mapped;
  });

  const chartType = spec.chartType ?? "Bar";

  if (chartType === "Pie") {
    const valueColumn = seriesColumns[0];
    if (!valueColumn) {
      return <p className="text-sm text-muted">Pie charts need at least one numeric column.</p>;
    }
    return (
      <div className="h-72 w-full">
        <ResponsiveContainer width="100%" height="100%">
          <PieChart>
            <Pie
              data={data}
              dataKey={valueColumn.key}
              nameKey={categoryColumn.key}
              cx="50%"
              cy="50%"
              outerRadius="80%"
              label={(props: PieLabelRenderProps) => String(props.name ?? "")}
            >
              {data.map((_, i) => (
                <Cell key={i} fill={CHART_COLORS[i % CHART_COLORS.length]} />
              ))}
            </Pie>
            <Tooltip contentStyle={{ background: "var(--surface)", border: "1px solid var(--border)", fontSize: 12 }} />
            <Legend wrapperStyle={{ fontSize: 12 }} />
          </PieChart>
        </ResponsiveContainer>
      </div>
    );
  }

  const ChartComponent = chartType === "Line" ? LineChart : BarChart;

  return (
    <div className="h-72 w-full">
      <ResponsiveContainer width="100%" height="100%">
        <ChartComponent data={data} margin={{ top: 8, right: 8, left: 0, bottom: 8 }}>
          <CartesianGrid stroke="var(--border)" strokeDasharray="3 3" />
          <XAxis dataKey={categoryColumn.key} stroke="var(--muted)" fontSize={12} tickLine={false} />
          <YAxis stroke="var(--muted)" fontSize={12} tickLine={false} />
          <Tooltip contentStyle={{ background: "var(--surface)", border: "1px solid var(--border)", fontSize: 12 }} />
          {seriesColumns.length > 1 && <Legend wrapperStyle={{ fontSize: 12 }} />}
          {seriesColumns.map((col, i) =>
            chartType === "Line" ? (
              <Line
                key={col.key}
                type="monotone"
                dataKey={col.key}
                name={col.label}
                stroke={CHART_COLORS[i % CHART_COLORS.length]}
                strokeWidth={2}
                dot={false}
              />
            ) : (
              <Bar key={col.key} dataKey={col.key} name={col.label} fill={CHART_COLORS[i % CHART_COLORS.length]} radius={[3, 3, 0, 0]} />
            )
          )}
        </ChartComponent>
      </ResponsiveContainer>
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
