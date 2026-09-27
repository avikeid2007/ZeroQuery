# Contributing to Zeroquery

Thank you for your interest in contributing to Zeroquery! Zeroquery is an open-source, self-service web application that transforms arbitrary relational databases (SQL Server, PostgreSQL, MySQL) into dynamic, natural-language queryable applications powered by Microsoft Data API builder (DAB) and MCP.

---

## 1. Prerequisites

Before setting up your local development environment, ensure you have installed:

- **[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)**
- **[Node.js 20+ / npm](https://nodejs.org/)**
- **[Microsoft Data API builder (dab)](https://learn.microsoft.com/azure/data-api-builder/)**:
  ```powershell
  dotnet tool install -g Microsoft.DataApiBuilder
  ```

---

## 2. Repository Structure

```text
Zeroquery/
├── backend/
│   ├── Zeroquery.Api/          # ASP.NET Core Minimal API endpoints & rate limiting
│   ├── Zeroquery.Core/         # Introspection, DAB process manager, SSRF, LLM orchestration, mutations
│   └── Zeroquery.Tests/        # xUnit test suite (129+ unit & integration tests)
├── frontend/
│   ├── src/app/                # Next.js App Router (setup wizard + query views)
│   ├── src/components/         # React UI components (TablePicker, QueryView, FormView, etc.)
│   └── src/lib/                # API client, selection helpers, TypeScript interfaces
├── doc/
│   └── Plan.md                 # Complete architectural blueprint and progress checklist
├── docker-compose.yml          # 1-command containerized deployment
└── README.md                   # Project overview, quickstart, and configuration reference
```

---

## 3. Development Workflow

### Running the Backend

```powershell
dotnet run --project backend/Zeroquery.Api
```

The backend starts at `http://localhost:5000` (and `https://localhost:7157`). OpenAPI documentation is available at `http://localhost:5000/openapi/v1.json`.

### Running the Frontend

```powershell
cd frontend
npm install
npm run dev
```

The frontend launches with Hot Module Replacement at `http://localhost:3000`.

### Running the Test Suite

Zeroquery maintains high test coverage. Always ensure all tests pass before opening a Pull Request:

```powershell
# Backend unit & integration tests
dotnet test backend/Zeroquery.Tests

# Frontend typecheck & production build
cd frontend
npm run build
```

---

## 4. Architectural Conventions & Core Principles

1. **Security-by-Default**:
   - Connection strings are **never** committed to version control and **never** embedded inline in `dab-config.json`. They are injected via child process environment variables (`@env('VAR')`).
   - Connection strings persisted to disk are encrypted at rest using the ASP.NET Core Data Protection API (`IConnectionStringProtector`).
   - Private network connections are permitted for local development by default (`MCP_BLOCK_PRIVATE_NETWORKS=false`), but can be toggled on for public/multi-tenant deployments.

2. **Deterministic UI Specs (No Arbitrary Code Execution)**:
   - The LLM never writes or executes arbitrary code or SQL in the user's browser.
   - The LLM interacts with DAB via structured MCP tool calls and renders results using predefined, validated UI Spec types: `Table`, `Chart`, `Card`, `Stat`, and `Form`.

3. **Human-in-the-Loop for Mutations**:
   - Database mutations (Create, Update, Delete) are strictly opt-in per table in the table picker.
   - The LLM is prohibited from firing writes autonomously. All write proposals render as a `Form` diff preview that requires human confirmation before execution.
   - Every mutation is logged to the write audit log (`IWriteAuditStore`).

4. **Documentation Integrity**:
   - When introducing new components or features, preserve existing comments and documentation.
   - Keep `README.md` and `doc/Plan.md` updated with any new configuration variables, endpoints, or architectural changes.

---

## 5. Submitting Pull Requests

1. **Fork the repository** and create a feature branch (`git checkout -b feature/my-new-feature`).
2. **Commit your changes** with clear, descriptive commit messages.
3. **Verify tests and builds** pass cleanly:
   ```powershell
   dotnet test backend/Zeroquery.Tests
   npm --prefix frontend run build
   ```
4. **Open a Pull Request** explaining the context, approach, and how you tested the changes.
