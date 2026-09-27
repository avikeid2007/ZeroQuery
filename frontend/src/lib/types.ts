// Mirrors the DTOs in backend/Zeroquery.Api/Introspection/IntrospectDtos.cs
// Keep in sync manually until/unless we add a shared schema generator.

export type DatabaseProvider = "SqlServer" | "PostgreSql" | "MySql";

export interface ColumnDto {
  name: string;
  dataType: string;
  isNullable: boolean;
  isPrimaryKey: boolean;
  maxLength: number | null;
}

export interface TableDto {
  schema: string;
  name: string;
  isView: boolean;
  columns: ColumnDto[];
}

export interface ForeignKeyDto {
  constraintName: string;
  fromTable: string;
  fromColumn: string;
  toTable: string;
  toColumn: string;
}

export interface IntrospectResponse {
  provider: DatabaseProvider;
  tables: TableDto[];
  foreignKeys: ForeignKeyDto[];
}

export interface ErrorResponse {
  message: string;
}

export interface IntrospectRequest {
  provider: DatabaseProvider;
  connectionString: string;
}

/** Helper: schema-qualified table identifier, matching TableInfo.QualifiedName on the backend. */
export function qualifiedTableName(table: Pick<TableDto, "schema" | "name">): string {
  return table.schema ? `${table.schema}.${table.name}` : table.name;
}

// Mirrors backend/Zeroquery.Api/ConfigGeneration/GenerateConfigDtos.cs

export interface ColumnSelectionDto {
  name: string;
  include: boolean;
  isPrimaryKey: boolean;
  description: string | null;
}

export interface EntitySelectionDto {
  schema: string;
  tableName: string;
  isView: boolean;
  columns: ColumnSelectionDto[];
  entityName: string | null;
  description: string | null;
  writeActions: string[] | null;
}

export interface GenerateConfigRequest {
  provider: DatabaseProvider;
  connectionStringEnvVarName: string;
  entities: EntitySelectionDto[];
}

export interface GenerateConfigResponse {
  configJson: string;
  isValid: boolean;
  validationErrors: string[];
}

// Mirrors backend/Zeroquery.Api/Instances/InstanceDtos.cs

export type DabInstanceStatusName =
  | "Provisioning"
  | "Starting"
  | "Running"
  | "Idle"
  | "Stopped"
  | "Error";

export interface StartInstanceRequest {
  configJson: string;
  connectionStringEnvVarName: string;
  connectionString: string;
}

export interface InstanceStatusResponse {
  id: string;
  status: DabInstanceStatusName;
  port: number;
  baseUrl: string;
  startedAt: string;
  lastUsedAt: string;
  lastError: string | null;
}

// Mirrors backend/Zeroquery.Api/Query/QueryDtos.cs

export interface QueryRequest {
  prompt: string;
}

export type UiSpecTypeName = "Table" | "Chart" | "Card" | "Stat";
export type UiSpecChartTypeName = "Bar" | "Line" | "Pie";

export interface UiSpecColumnDto {
  key: string;
  label: string;
}

export interface UiSpecMetaDto {
  sourceEntity: string;
  generatedAt: string;
}

export interface UiSpecResponse {
  type: UiSpecTypeName;
  title: string;
  columns: UiSpecColumnDto[];
  rows: Record<string, unknown>[];
  chartType: UiSpecChartTypeName | null;
  meta: UiSpecMetaDto;
}

/**
 * SSE "progress" event payload from POST /api/instances/{id}/query/stream (doc/Plan.md
 * Phase 5). Stage names mirror Zeroquery.Core.Orchestration.OrchestrationStage.
 */
export type OrchestrationStageName = "Thinking" | "ToolCall" | "ToolResult" | "Rendering";

export interface OrchestrationProgressDto {
  stage: OrchestrationStageName;
  toolName: string | null;
}

// Mirrors backend/Zeroquery.Api/Settings/SettingsDtos.cs

export interface LlmSettingsResponse {
  modelId: string;
  isApiKeyConfigured: boolean;
  apiKeyMasked: string | null;
}

export interface UpdateLlmSettingsRequest {
  apiKey: string | null;
  modelId: string | null;
}
