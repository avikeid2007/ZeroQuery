"use client";

import { useState } from "react";
import type { DatabaseProvider } from "@/lib/types";

const PROVIDERS: { value: DatabaseProvider; label: string }[] = [
  { value: "SqlServer", label: "SQL Server" },
  { value: "PostgreSql", label: "PostgreSQL" },
  { value: "MySql", label: "MySQL" },
];

interface ConnectionFormProps {
  onSubmit: (provider: DatabaseProvider, connectionString: string) => void;
  isLoading: boolean;
  errorMessage: string | null;
}

export default function ConnectionForm({ onSubmit, isLoading, errorMessage }: ConnectionFormProps) {
  const [provider, setProvider] = useState<DatabaseProvider>("SqlServer");
  const [connectionString, setConnectionString] = useState("");

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!connectionString.trim()) return;
    onSubmit(provider, connectionString.trim());
  }

  return (
    <form onSubmit={handleSubmit} className="flex w-full flex-col gap-4">
      <div>
        <h1 className="text-lg font-semibold tracking-tight text-text">Connect a database</h1>
        <p className="mt-1 text-sm text-muted">
          Paste a connection string to read its schema. Nothing is saved or queried until you
          confirm which tables to expose in the next step.
        </p>
      </div>

      <label className="flex flex-col gap-1.5">
        <span className="text-xs text-muted">Database type</span>
        <select
          value={provider}
          onChange={(e) => setProvider(e.target.value as DatabaseProvider)}
          disabled={isLoading}
          className="rounded-md border border-border bg-bg px-3 py-2.5 text-sm text-text focus:border-teal focus:outline-none disabled:opacity-50"
        >
          {PROVIDERS.map((p) => (
            <option key={p.value} value={p.value}>
              {p.label}
            </option>
          ))}
        </select>
      </label>

      <label className="flex flex-col gap-1.5">
        <span className="text-xs text-muted">Connection string</span>
        <textarea
          value={connectionString}
          onChange={(e) => setConnectionString(e.target.value)}
          disabled={isLoading}
          rows={3}
          placeholder="Server=localhost;Database=mydb;User Id=sa;Password=...;"
          spellCheck={false}
          className="resize-none rounded-md border border-border bg-bg px-3 py-2.5 font-mono text-sm text-text focus:border-teal focus:outline-none disabled:opacity-50"
        />
        <span className="text-xs leading-relaxed text-muted">
          Used once to read table/column metadata, then discarded unless you choose to save it later.
        </span>
      </label>

      {errorMessage && (
        <div className="rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
          {errorMessage}
        </div>
      )}

      <div className="mt-1 flex justify-end">
        <button
          type="submit"
          disabled={isLoading || !connectionString.trim()}
          className="inline-flex items-center justify-center rounded-md bg-teal px-4 py-2 text-sm font-medium text-bg transition-opacity disabled:cursor-not-allowed disabled:opacity-50"
        >
          {isLoading ? "Reading schema…" : "Read schema"}
        </button>
      </div>
    </form>
  );
}
