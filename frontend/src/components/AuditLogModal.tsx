"use client";

import { useEffect, useState } from "react";
import type { WriteAuditEntry } from "@/lib/types";
import { getAuditLogs } from "@/lib/api";

interface AuditLogModalProps {
  isOpen: boolean;
  onClose: () => void;
}

export default function AuditLogModal({ isOpen, onClose }: AuditLogModalProps) {
  const [entries, setEntries] = useState<WriteAuditEntry[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    if (isOpen) {
      loadLogs();
    }
  }, [isOpen]);

  async function loadLogs() {
    setIsLoading(true);
    setErrorMessage(null);
    try {
      const logs = await getAuditLogs(50);
      setEntries(logs);
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Failed to load audit logs.");
    } finally {
      setIsLoading(false);
    }
  }

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-xs">
      <div className="flex max-h-[85vh] w-full max-w-3xl flex-col rounded-xl border border-border bg-surface shadow-2xl">
        {/* Header */}
        <div className="flex items-center justify-between border-b border-border px-5 py-4">
          <div>
            <h2 className="text-base font-semibold text-text">Database Write Audit Trail</h2>
            <p className="text-xs text-muted">
              Tamper-evident record of all create, update, and delete actions executed via Zeroquery.
            </p>
          </div>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={loadLogs}
              disabled={isLoading}
              className="rounded-md border border-border px-2.5 py-1 text-xs text-text hover:bg-surface2"
            >
              {isLoading ? "Refreshing…" : "Refresh"}
            </button>
            <button
              type="button"
              onClick={onClose}
              className="rounded-md border border-border px-2.5 py-1 text-xs text-muted hover:text-text hover:bg-surface2"
            >
              Close
            </button>
          </div>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto p-5">
          {isLoading && entries.length === 0 ? (
            <p className="py-8 text-center text-xs text-muted">Loading audit entries…</p>
          ) : errorMessage ? (
            <div className="rounded-md border border-danger/40 bg-danger/10 p-3 text-xs text-danger">
              {errorMessage}
            </div>
          ) : entries.length === 0 ? (
            <p className="py-8 text-center text-xs text-muted">No write actions recorded yet.</p>
          ) : (
            <div className="flex flex-col gap-3">
              {entries.map((entry) => (
                <div
                  key={entry.id}
                  className="flex flex-col gap-2 rounded-lg border border-border bg-bg p-3.5 text-xs shadow-xs"
                >
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="flex items-center gap-2">
                      <span
                        className={`rounded px-1.5 py-0.5 text-[10px] font-bold uppercase tracking-wider ${
                          entry.success
                            ? "bg-teal/10 text-teal border border-teal/30"
                            : "bg-danger/10 text-danger border border-danger/30"
                        }`}
                      >
                        {entry.success ? "Success" : "Failed"}
                      </span>
                      <span className="font-semibold text-text uppercase tracking-wide">
                        {entry.operation}
                      </span>
                      <span className="font-mono text-muted">table: {entry.entity}</span>
                    </div>
                    <span className="text-[11px] text-muted">
                      {new Date(entry.timestamp).toLocaleString()}
                    </span>
                  </div>

                  <div className="flex flex-wrap items-center gap-4 text-[11px] text-muted">
                    <span>IP: <code className="text-text">{entry.clientIp}</code></span>
                    {entry.primaryKey && (
                      <span>PK: <code className="text-text">{JSON.stringify(entry.primaryKey)}</code></span>
                    )}
                  </div>

                  {entry.errorMessage && (
                    <div className="rounded border border-danger/30 bg-danger/5 px-2 py-1 text-[11px] text-danger">
                      Error: {entry.errorMessage}
                    </div>
                  )}

                  {entry.newValues && Object.keys(entry.newValues).length > 0 && (
                    <details className="mt-1">
                      <summary className="cursor-pointer text-[11px] text-muted hover:text-text">
                        View values
                      </summary>
                      <pre className="mt-1 max-h-24 overflow-auto rounded border border-border/50 bg-surface p-2 font-mono text-[10px] text-text">
                        {JSON.stringify(entry.newValues, null, 2)}
                      </pre>
                    </details>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
