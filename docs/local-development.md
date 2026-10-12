# Local Development Guide

## 1. Purpose

This guide describes the standard local development workflow for ArvindJobHunter. It covers prerequisites, backend and frontend startup, local data behavior, smoke testing, and secret management expectations.

---

## 2. Prerequisites

The recommended local development environment includes:

- .NET 10 SDK
- Node.js 20 or later
- Angular CLI 22-compatible tooling

Microsoft Word is not required for the current local workflow because resume automation uses Open XML in the present implementation path.

---

## 3. Backend Development Workflow

Use the following commands from the repository root:

```powershell
dotnet build ArvindJobHunter.slnx
dotnet test ArvindJobHunter.slnx
dotnet run --project src/ArvindJobHunter.Api
dotnet run --project src/ArvindJobHunter.Worker
```

Expected local endpoints:

- API: `http://localhost:5228`
- Worker (optional): `http://127.0.0.1:5310`

The worker is optional unless the development scenario requires worker-based capabilities.

---

## 4. Frontend Development Workflow

Run the frontend from the Angular project directory:

```powershell
cd src/ArvindJobHunter.Web
npm install
npm start
npm run build
```

Expected local endpoint:

- Frontend: `http://localhost:4200`

The frontend expects the backend API to be available on the configured API base URL, which is typically `http://localhost:5228` for local development.

---

## 5. Local Data and Storage Behavior

The application currently uses JSON-based local persistence.

- Default data directory: `./data` under the API content root
- Override setting: `Storage__DataDirectory`

You can reset the local seeded state by removing the current data directory and restarting the application.

Related document:

- `docs/json-storage.md`

---

## 6. Runtime Mode Expectations

The default local mode is `DEMO`.

In Demo mode:

- no real external provider calls should occur
- no production-style external execution should occur
- the environment remains safe for exploration and controlled testing

Other modes such as `DRY_RUN` and `LIVE` should be enabled intentionally and only when the related setup is understood.

Related documents:

- `docs/approval-engine.md`
- `docs/llm-provider.md`

---

## 7. End-to-End Smoke Testing

A local smoke test script is available for validating the primary workflow.

Run:

```powershell
powershell -File scripts/smoke-test.ps1
```

With the API running, this script validates a representative end-to-end path including:

- local account creation or login
- job creation
- analyze and prepare workflow execution
- approval enforcement before execution
- approval and execution behavior
- idempotency checks
- audit coverage
- logout flow

---

## 8. Secret and Configuration Management

Use either:

- `dotnet user-secrets` for development
- environment variables for local overrides and testing

Typical sensitive values include:

- `OpenAI:ApiKey`
- `Google:ClientId`
- `Google:ClientSecret`
- `Worker:SharedSecret`

Do not commit real secrets to source control.

---

## 9. Recommended Local Development Sequence

1. build the solution
2. run tests
3. start the backend API
4. start the optional worker if needed
5. start the Angular frontend
6. verify the UI loads and the API responds
7. run the smoke test when end-to-end validation is required

---

## 10. Related Documentation

- `docs/STARTUP_GUIDE.md`
- `docs/architecture.md`
- `docs/json-storage.md`
- `docs/approval-engine.md`
- `docs/GOOGLE_OAUTH_CONFIGURATION_GUIDE.md`

---

## 11. Summary

The standard local development workflow is centered around a .NET 10 API, an Angular frontend, optional worker execution, and JSON-based local persistence. Developers should keep local secrets out of source control and use Demo mode by default unless a scenario explicitly requires more advanced runtime behavior.
