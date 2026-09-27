import type {
  IntrospectRequest,
  IntrospectResponse,
  ErrorResponse,
  GenerateConfigRequest,
  GenerateConfigResponse,
  StartInstanceRequest,
  InstanceStatusResponse,
  QueryRequest,
  UiSpecResponse,
  OrchestrationProgressDto,
  LlmSettingsResponse,
  UpdateLlmSettingsRequest,
  SavedConnectionSummary,
  SavedConnectionDetail,
  SaveConnectionRequest,
  ExecuteMutationRequest,
  ExecuteMutationResponse,
  WriteAuditEntry,
  DabStatusInfo,
  DabInstallResult,
} from "./types";

const API_BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5000";

export class ApiError extends Error {}

async function parseErrorMessage(response: Response, fallback: string): Promise<string> {
  try {
    const body = (await response.json()) as ErrorResponse;
    return body?.message || fallback;
  } catch {
    return fallback;
  }
}

/**
 * Calls POST /api/introspect on the Zeroquery.Api backend.
 * The connection string is sent once, over the wire to our own backend, and never stored
 * client-side beyond the current form state — see doc/Plan.md Section 3 (Security).
 */
export async function introspectDatabase(request: IntrospectRequest): Promise<IntrospectResponse> {
  const response = await fetch(`${API_BASE_URL}/api/introspect`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new ApiError(await parseErrorMessage(response, `Introspection failed with status ${response.status}.`));
  }

  return (await response.json()) as IntrospectResponse;
}

/**
 * Calls POST /api/config/generate on the Zeroquery.Api backend to turn a confirmed
 * table/column picker selection into a dab-config.json, validated against DAB's schema.
 */
export async function generateDabConfig(request: GenerateConfigRequest): Promise<GenerateConfigResponse> {
  const response = await fetch(`${API_BASE_URL}/api/config/generate`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new ApiError(await parseErrorMessage(response, `Config generation failed with status ${response.status}.`));
  }

  return (await response.json()) as GenerateConfigResponse;
}

/**
 * Calls POST /api/instances to write the generated config to disk (server-side) and start a
 * DAB subprocess for it. The connection string is sent once and set only as an environment
 * variable on the child process — never written into the config file itself.
 */
export async function startDabInstance(request: StartInstanceRequest): Promise<InstanceStatusResponse> {
  const response = await fetch(`${API_BASE_URL}/api/instances`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new ApiError(await parseErrorMessage(response, `Starting the instance failed with status ${response.status}.`));
  }

  return (await response.json()) as InstanceStatusResponse;
}

/** Calls GET /api/instances/{id}/status to poll a running instance's lifecycle state. */
export async function getDabInstanceStatus(id: string): Promise<InstanceStatusResponse> {
  const response = await fetch(`${API_BASE_URL}/api/instances/${id}/status`);

  if (!response.ok) {
    throw new ApiError(await parseErrorMessage(response, `Fetching instance status failed with status ${response.status}.`));
  }

  return (await response.json()) as InstanceStatusResponse;
}

