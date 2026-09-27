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
  enableRest?: boolean;
  enableGraphQL?: boolean;
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
  restUrl?: string | null;
  graphqlUrl?: string | null;
  healthUrl?: string | null;
}

export interface DabStatusInfo {
  isInstalled: boolean;
  version: string | null;
  executablePath: string;
  error: string | null;
}

export interface DabInstallResult {
  success: boolean;
  message: string;
}

// Mirrors backend/Zeroquery.Api/Query/QueryDtos.cs

export interface QueryRequest {
  prompt: string;
}

export type UiSpecTypeName = "Table" | "Chart" | "Card" | "Stat" | "Form";
export type UiSpecChartTypeName = "Bar" | "Line" | "Pie";

export interface UiSpecColumnDto {
  key: string;
  label: string;
}

export interface UiSpecMetaDto {
  sourceEntity: string;
  generatedAt: string;
}

export interface UiSpecFormFieldDto {
  name: string;
  label: string;
  currentValue?: unknown;
  proposedValue?: unknown;
  isPrimaryKey?: boolean;
}

export interface UiSpecFormDto {
  operation: "create" | "update" | "delete";
  entity: string;
  primaryKey?: Record<string, unknown> | null;
  fields: UiSpecFormFieldDto[];
}

export interface UiSpecResponse {
  type: UiSpecTypeName;
  title: string;
  columns: UiSpecColumnDto[];
  rows: Record<string, unknown>[];
  chartType: UiSpecChartTypeName | null;
  meta: UiSpecMetaDto;
  form?: UiSpecFormDto | null;
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
  systemPrompt?: string | null;
  defaultSystemPrompt?: string;
}

export interface UpdateLlmSettingsRequest {
  apiKey: string | null;
  modelId: string | null;
  systemPrompt?: string | null;
}

// Mirrors backend/Zeroquery.Api/Connections/ConnectionDtos.cs (Phase 7)

export interface SavedConnectionSummary {
  id: string;
  name: string;
  provider: DatabaseProvider;
  createdAt: string;
  lastConnectedAt: string | null;
}

export interface SavedConnectionDetail {
  id: string;
  name: string;
  provider: DatabaseProvider;
  connectionStringEnvVarName: string;
  configJson: string;
  createdAt: string;
  lastConnectedAt: string | null;
}

export interface SaveConnectionRequest {
  name?: string;
  provider: DatabaseProvider;
  connectionString: string;
  configJson: string;
  connectionStringEnvVarName: string;
}

// Phase 8: Write access (Full CRUD) & Audit types

export interface ExecuteMutationRequest {
  entity: string;
  operation: "create" | "update" | "delete";
  primaryKey?: Record<string, unknown> | null;
  previousValues?: Record<string, unknown> | null;
  values: Record<string, unknown>;
}

export interface ExecuteMutationResponse {
  success: boolean;
  message: string;
  record?: Record<string, unknown> | null;
}

export interface WriteAuditEntry {
  id: string;
  timestamp: string;
  clientIp: string;
  instanceId: string;
  entity: string;
  operation: string;
  primaryKey?: Record<string, unknown> | null;
  previousValues?: Record<string, unknown> | null;
  newValues?: Record<string, unknown> | null;
  success: boolean;
  errorMessage?: string | null;
}
