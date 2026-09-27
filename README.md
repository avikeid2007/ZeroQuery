# Zeroquery

> **Experimental.** This project is being built to learn and explore the "point at any
> database → get an AI-queryable app" pattern. It is not shipped or claimed as
> production-ready. See [`doc/Plan.md`](doc/Plan.md) Section 9 for known limitations.

Zeroquery is an open-source, self-service web app: paste a connection string, pick the
tables/columns you want exposed, and (eventually) query that database in natural language
with a dynamically rendered UI. Full design/architecture/phased plan lives in
[`doc/Plan.md`](doc/Plan.md).

**Current status: Phases 1–5 are done.** Connect a database, read its schema, select what
to expose, generate a schema-validated `dab-config.json`, start a real `dab start`
subprocess for it, and ask natural-language questions against it — answered via a real MCP
tool-calling loop against DAB's SQL MCP Server, streamed live via Server-Sent Events, and
rendered through a fixed UI Spec contract (table/chart/card/stat — no LLM-authored markup).
Security hardening (Phase 6) is next.

## Repo layout

```
backend/
  Zeroquery.Api/       ASP.NET Core Web API (.NET 10)
    Introspection/       POST /api/introspect
    ConfigGeneration/    POST /api/config/generate
    Instances/           POST /api/instances, GET /api/instances/{id}/status, DELETE /api/instances/{id}
    Query/               POST /api/instances/{id}/query, POST /api/instances/{id}/query/stream (SSE)
  Zeroquery.Core/      Class library
    Introspection/       Schema introspection provider abstraction + SQL Server/PostgreSQL/MySQL implementations
    ConfigGeneration/    dab-config.json generator + validator (against DAB's own embedded JSON schema)
    ProcessManagement/   DabProcessManager (spawn/kill `dab start`), port allocation, idle-timeout reaper
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

### LLM provider settings (Phase 4)

Querying requires an OpenRouter API key. There are **two ways to set it** — pick whichever
fits your workflow:

**1. From the UI** — click the **LLM settings (OpenRouter)** panel at the top of the page
(shows a red/green dot for whether a key is configured), paste your key, optionally set a
model id, and **Save**. Takes effect immediately for that running backend. This is
in-memory only and does **not** persist across a backend restart — it's the fastest way to
try things out, but set the env vars below too if you want it to stick.

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

### DAB Process Manager settings (Phase 3)

Deployment-configurable via the `DabProcessManager` config section or the documented env
var names (see `doc/Plan.md` Section 4):

| Setting | Env var | Default |
| --- | --- | --- |
| Max concurrent DAB subprocesses | `MCP_MAX_CONCURRENT_INSTANCES` | `10` |
| Idle timeout before reaping | `MCP_IDLE_TIMEOUT_MINUTES` | `30` |
| Port range | `DabProcessManager__PortRangeStart` / `...__PortRangeEnd` | `6000–6999` |

## Testing

```powershell
dotnet test backend/Zeroquery.Tests
```

## Security note (Phase 1–4 scope)

Connection strings entered in the setup wizard are sent once to the backend for a
metadata-only read and are **not persisted** across requests. The generated
`dab-config.json` never embeds the raw connection string either — it references an
environment variable via DAB's `@env('NAME')` function, which is set only on the spawned
subprocess's environment (never logged, never written to the config file on disk). DAB
subprocesses currently run under the same OS account as the orchestration API and are
reachable only on `localhost` — dedicated low-privilege process isolation and per-instance
resource caps are tracked as v2 hardening in `doc/Plan.md` Section 3, same as before this
is used with untrusted/community traffic.

## License

MIT (planned — see `doc/Plan.md` Section 8).
