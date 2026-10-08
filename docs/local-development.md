# Local Development

Prerequisites: .NET 10 SDK, Node.js 20+, Angular CLI 22. Microsoft Word is **not** required — resume automation uses Open XML.

## Backend

```powershell
dotnet build ArvindJobHunter.slnx
dotnet test  ArvindJobHunter.slnx
dotnet run --project src/ArvindJobHunter.Api        # http://localhost:5228
dotnet run --project src/ArvindJobHunter.Worker     # optional, http://127.0.0.1:5310
```

The JSON data directory defaults to `./data` under the API content root; override with `Storage__DataDirectory`. Delete it to reset to a fresh seed.

Default mode is `DEMO`: no external network calls, no files written. See README for `DRY_RUN` and `LIVE`.

## Logs

The API writes a Markdown log per day to `<data directory>/logs/jobhunter-api-<yyyy-MM-dd>.md`; the worker writes `src/ArvindJobHunter.Worker/logs/jobhunter-worker-<yyyy-MM-dd>.md`. Open them with VS Code's Markdown preview. Each API response has an `X-Correlation-Id` header that you can search for in the log. Details and settings: [logging.md](logging.md).

Note: `dotnet test ArvindJobHunter.slnx` builds only the test projects and what they reference — the Worker is not among them. Use `dotnet build ArvindJobHunter.slnx` to compile everything.

## Frontend

```powershell
cd src/ArvindJobHunter.Web
npm install
npm start        # http://localhost:4200, expects API on :5228 (src/environments/environment.ts)
npm run build
```

## End-to-end smoke test

With the API running: `powershell -File scripts/smoke-test.ps1`. It creates/logs in a local account, adds a job, runs analyze → prepare, verifies execution is blocked before approval (409), approves, executes, checks idempotency and audit, and logs out.

## Secrets

Use `dotnet user-secrets` (API project) or environment variables for `OpenAI:ApiKey`, `Google:ClientId`, `Google:ClientSecret`, and `Worker:SharedSecret`. Never commit them.
