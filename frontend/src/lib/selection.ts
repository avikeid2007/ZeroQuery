import type { EntitySelectionDto, TableDto } from "./types";
import { qualifiedTableName } from "./types";

/** User's picker choices for a single column: whether it's exposed, plus an optional description. */
export interface ColumnSelection {
  selected: boolean;
  description: string;
}

/** Opt-in write permissions for an entity (Phase 8 full CRUD). Defaults to all false (read-only). */
export interface TableWriteActions {
  create: boolean;
  update: boolean;
  delete: boolean;
}

/** User's picker choices for a single table/view: whether it's exposed, its description, and its columns. */
export interface TableSelection {
  selected: boolean;
  description: string;
  columns: Record<string, ColumnSelection>;
  writeActions: TableWriteActions;
}

/** Full picker state, keyed by the table's schema-qualified name (see qualifiedTableName). */
export type PickerSelection = Record<string, TableSelection>;

/** Builds initial picker state from an introspection result — everything selected by default, descriptions empty, read-only. */
export function buildInitialSelection(tables: TableDto[]): PickerSelection {
  const selection: PickerSelection = {};
  for (const table of tables) {
    const key = table.schema ? `${table.schema}.${table.name}` : table.name;
    const columns: Record<string, ColumnSelection> = {};
    for (const column of table.columns) {
      columns[column.name] = { selected: true, description: "" };
    }
    selection[key] = {
      selected: true,
      description: "",
      columns,
      writeActions: { create: false, update: false, delete: false },
    };
  }
  return selection;
}

/** Count of tables currently marked selected. */
export function countSelectedTables(selection: PickerSelection): number {
  return Object.values(selection).filter((t) => t.selected).length;
}

/**
 * Converts picker state + the original introspection tables into the entity selection DTOs
 * expected by POST /api/config/generate. Only tables/columns marked selected are included;
 * primary key columns are always carried through (the backend re-includes them regardless,
 * but sending them explicitly keeps the payload self-describing).
 */
export function toEntitySelectionDtos(tables: TableDto[], selection: PickerSelection): EntitySelectionDto[] {
  const result: EntitySelectionDto[] = [];

  for (const table of tables) {
    const key = qualifiedTableName(table);
    const tableSelection = selection[key];
    if (!tableSelection?.selected) continue;

    const columns = table.columns
      .filter((c) => tableSelection.columns[c.name]?.selected || c.isPrimaryKey)
      .map((c) => ({
        name: c.name,
        include: tableSelection.columns[c.name]?.selected ?? false,
        isPrimaryKey: c.isPrimaryKey,
        description: tableSelection.columns[c.name]?.description?.trim() || null,
      }));

    const writeActions: string[] = [];
    if (tableSelection.writeActions?.create) writeActions.push("create");
    if (tableSelection.writeActions?.update) writeActions.push("update");
    if (tableSelection.writeActions?.delete) writeActions.push("delete");

    result.push({
      schema: table.schema,
      tableName: table.name,
      isView: table.isView,
      columns,
      entityName: null,
      description: tableSelection.description.trim() || null,
      writeActions,
    });
  }

  return result;
}
