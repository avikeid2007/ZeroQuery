"use client";

import { useState } from "react";
import type { UiSpecResponse, ExecuteMutationResponse, UiSpecFormFieldDto } from "@/lib/types";
import { executeMutation } from "@/lib/api";

interface FormViewProps {
  spec: UiSpecResponse;
  instanceId: string;
  onExecuted?: (result: ExecuteMutationResponse) => void;
  onCancel?: () => void;
}

export default function FormView({ spec, instanceId, onExecuted, onCancel }: FormViewProps) {
  const form = spec.form;
  const operation = (form?.operation || "update").toLowerCase();
  const entity = form?.entity || spec.meta.sourceEntity || "Record";

  // Editable field values initialized to proposedValue
  const [fieldValues, setFieldValues] = useState<Record<string, unknown>>(() => {
    const initial: Record<string, unknown> = {};
    if (form?.fields) {
      for (const field of form.fields) {
        initial[field.name] = field.proposedValue ?? "";
      }
    }
    return initial;
  });

  const [confirmed, setConfirmed] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [executionResult, setExecutionResult] = useState<ExecuteMutationResponse | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  if (!form) {
    return (
      <div className="rounded-lg border border-border bg-surface p-4 text-sm text-muted">
        No form details available for this mutation proposal.
      </div>
    );
  }

  function handleFieldChange(name: string, value: string) {
    setFieldValues((prev) => ({ ...prev, [name]: value }));
  }

  async function handleExecute() {
    if (!confirmed || isSubmitting) return;

    setIsSubmitting(true);
    setErrorMessage(null);

    // Compute previous values for audit trail
    const previousValues: Record<string, unknown> = {};
    if (form?.fields) {
      for (const f of form.fields) {
        if (f.currentValue !== undefined && f.currentValue !== null) {
          previousValues[f.name] = f.currentValue;
        }
      }
    }

    try {
      const result = await executeMutation(instanceId, {
        entity,
        operation: operation as "create" | "update" | "delete",
        primaryKey: form?.primaryKey ?? null,
        previousValues: Object.keys(previousValues).length > 0 ? previousValues : null,
        values: fieldValues,
      });

      setExecutionResult(result);
      if (onExecuted) {
        onExecuted(result);
      }
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Mutation execution failed.");
    } finally {
      setIsSubmitting(false);
    }
  }

  const isDelete = operation === "delete";
  const isCreate = operation === "create";

  return (
    <div className="flex flex-col gap-4 rounded-xl border border-border bg-surface p-6 shadow-sm">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-border pb-4">
        <div>
          <div className="flex items-center gap-2">
            <span
              className={`rounded-full px-2.5 py-0.5 text-xs font-semibold uppercase tracking-wider ${
                isDelete
                  ? "bg-danger/10 text-danger border border-danger/30"
                  : isCreate
                  ? "bg-teal/10 text-teal border border-teal/30"
                  : "bg-amber-500/10 text-amber-400 border border-amber-500/30"
              }`}
            >
              {operation}
            </span>
            <span className="font-mono text-xs text-muted">table: {entity}</span>
          </div>
          <h2 className="mt-1 text-base font-semibold text-text">{spec.title}</h2>
        </div>

        {form.primaryKey && (
          <div className="flex items-center gap-1.5 rounded-md border border-border bg-bg px-2.5 py-1 text-xs">
            <span className="text-muted">Target PK:</span>
            <span className="font-mono text-text">
              {JSON.stringify(form.primaryKey)}
            </span>
          </div>
        )}
      </div>

      {/* Safety Notice */}
      <div
        className={`rounded-lg p-3 text-xs flex items-start gap-2.5 ${
          isDelete
            ? "border border-danger/40 bg-danger/10 text-danger"
            : "border border-amber-500/40 bg-amber-500/10 text-amber-200"
        }`}
      >
        <span className="font-bold text-sm leading-none mt-0.5">⚠</span>
        <div>
          <p className="font-medium">
            {isDelete
              ? "Irreversible Action: This will permanently delete data from the database."
              : "Review Proposed Write: Zeroquery never executes mutations automatically without human confirmation."}
          </p>
          <p className="mt-0.5 opacity-90">
            Inspect the proposed values below. You may tweak fields before confirming.
          </p>
        </div>
      </div>

      {/* Fields Diff / Form */}
      {isDelete ? (
        <div className="rounded-lg border border-border bg-bg p-4">
          <p className="text-xs text-muted mb-2">Record to be removed:</p>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 text-xs">
            {form.fields.map((f) => (
              <div key={f.name} className="flex flex-col rounded border border-border/50 bg-surface px-2.5 py-1.5">
                <span className="text-muted font-mono">{f.label || f.name}</span>
                <span className="font-semibold text-text truncate">
                  {f.currentValue !== undefined && f.currentValue !== null ? String(f.currentValue) : "—"}
                </span>
              </div>
            ))}
          </div>
        </div>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full border-collapse text-left text-xs">
            <thead>
              <tr className="border-b border-border text-muted">
                <th className="py-2 px-3 font-medium">Field</th>
                {!isCreate && <th className="py-2 px-3 font-medium">Current Value</th>}
                <th className="py-2 px-3 font-medium">Proposed Value (Editable)</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {form.fields.map((field) => {
                const currentValStr =
                  field.currentValue !== undefined && field.currentValue !== null
                    ? String(field.currentValue)
                    : "—";
                const isDifferent =
                  !isCreate &&
                  field.currentValue !== undefined &&
                  String(field.currentValue) !== String(fieldValues[field.name] ?? "");

                return (
                  <tr key={field.name} className={isDifferent ? "bg-amber-500/5" : ""}>
                    <td className="py-2.5 px-3">
                      <span className="font-mono font-medium text-text">{field.name}</span>
                      {field.isPrimaryKey && (
                        <span className="ml-1.5 rounded bg-border px-1 py-0.5 text-[10px] text-muted">
                          PK
                        </span>
                      )}
                    </td>
                    {!isCreate && (
                      <td className="py-2.5 px-3 font-mono text-muted">
                        {currentValStr}
                      </td>
                    )}
                    <td className="py-2.5 px-3">
                      <input
                        type="text"
                        disabled={field.isPrimaryKey || isSubmitting || !!executionResult}
                        value={String(fieldValues[field.name] ?? "")}
                        onChange={(e) => handleFieldChange(field.name, e.target.value)}
                        className={`w-full max-w-sm rounded border px-2.5 py-1 font-mono text-xs text-text transition-colors focus:outline-none ${
                          field.isPrimaryKey
                            ? "border-transparent bg-transparent text-muted cursor-not-allowed"
                            : isDifferent
                            ? "border-amber-500/60 bg-amber-500/10 focus:border-amber-400"
                            : "border-border bg-bg focus:border-teal"
                        }`}
                      />
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {/* Error state */}
      {errorMessage && (
        <div className="rounded-lg border border-danger/40 bg-danger/10 p-3 text-xs text-danger">
          <p className="font-semibold">Execution Failed</p>
          <p className="mt-0.5">{errorMessage}</p>
        </div>
      )}

      {/* Success state */}
      {executionResult?.success && (
        <div className="rounded-lg border border-teal/40 bg-teal/10 p-3 text-xs text-teal">
          <p className="font-semibold">✓ Mutation Executed Successfully</p>
          <p className="mt-0.5">{executionResult.message}</p>
          {executionResult.record && (
            <pre className="mt-2 max-h-32 overflow-auto rounded border border-teal/30 bg-bg p-2 font-mono text-[11px] text-text">
              {JSON.stringify(executionResult.record, null, 2)}
            </pre>
          )}
        </div>
      )}

      {/* Confirmation & Actions */}
      {!executionResult?.success && (
        <div className="flex flex-wrap items-center justify-between gap-4 border-t border-border pt-4">
          <label className="flex items-center gap-2 text-xs text-text cursor-pointer select-none">
            <input
              type="checkbox"
              checked={confirmed}
              disabled={isSubmitting}
              onChange={(e) => setConfirmed(e.target.checked)}
              className="h-4 w-4 rounded accent-teal cursor-pointer"
            />
            <span>
              I understand and confirm this <strong className="uppercase">{operation}</strong> action on{" "}
              <strong>{entity}</strong>.
            </span>
          </label>

          <div className="flex items-center gap-2.5">
            {onCancel && (
              <button
                type="button"
                onClick={onCancel}
                disabled={isSubmitting}
                className="rounded-md border border-border px-3.5 py-1.5 text-xs font-medium text-text hover:bg-surface2 transition-colors"
              >
                Cancel
              </button>
            )}

            <button
              type="button"
              onClick={handleExecute}
              disabled={!confirmed || isSubmitting}
              className={`rounded-md px-4 py-1.5 text-xs font-semibold text-bg transition-all disabled:opacity-40 disabled:cursor-not-allowed ${
                isDelete
                  ? "bg-danger hover:bg-danger/90 text-white"
                  : "bg-teal hover:bg-teal/90 text-bg"
              }`}
            >
              {isSubmitting
                ? "Executing…"
                : isDelete
                ? "Confirm & Delete Record"
                : isCreate
                ? "Confirm & Insert Record"
                : "Confirm & Update Record"}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