/** Calls DELETE /api/instances/{id} to stop and remove a running instance (manual disconnect). */
export async function stopDabInstance(id: string): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/api/instances/${id}`, { method: "DELETE" });

  if (!response.ok && response.status !== 404) {
    throw new ApiError(await parseErrorMessage(response, `Stopping the instance failed with status ${response.status}.`));
  }
}

/** Calls GET /api/instances/dab-status to check if the DAB CLI is installed. */
export async function getDabStatus(): Promise<DabStatusInfo> {
  const response = await fetch(`${API_BASE_URL}/api/instances/dab-status`);
  if (!response.ok) {
    throw new ApiError(await parseErrorMessage(response, `Failed to check DAB status: ${response.status}`));
  }
  return (await response.json()) as DabStatusInfo;
}

/** Calls POST /api/instances/install-dab to install Microsoft.DataApiBuilder globally. */
export async function installDab(): Promise<DabInstallResult> {
  const response = await fetch(`${API_BASE_URL}/api/instances/install-dab`, {
    method: "POST",
  });
  if (!response.ok) {
    const message = await parseErrorMessage(response, `Installation failed with status ${response.status}`);
    throw new ApiError(message);
  }
  return (await response.json()) as DabInstallResult;
}

/**
 * Calls POST /api/instances/{id}/query to run a natural-language prompt against a running
 * DAB instance's MCP tools and get back a UI Spec to render.
 */
export async function queryDabInstance(id: string, request: QueryRequest): Promise<UiSpecResponse> {
  const response = await fetch(`${API_BASE_URL}/api/instances/${id}/query`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new ApiError(await parseErrorMessage(response, `Query failed with status ${response.status}.`));
  }

  return (await response.json()) as UiSpecResponse;
}

/**
 * Calls POST /api/instances/{id}/query/stream — the SSE sibling of {@link queryDabInstance}
 * (doc/Plan.md Phase 5). Streams `progress` events via `onProgress` while the tool-calling
 * loop runs, then resolves with the final UI Spec from the terminal `result` event (or
 * rejects with an {@link ApiError} on a terminal `error` event / non-2xx response).
 *
 * `EventSource` can't send a POST body, so this parses the `text/event-stream` response
 * manually off a `fetch` body reader instead of using the browser's native SSE client.
 */
export async function queryDabInstanceStream(
  id: string,
  request: QueryRequest,
  onProgress: (progress: OrchestrationProgressDto) => void,
  signal?: AbortSignal,
): Promise<UiSpecResponse> {
  const response = await fetch(`${API_BASE_URL}/api/instances/${id}/query/stream`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
    signal,
  });

  if (!response.ok) {
    throw new ApiError(await parseErrorMessage(response, `Query failed with status ${response.status}.`));
  }

  if (!response.body) {
    throw new ApiError("Streaming query response had no body.");
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });

      // SSE frames are separated by a blank line ("\n\n"); process each complete frame as
      // it arrives so progress events render immediately instead of batching until EOF.
      let separatorIndex: number;
      while ((separatorIndex = buffer.indexOf("\n\n")) !== -1) {
        const frame = buffer.slice(0, separatorIndex);
        buffer = buffer.slice(separatorIndex + 2);

        const parsed = parseSseFrame(frame);
        if (!parsed) continue;

        if (parsed.event === "progress") {
          onProgress(JSON.parse(parsed.data) as OrchestrationProgressDto);
        } else if (parsed.event === "result") {
          return JSON.parse(parsed.data) as UiSpecResponse;
        } else if (parsed.event === "error") {
          const errorBody = JSON.parse(parsed.data) as ErrorResponse;
          throw new ApiError(errorBody.message || "Query failed.");
        }
      }
    }
  } finally {
    reader.releaseLock();
  }

  throw new ApiError("Query stream ended without a result.");
}

function parseSseFrame(frame: string): { event: string; data: string } | null {
  let event = "message";
  const dataLines: string[] = [];

  for (const line of frame.split("\n")) {
    if (line.startsWith("event:")) {
      event = line.slice("event:".length).trim();
    } else if (line.startsWith("data:")) {
      dataLines.push(line.slice("data:".length).trim());
    }
  }

  if (dataLines.length === 0) return null;
  return { event, data: dataLines.join("\n") };
}

/** Calls GET /api/settings/llm to read the current (masked) LLM provider settings. */
export async function getLlmSettings(): Promise<LlmSettingsResponse> {
  const response = await fetch(`${API_BASE_URL}/api/settings/llm`);

  if (!response.ok) {
    throw new ApiError(await parseErrorMessage(response, `Fetching LLM settings failed with status ${response.status}.`));
  }

  return (await response.json()) as LlmSettingsResponse;
}

/**
 * Calls PUT /api/settings/llm to update the LLM provider settings (API key and/or model id)
 * for the running backend instance.
 */
export async function updateLlmSettings(request: UpdateLlmSettingsRequest): Promise<LlmSettingsResponse> {
  const response = await fetch(`${API_BASE_URL}/api/settings/llm`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new ApiError(await parseErrorMessage(response, `Updating LLM settings failed with status ${response.status}.`));
  }

  return (await response.json()) as LlmSettingsResponse;
}

// Phase 7: Saved connection management

export async function getSavedConnections(): Promise<SavedConnectionSummary[]> {
  const response = await fetch(`${API_BASE_URL}/api/connections`);
  if (!response.ok) {
    throw new ApiError(await parseErrorMessage(response, `Failed to load saved connections.`));
  }
  return (await response.json()) as SavedConnectionSummary[];
}

export async function getSavedConnectionById(id: string): Promise<SavedConnectionDetail> {
  const response = await fetch(`${API_BASE_URL}/api/connections/${id}`);
  if (!response.ok) {
    throw new ApiError(await parseErrorMessage(response, `Failed to fetch connection details.`));
  }
  return (await response.json()) as SavedConnectionDetail;
}

export async function saveConnection(request: SaveConnectionRequest): Promise<SavedConnectionSummary> {
  const response = await fetch(`${API_BASE_URL}/api/connections`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new ApiError(await parseErrorMessage(response, `Failed to save connection.`));
  }

  return (await response.json()) as SavedConnectionSummary;
}

export async function reconnectSavedConnection(id: string): Promise<InstanceStatusResponse> {
  const response = await fetch(`${API_BASE_URL}/api/connections/${id}/connect`, {
    method: "POST",
  });

  if (!response.ok) {
    throw new ApiError(await parseErrorMessage(response, `Reconnecting failed with status ${response.status}.`));
  }

  return (await response.json()) as InstanceStatusResponse;
}

export async function deleteSavedConnection(id: string): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/api/connections/${id}`, {
    method: "DELETE",
  });

  if (!response.ok && response.status !== 404) {
    throw new ApiError(await parseErrorMessage(response, `Failed to forget connection.`));
  }
}

export async function executeMutation(
  instanceId: string,
  request: ExecuteMutationRequest
): Promise<ExecuteMutationResponse> {
  const response = await fetch(`${API_BASE_URL}/api/instances/${instanceId}/mutate`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

  if (!response.ok) {
    throw new ApiError(await parseErrorMessage(response, `Mutation failed with status ${response.status}.`));
  }

  return (await response.json()) as ExecuteMutationResponse;
}

export async function getAuditLogs(
  limit: number = 50,
  entity?: string
): Promise<WriteAuditEntry[]> {
  const params = new URLSearchParams();
  params.set("limit", limit.toString());
  if (entity) params.set("entity", entity);

  const response = await fetch(`${API_BASE_URL}/api/audit?${params.toString()}`);
  if (!response.ok) {
    throw new ApiError(await parseErrorMessage(response, `Failed to fetch audit logs.`));
  }

  return (await response.json()) as WriteAuditEntry[];
}
