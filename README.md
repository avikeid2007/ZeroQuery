# Zeroquery

> **Experimental.** This project is being built to learn and explore the "point at any
> database → get an AI-queryable app" pattern. It is not shipped or claimed as
> production-ready. See [`doc/Plan.md`](doc/Plan.md) Section 9 for known limitations.

Zeroquery is an open-source, self-service web app: paste a connection string, pick the
tables/columns you want exposed, and (eventually) query that database in natural language
with a dynamically rendered UI. Full design/architecture/phased plan lives in
[`doc/Plan.md`](doc/Plan.md).

**Current status: Phases 1–6 are done.** Connect a database, read its schema, select what
to expose, generate a schema-validated `dab-config.json`, start a real `dab start`
subprocess for it, and ask natural-language questions against it — answered via a real MCP
tool-calling loop against DAB's SQL MCP Server, streamed live via Server-Sent Events, and
rendered through a fixed UI Spec contract (table/chart/card/stat — no LLM-authored markup).
Security hardening (Phase 6) is in place: SSRF protection against internal/private IP targets,
Data Protection API encryption for connection strings, per-instance CPU/memory caps via Win32 Job Objects,
rate limiting across API endpoints, and low-privilege OS execution support. Persistence (Phase 7) is next.

## Repo layout

```
backend/
  Zeroquery.Api/       ASP.NET Core Web API (.NET 10)
    Introspection/       POST /api/introspect
    ConfigGeneration/    POST /api/config/generate
    Instances/           POST /api/instances, GET /api/instances/{id}/status, DELETE /api/instances/{id}
    Query/               POST /api/instances/{id}/query, POST /api/instances/{id}/query/stream (SSE)
    Security/            Rate limiting policies (query/introspect/instances per client IP)
  Zeroquery.Core/      Class library
    Introspection/       Schema introspection provider abstraction + SQL Server/PostgreSQL/MySQL implementations
    ConfigGeneration/    dab-config.json generator + validator (against DAB's own embedded JSON schema)
    ProcessManagement/   DabProcessManager (spawn/kill `dab start`), port allocation, idle-timeout reaper
      ResourceLimits/    Win32 Job Object & cross-platform CPU/memory limiters + kill-on-close
    Security/            ISsrfValidator (private/internal IP blocking), IConnectionStringProtector (Data Protection API)
    Mcp/                 McpClient — JSON-RPC client for DAB's streamable-HTTP MCP transport
    Orchestration/        ILlmProvider (OpenRouter impl) + OrchestrationService tool-calling loop (with SSE progress reporting) + UiSpec contract
    Settings/            GET/PUT /api/settings/llm
  Zeroquery.Tests/     xUnit tests (unit + live integration against a real `dab` subprocess)
frontend/
  src/app/             Next.js App Router pages
  src/components/      ConnectionForm, TablePicker, ConfigPreview, InstanceStatusBadge, QueryView (SSE + Recharts table/chart/card/stat), LlmSettingsPanel
  src/lib/             API client (incl. SSE stream parsing), shared types, picker selection state
doc/
  Plan.md              Full project plan (architecture, phases, security, decisions)
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) — upgraded from .NET 9
  during Phase 5 to get native `TypedResults.ServerSentEvents` support for query streaming.
- [Node.js 20+](https://nodejs.org/) and npm
- [Data API builder CLI](https://learn.microsoft.com/azure/data-api-builder/) (`dab`) on
  `PATH` — required starting Phase 3, since Zeroquery spawns `dab start` as a subprocess.
  Install with `dotnet tool install -g Microsoft.DataApiBuilder`.

## Running locally

**Backend** (from `backend/`):

```powershell
dotnet run --project Zeroquery.Api --urls http://localhost:5000
```

**Frontend** (from `frontend/`):

```powershell
npm install
copy .env.local.example .env.local
npm run dev
```

Then open http://localhost:3000, paste a connection string (SQL Server, PostgreSQL, or
MySQL), step through the schema picker, generate a `dab-config.json`, and click **Start DAB
instance** to spin up a real `dab start` subprocess — its status badge polls live until it's
`Running`. Once running, use the **Ask a question** box to query it in natural language —
the query streams live progress ("Thinking…", "Calling read_records…", "Rendering result…")
via Server-Sent Events while the LLM's tool-calling loop runs, then renders the result as a
table, chart (bar/line/pie via Recharts), card, or stat, depending on what the model chose.
**Disconnect** stops the instance again.

### Query streaming (Phase 5)

`POST /api/instances/{id}/query/stream` is the SSE sibling of the original blocking
`POST /api/instances/{id}/query` endpoint (both still exist — the frontend uses the
streaming one). It emits `progress` events (`{ stage, toolName }`, stages:
`Thinking`/`ToolCall`/`ToolResult`/`Rendering`) while `OrchestrationService`'s tool-calling
loop runs, followed by exactly one terminal `result` (a `UiSpecResponse`) or `error` event.
Pre-flight validation (empty prompt, unknown/not-ready instance) still returns a normal JSON
error with a matching HTTP status code — the response only switches to
`text/event-stream` once the orchestration loop actually starts.

### LLM provider settings (Phase 4 & 7)

Querying requires an OpenRouter API key. There are **two ways to set it** — pick whichever
fits your workflow:

**1. From the UI** — click the **LLM settings (OpenRouter)** panel at the top of the page
(shows a red/green dot for whether a key is configured), paste your key, optionally set a
model id, and **Save**. Takes effect immediately for that running backend. In Phase 7,
settings configured here are encrypted at rest via the Data Protection API and automatically
persist across backend restarts unless `MCP_PERSISTENCE_MODE=session-only` is configured.

**2. Env vars / appsettings.json** (doc/Plan.md Section 8 — model configurable per env, not
hardcoded) — used to seed the initial value at startup:

| Setting | Env var | Default |
| --- | --- | --- |
| OpenRouter API key | `OPENROUTER_API_KEY` | *(required)* |
| Model id | `LLM_MODEL_ID` | `openrouter/auto` |

Without an API key set (via either method), `/api/instances/{id}/query` returns a `502`
with a clear configuration error — the rest of the pipeline (MCP session, tool discovery)
still runs and is exercised by the test suite independent of having a real key.

The API key is never returned in full by `GET /api/settings/llm` — only a masked suffix
(e.g. `••••••••ab3x`) plus whether one is configured at all.

### Persistence & Saved Connections (Phase 7)

Configurable via environment variables or `appsettings.json`:

| Setting | Env var | Default |
| --- | --- | --- |
| Persistence mode (`save` or `session-only`) | `MCP_PERSISTENCE_MODE` | `save` |
| Storage directory | `MCP_PERSISTENCE_DIR` | `%LOCALAPPDATA%/Zeroquery/data` (Windows) or `~/.zeroquery/data` (Linux/macOS) |

- **Encrypted at rest**: Saved connection profiles (`saved-connections.json`) and LLM settings (`llm-settings.json`) are persisted to disk using thread-safe, atomic file writes. All connection strings and API keys are encrypted at rest via ASP.NET Core Data Protection (`IConnectionStringProtector`, prefixed with `zqenc:v1:`).
- **Zero raw credential leakage**: The `GET /api/connections` and `GET /api/connections/{id}` endpoints never expose raw connection strings back to the browser; only metadata (database type, name, entities, timestamps) is returned.
- **Explicit confirmation flow**: Per security and architectural requirements, Zeroquery never silently auto-reconnects on page load. Saved connections are presented on the initial Connect step with a **"Reconnect to Database"** action that prompts for human confirmation before provisioning a subprocess.
- **Forget connection**: Users can remove saved profiles at any time via the UI (**"Forget"** button) or `DELETE /api/connections/{id}`, permanently purging the profile from storage.

### DAB Process Manager & Security settings (Phases 3 & 6)

Deployment-configurable via the `DabProcessManager` config section or the documented env
var names (see `doc/Plan.md` Section 4 & 6):

| Setting | Env var | Default |
| --- | --- | --- |
| Max concurrent DAB subprocesses | `MCP_MAX_CONCURRENT_INSTANCES` | `10` |
| Idle timeout before reaping | `MCP_IDLE_TIMEOUT_MINUTES` | `30` |
| Port range | `DabProcessManager__PortRangeStart` / `...__PortRangeEnd` | `6000–6999` |
| Max subprocess memory cap (MB) | `MCP_INSTANCE_MAX_MEMORY_MB` | `512` (0 = uncapped) |
| Max subprocess CPU rate cap (%) | `MCP_INSTANCE_CPU_LIMIT_PERCENT` | `50` (0 = uncapped) |
| Block private / SSRF network targets | `MCP_BLOCK_PRIVATE_NETWORKS` | `false` *(set `true` for public/multi-tenant)* |
| Query rate limit (per minute/IP) | `RATE_LIMIT_QUERIES_PER_MINUTE` | `30` |
| Introspect rate limit (per minute/IP) | `RATE_LIMIT_INTROSPECT_PER_MINUTE` | `10` |
| Instance start rate limit (per minute/IP) | `RATE_LIMIT_INSTANCES_PER_MINUTE` | `5` |
| Mutation rate limit (per minute/IP) | `RATE_LIMIT_MUTATIONS_PER_MINUTE` | `5` |
| Dedicated low-privilege OS user | `MCP_SUBPROCESS_USER` | *(optional, recommended for prod)* |
| Subprocess user password | `MCP_SUBPROCESS_PASSWORD` | *(optional)* |
| Subprocess user domain | `MCP_SUBPROCESS_DOMAIN` | *(optional)* |

### Write Access & Full CRUD (Phase 8)

Zeroquery supports safe, human-confirmed database mutations (Create, Update, Delete):

- **Per-entity, per-operation permissions**: Tables default to strictly read-only. In the table picker, users must explicitly opt into `Create`, `Update`, or `Delete` permissions individually per table. Keyless tables and views cannot be opted into mutations.
- **Confirm-before-execute (`form` UI Spec)**: The LLM never fires mutations unilaterally. When asked to modify data, it retrieves context and emits a `form` proposal presenting a diff of current values vs proposed new values. The user can tweak proposed fields and must explicitly check a confirmation checkbox before clicking **"Confirm & Execute"**.
- **Autonomous tool exclusion**: Write-capable MCP tools are stripped from the LLM's tool pool during query execution, ensuring prompt injections or hallucinations cannot trigger backend writes behind the scenes.
- **Dedicated write audit trail**: All mutation attempts (timestamp, client IP, target entity, operation, primary key, previous values, new values, success/failure) are logged to a tamper-evident audit store (`audit-log.json`). Users can inspect recent writes at any time via the **"Audit Log"** modal in the UI or `GET /api/audit`.
- **Tighter rate limiting**: Mutations are guarded by a stricter rate limit (`RATE_LIMIT_MUTATIONS_PER_MINUTE`, default 5/minute/IP) than read queries.

## Testing

```powershell
dotnet test backend/Zeroquery.Tests
```

## Security architecture (Phase 6 & 8)

- **SSRF Protection**: `ISsrfValidator` inspects database connection strings across SQL Server, PostgreSQL, and MySQL. It resolves DNS hostnames and blocks connections to loopback (`127.0.0.0/8`, `::1`), private networks (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`), and link-local cloud metadata endpoints (`169.254.169.254`, `fe80::/10`). Disabled by default (`MCP_BLOCK_PRIVATE_NETWORKS=false`) so users can seamlessly connect to local databases, LocalDB, Docker, and internal subnets. Can be enabled (`MCP_BLOCK_PRIVATE_NETWORKS=true`) in public or multi-tenant deployments.
- **Connection String Protection at Rest**: `IConnectionStringProtector` uses ASP.NET Core Data Protection API (`IDataProtectionProvider`) to encrypt connection strings at rest. Protected strings are prefixed with `zqenc:v1:` and automatically unmasked only when passing environment variables into isolated child processes.
- **Process Resource Caps**: On Windows, child `dab` subprocesses are bound to Win32 Job Objects (`IProcessResourceLimiter`) with hard memory caps (`MCP_INSTANCE_MAX_MEMORY_MB`), CPU rate limits (`MCP_INSTANCE_CPU_LIMIT_PERCENT`), and `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` (ensuring operating-system level cleanup of all subprocesses if Zeroquery terminates).
- **Abuse & Rate Limiting**: ASP.NET Core rate limiting enforces per-client IP policies on queries (30/min), introspection (10/min), instance launches (5/min), and mutations (5/min). Per-IP concurrency caps prevent a single client from monopolizing the host's DAB process pool (`MCP_MAX_INSTANCES_PER_IP`).
- **Low-Privilege Process Execution**: Dedicated OS credentials can be configured (`MCP_SUBPROCESS_USER`) so DAB child processes run under a restricted service account rather than the API host identity.
- **Human-in-the-Loop Mutations**: All write operations require user confirmation through the `form` interface, preventing LLM prompt injections or adversarial data from autonomously executing writes.

## License

MIT (planned — see `doc/Plan.md` Section 8).
