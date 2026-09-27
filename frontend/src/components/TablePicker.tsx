"use client";

import { Fragment, useState } from "react";
import type { IntrospectResponse } from "@/lib/types";
import { qualifiedTableName } from "@/lib/types";
import {
  buildInitialSelection,
  countSelectedTables,
  type PickerSelection,
} from "@/lib/selection";

interface TablePickerProps {
  schema: IntrospectResponse;
  onBack: () => void;
  onConfirm: (selection: PickerSelection) => void;
}

export default function TablePicker({ schema, onBack, onConfirm }: TablePickerProps) {
  const [selection, setSelection] = useState<PickerSelection>(() => buildInitialSelection(schema.tables));
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});

  function toggleTable(key: string) {
    setSelection((prev) => ({
      ...prev,
      [key]: { ...prev[key], selected: !prev[key].selected },
    }));
  }

  function setTableDescription(key: string, description: string) {
    setSelection((prev) => ({
      ...prev,
      [key]: { ...prev[key], description },
    }));
  }

  function toggleColumn(tableKey: string, columnName: string) {
    setSelection((prev) => {
      const table = prev[tableKey];
      const column = table.columns[columnName];
      return {
        ...prev,
        [tableKey]: {
          ...table,
          columns: {
            ...table.columns,
            [columnName]: { ...column, selected: !column.selected },
          },
        },
      };
    });
  }

  function setColumnDescription(tableKey: string, columnName: string, description: string) {
    setSelection((prev) => {
      const table = prev[tableKey];
      const column = table.columns[columnName];
      return {
        ...prev,
        [tableKey]: {
          ...table,
          columns: {
            ...table.columns,
            [columnName]: { ...column, description },
          },
        },
      };
    });
  }

  function toggleWriteAction(key: string, action: "create" | "update" | "delete") {
    setSelection((prev) => {
      const table = prev[key];
      const current = table.writeActions ?? { create: false, update: false, delete: false };
      return {
        ...prev,
        [key]: {
          ...table,
          writeActions: {
            ...current,
            [action]: !current[action],
          },
        },
      };
    });
  }

  function toggleExpanded(key: string) {
    setExpanded((prev) => ({ ...prev, [key]: !prev[key] }));
  }

  const selectedCount = countSelectedTables(selection);

  return (
    <div className="flex w-full flex-col gap-4">
      <div>
        <h1 className="text-lg font-semibold tracking-tight text-text">Choose what to expose</h1>
        <p className="mt-1 text-sm text-muted">
          Pick the tables/views and columns Zeroquery can query. Descriptions you add here
          directly improve how well the AI picks the right table/column later — worth filling in.
          All tables default to <span className="font-medium text-text">read-only</span>; you can
          opt into <span className="font-medium text-text">Create, Update, or Delete</span> permissions per table.
        </p>
      </div>

      {schema.tables.length === 0 ? (
        <div className="flex flex-col items-center justify-center gap-3 rounded-lg border border-dashed border-border py-12 px-6 text-center">
          <div className="text-3xl">🔍</div>
          <h3 className="text-base font-semibold text-text">No tables or views found</h3>
          <p className="max-w-md text-xs text-muted">
            The database connected successfully, but no tables or views were visible.
            Ensure your database user has <code className="font-mono text-text">SELECT</code> permissions on <code className="font-mono text-text">INFORMATION_SCHEMA</code> or that the target database contains tables.
          </p>
        </div>
      ) : (
        <table className="w-full min-w-full border-collapse text-sm">
          <thead>
            <tr>
              <th className="w-8 border-b border-border px-2.5 py-2"></th>
              <th className="w-1/4 min-w-[13rem] border-b border-border px-2.5 py-2 text-left text-xs font-medium text-muted">table</th>
              <th className="w-56 min-w-[12rem] border-b border-border px-2.5 py-2 text-left text-xs font-medium text-muted">permissions</th>
              <th className="border-b border-border px-2.5 py-2 text-left text-xs font-medium text-muted">
                description agents will use
              </th>
            </tr>
          </thead>
          <tbody>
            {schema.tables.map((table) => {
            const key = qualifiedTableName(table);
            const tableSelection = selection[key];
            const isExpanded = !!expanded[key];
            const hasPrimaryKey = table.columns.some((c) => c.isPrimaryKey);
            const canMutate = !table.isView && hasPrimaryKey;

            return (
              <Fragment key={key}>
                <tr className="border-b border-border align-top">
                  <td className="px-2.5 py-2.5">
                    <input
                      type="checkbox"
                      checked={tableSelection.selected}
                      onChange={() => toggleTable(key)}
                      className="h-4 w-4 accent-teal"
                    />
                  </td>
                  <td className="px-2.5 py-2.5">
                    <button
                      type="button"
                      onClick={() => toggleExpanded(key)}
                      className="flex items-center gap-2 text-left"
                    >
                      <span className="font-mono text-text">{key}</span>
                      {table.isView && (
                        <span className="rounded border border-border bg-surface2 px-1.5 py-0.5 text-xs text-muted">
                          view
                        </span>
                      )}
                    </button>
                    <div className="mt-0.5 text-xs text-muted">
                      {table.columns.length} column{table.columns.length === 1 ? "" : "s"} ·{" "}
                      <button type="button" onClick={() => toggleExpanded(key)} className="underline">
                        {isExpanded ? "hide columns" : "show columns"}
                      </button>
                    </div>
                  </td>
                  <td className="px-2.5 py-2.5">
                    {tableSelection.selected ? (
                      canMutate ? (
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="rounded bg-teal/10 px-1.5 py-0.5 text-xs font-medium text-teal" title="Read is always granted">
                            Read
                          </span>
                          <label className="flex items-center gap-1 text-xs text-text cursor-pointer">
                            <input
                              type="checkbox"
                              checked={tableSelection.writeActions?.create ?? false}
                              onChange={() => toggleWriteAction(key, "create")}
                              className="h-3.5 w-3.5 accent-teal"
                            />
                            Create
                          </label>
                          <label className="flex items-center gap-1 text-xs text-text cursor-pointer">
                            <input
                              type="checkbox"
                              checked={tableSelection.writeActions?.update ?? false}
                              onChange={() => toggleWriteAction(key, "update")}
                              className="h-3.5 w-3.5 accent-teal"
                            />
                            Update
                          </label>
                          <label className="flex items-center gap-1 text-xs text-text cursor-pointer">
                            <input
                              type="checkbox"
                              checked={tableSelection.writeActions?.delete ?? false}
                              onChange={() => toggleWriteAction(key, "delete")}
                              className="h-3.5 w-3.5 accent-teal"
                            />
                            Delete
                          </label>
                        </div>
                      ) : (
                        <span className="text-xs text-muted" title={table.isView ? "Views cannot be modified" : "Primary key required for mutation"}>
                          Read-only {table.isView ? "(view)" : "(no PK)"}
                        </span>
                      )
                    ) : (
                      <span className="text-xs text-muted">—</span>
                    )}
                  </td>
                  <td className="px-2.5 py-2.5">
                    {tableSelection.selected ? (
                      <input
                        type="text"
                        value={tableSelection.description}
                        onChange={(e) => setTableDescription(key, e.target.value)}
                        placeholder="Describe what this table represents (recommended)…"
                        className="w-full rounded-md border border-border bg-bg px-2.5 py-1.5 text-sm text-text focus:border-teal focus:outline-none"
                      />
                    ) : (
                      <span className="text-xs text-muted">excluded</span>
                    )}
                  </td>
                </tr>
                {isExpanded && (
                  <tr className="border-b border-border">
                    <td></td>
                    <td colSpan={3} className="px-2.5 py-2">
                      <div className="flex flex-col gap-1.5 border-l border-border pl-3">
                        {table.columns.map((column) => {
                          const columnSelection = tableSelection.columns[column.name];
                          return (
                            <div key={column.name} className="flex flex-wrap items-center gap-2">
                              <input
                                type="checkbox"
                                checked={columnSelection.selected}
                                onChange={() => toggleColumn(key, column.name)}
                                className="h-3.5 w-3.5 accent-teal"
                              />
                              <span className="font-mono text-xs text-text">{column.name}</span>
                              <span className="text-xs text-muted">
                                {column.dataType}
                                {column.isPrimaryKey ? " · PK" : ""}
                                {column.isNullable ? "" : " · required"}
                              </span>
                              {columnSelection.selected && (
                                <input
                                  type="text"
                                  value={columnSelection.description}
                                  onChange={(e) => setColumnDescription(key, column.name, e.target.value)}
                                  placeholder="Description (optional)…"
                                  className="ml-auto min-w-[10rem] flex-1 rounded border border-border bg-bg px-2 py-1 text-xs text-text focus:border-teal focus:outline-none"
                                />
                              )}
                            </div>
                          );
                        })}
                      </div>
                    </td>
                  </tr>
                )}
              </Fragment>
            );
          })}
        </tbody>
      </table>
      )}

      {schema.foreignKeys.length > 0 && (
        <details className="rounded-md border border-border p-3 text-sm">
          <summary className="cursor-pointer font-medium text-text">
            {schema.foreignKeys.length} relationship{schema.foreignKeys.length === 1 ? "" : "s"} detected
          </summary>
          <ul className="mt-2 flex flex-col gap-1 font-mono text-xs text-muted">
            {schema.foreignKeys.map((fk) => (
              <li key={fk.constraintName}>
                {fk.fromTable}.{fk.fromColumn} → {fk.toTable}.{fk.toColumn}
              </li>
            ))}
          </ul>
        </details>
      )}

      <div className="flex items-center justify-between border-t border-border pt-4">
        <button
          type="button"
          onClick={onBack}
          className="rounded-md border border-border px-4 py-2 text-sm font-medium text-text hover:bg-surface2"
        >
          Back
        </button>
        <button
          type="button"
          onClick={() => onConfirm(selection)}
          disabled={selectedCount === 0}
          className="inline-flex items-center justify-center rounded-md bg-teal px-4 py-2 text-sm font-medium text-bg disabled:cursor-not-allowed disabled:opacity-50"
        >
          Continue with {selectedCount} table{selectedCount === 1 ? "" : "s"}
        </button>
      </div>
    </div>
  );
}
