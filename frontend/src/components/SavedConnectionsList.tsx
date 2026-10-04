"use client";

import { useEffect, useState } from "react";
import type { SavedConnectionSummary } from "@/lib/types";
import { getSavedConnections, deleteSavedConnection, ApiError } from "@/lib/api";

interface SavedConnectionsListProps {
  onSelectReconnect: (connection: SavedConnectionSummary) => void;
  isReconnectingId: string | null;
}

export default function SavedConnectionsList({ onSelectReconnect, isReconnectingId }: SavedConnectionsListProps) {
  const [connections, setConnections] = useState<SavedConnectionSummary[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [confirmingConnection, setConfirmingConnection] = useState<SavedConnectionSummary | null>(null);
  const [deletingId, setDeletingId] = useState<string | null>(null);

  useEffect(() => {
    loadConnections();
  }, []);

  async function loadConnections() {
    setIsLoading(true);
    setError(null);
    try {
      const list = await getSavedConnections();
      setConnections(list);
    } catch {
      // If backend is not available or endpoint error, show gracefully
      setConnections([]);
    } finally {
      setIsLoading(false);
    }
  }

  async function handleForget(id: string) {
    if (!confirm("Are you sure you want to forget this saved connection?")) return;
    setDeletingId(id);
    try {
      await deleteSavedConnection(id);
      setConnections((prev) => prev.filter((c) => c.id !== id));
      if (confirmingConnection?.id === id) {
        setConfirmingConnection(null);
      }
    } catch (err) {
      alert(err instanceof ApiError ? err.message : "Failed to forget connection.");
    } finally {
      setDeletingId(null);
    }
  }

  if (isLoading) {
    return (
      <div className="py-2 text-xs text-muted">
        Loading saved connections…
      </div>
    );
  }

  if (connections.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-col gap-3 rounded-lg border border-border bg-surface2/50 p-4">
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-semibold tracking-tight text-text">Saved connections</h2>
        <span className="text-xs text-muted">{connections.length} saved</span>
      </div>

      <div className="flex flex-col divide-y divide-border/60">
        {connections.map((c) => (
          <div key={c.id} className="flex flex-wrap items-center justify-between gap-3 py-3 first:pt-1 last:pb-1">
            <div className="flex flex-col gap-0.5">
              <div className="flex items-center gap-2">
                <span className="text-sm font-medium text-text">{c.name}</span>
                <span className="rounded bg-surface px-1.5 py-0.5 text-[10px] font-mono text-muted">
                  {c.provider}
                </span>
              </div>
              <span className="text-xs text-muted">
                {c.lastConnectedAt
                  ? `Last connected: ${new Date(c.lastConnectedAt).toLocaleString()}`
                  : `Saved on: ${new Date(c.createdAt).toLocaleDateString()}`}
              </span>
            </div>

            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={() => setConfirmingConnection(c)}
                disabled={isReconnectingId === c.id || deletingId === c.id}
                className="btn-primary btn-sm"
              >
                {isReconnectingId === c.id ? "Connecting…" : "Reconnect"}
              </button>
              <button
                type="button"
                onClick={() => handleForget(c.id)}
                disabled={isReconnectingId === c.id || deletingId === c.id}
                className="btn-danger btn-sm"
              >
                {deletingId === c.id ? "…" : "Forget"}
              </button>
            </div>
          </div>
        ))}
      </div>

      {/* Explicit Reconnect Confirmation Dialog (doc/Plan.md Section 4 & Phase 7) */}
      {confirmingConnection && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4">
          <div className="flex w-full max-w-md flex-col gap-4 rounded-lg border border-[var(--win-border)] bg-[var(--win-card)] p-5 shadow-xl">
            <div>
              <h3 className="text-base font-semibold text-text">Reconnect to database?</h3>
              <p className="mt-1.5 text-xs leading-relaxed text-[var(--win-text-muted)]">
                You are about to launch a Data API builder (DAB) SQL MCP subprocess for{" "}
                <span className="font-semibold text-text">{confirmingConnection.name}</span> (
                {confirmingConnection.provider}).
              </p>
            </div>

            <div className="rounded-md border border-[var(--win-border)] bg-[var(--win-bg)] p-3 text-xs text-[var(--win-text-muted)]">
              <p>• Starts a new DAB process on an allocated port</p>
              <p>• Decrypts saved connection string and verifies SSRF policies</p>
              <p>• Zeroquery never reconnects automatically without this confirmation</p>
            </div>

            <div className="flex justify-end gap-2.5 pt-1">
              <button
                type="button"
                onClick={() => setConfirmingConnection(null)}
                className="btn-secondary btn-sm"
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={() => {
                  const target = confirmingConnection;
                  setConfirmingConnection(null);
                  onSelectReconnect(target);
                }}
                className="btn-primary btn-sm"
              >
                Confirm &amp; Reconnect
              </button>
            </div>
          </div>
        </div>
      )}

      {error && (
        <div className="rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-xs text-danger">
          {error}
        </div>
      )}
    </div>
  );
}
