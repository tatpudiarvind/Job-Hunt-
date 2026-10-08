# ARVIND AI JOB HUNTER

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)
![Angular 22](https://img.shields.io/badge/Angular-22-DD0031?logo=angular)
![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)
![Tests](https://img.shields.io/badge/tests-193%20passing-brightgreen)

Single-user, local-first, **approval-first** AI job search and application assistant.

The agent analyzes jobs, matches them against your *verified* facts, proposes resume tailoring and emails — and then stops. Nothing is written to a resume file, nothing is sent through Gmail, and nothing is submitted anywhere until you review the exact content, approve it, and explicitly execute it.

## Table of contents

- [Features](#features)
- [Tech stack](#tech-stack)
- [What is implemented](#what-is-implemented)
- [Prerequisites](#prerequisites)
- [Run it](#run-it)
- [Project structure](#project-structure)
- [Execution modes](#execution-modes)
- [Configuration](#configuration-srcarvindjobhunterapiappsettingsjson)
- [Safety model](#safety-model)
- [Documentation](#documentation)
- [Also included](#also-included)
- [Not yet implemented](#not-yet-implemented)
- [Contributing](#contributing)
- [License](#license)

## Features

- **Human-in-the-loop by design** — every side effect becomes a hash-bound, time-limited, single-use `ApprovalRequest`; a single audited command executes it.
- **Job analysis & matching** against verified candidate facts with a configurable qualification threshold.
- **Resume tailoring** via Open XML — the master `.docx` is never modified; every output is a copy.
- **Email drafting** with optional Gmail draft/send through Google OAuth.
- **Pluggable LLM** — offline deterministic Demo provider or OpenAI.
- **DEMO / DRY_RUN / LIVE** execution modes, switchable at runtime.
- **Versioned JSON persistence** with atomic writes, backups, quarantine and recovery.
- **Full audit trail**, execution receipts, follow-up reminders, interview-prep links, backup & restore.
- **Markdown activity log** — a daily, human-readable `.md` log of requests, agent runs, LLM/Gmail calls, approvals and errors, with correlation ids and secrets masked ([docs/logging.md](docs/logging.md)).

## Tech stack

| Area | Technology |
| --- | --- |
| Backend | C# / .NET 10, ASP.NET Core Minimal APIs |
| Frontend | Angular 22, TypeScript, Vitest |
| Documents | Open XML SDK (`.docx`) |
| Integrations | Google OAuth 2.0, Gmail API, OpenAI (optional) |
| Storage | Versioned JSON files, ASP.NET DataProtection (DPAPI on Windows) |
| Logging | `Microsoft.Extensions.Logging` + custom Markdown file provider (`ArvindJobHunter.Logging`) |
| Tests | xUnit, `WebApplicationFactory` integration tests, Vitest |

## What is implemented

| Layer | Status |
| --- | --- |
| **Domain** | Job, JobApplication (16-state validated state machine), ApprovalRequest (payload-hash bound, TTL, one-shot consume), ResumeVersion, EmailDraft, AgentRun, CandidateFact, ExecutionReceipt |
| **Application** | Local auth (PBKDF2, 8 h sessions, logout), Approval service, `ExecuteApprovedActionCommand` (the only side-effect path; idempotent, receipted, audited), job/application/resume/email/fact services, `ExternalActionGuard` |
| **Agents** | Explicit tool registry (high-risk tools cannot be invoked by the agent), `AgentOrchestrator` (analyze → match → prepare → draft), Demo LLM (offline, deterministic), optional OpenAI provider |
| **Infrastructure** | Versioned JSON persistence with atomic writes/backup/quarantine/recovery audit, runtime settings, Google OAuth (refresh, DataProtection-encrypted tokens), Gmail draft/send adapter |
| **ResumeAutomation** | Open XML service; master `.docx` opened read-only, every tailored resume is a copy |
| **Worker** | Loopback-only document worker with shared secret and idempotent apply receipts |
| **API** | Minimal APIs, bearer auth handler, rate-limited auth, string enums, dashboard/settings/audit/runs/Google integration endpoints |
| **Web** | Angular 22 routed app: login, dashboard, jobs, job detail, approvals + receipts, applications, emails, profile/facts, activity, settings; global error handler that reports browser errors to the API log |
| **Logging** | Markdown file logger (daily files, size roll-over, retention, collapsible exceptions, secret masking), per-request log line + `X-Correlation-Id`, correlation ids on audit events, structured logs across services |
| **Tests** | 169 .NET tests (domain, application incl. execution gateway, agents, integration, API pipeline, logger) and 24 Vitest tests |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) 20+ and npm (for the Angular web app)
- Windows, macOS or Linux (DataProtection keys are DPAPI-protected on Windows only)
- Optional: an OpenAI API key and/or Google Cloud OAuth client for Gmail

## Run it

```powershell
git clone <your-repo-url>
cd "Job Hunter"

dotnet build ArvindJobHunter.slnx
dotnet test  ArvindJobHunter.slnx

# API (http://localhost:5228)
dotnet run --project src/ArvindJobHunter.Api

# Web (http://localhost:4200)
cd src/ArvindJobHunter.Web; npm install; npm start
npm test   # vitest unit tests (AuthService, interceptor)

# Optional: scripted end-to-end check against a running API
powershell -File scripts/smoke-test.ps1
```

First visit creates your local account (password ≥ 12 chars). A candidate profile and 14 verified skill facts are seeded on first start.

Logs: the API writes a daily Markdown log to `src/ArvindJobHunter.Api/data/logs/jobhunter-api-<date>.md` (the path is printed at startup). See [docs/logging.md](docs/logging.md).

## Project structure

```
.
├── ArvindJobHunter.slnx
├── src/
│   ├── ArvindJobHunter.Domain/            # Entities, enums, state machines
│   ├── ArvindJobHunter.Contracts/         # DTOs shared with the API/web
│   ├── ArvindJobHunter.Application/       # Use cases, approval service, guards
│   ├── ArvindJobHunter.Agents/            # Tool registry, orchestrator, LLM providers
│   ├── ArvindJobHunter.Infrastructure/    # JSON store, settings, Google/Gmail adapters
│   ├── ArvindJobHunter.Logging/           # Markdown file logger, request logging, correlation ids
│   ├── ArvindJobHunter.ResumeAutomation/  # Open XML resume service
│   ├── ArvindJobHunter.Worker/            # Loopback-only document worker
│   ├── ArvindJobHunter.Api/               # Minimal API host
│   └── ArvindJobHunter.Web/               # Angular 22 SPA
├── tests/                                 # Domain, Application, Agents, API, Integration tests
├── docs/                                  # Architecture, ADRs, integration guides
└── scripts/                               # smoke-test.ps1 and helpers
```

## Execution modes

| Mode | Resume files | Gmail | Notes |
| --- | --- | --- | --- |
| `DEMO` (default) | never written | never contacted | Approvals are recorded; receipts say so. |
| `DRY_RUN` | never written | never contacted | Simulates the LIVE path without side effects. |
| `LIVE` | copy of master written via Open XML | drafts/sends after approval + execute | Requires `Runtime:MasterResumePath` and Google OAuth configured. |

Switch modes in **Settings**. An approval granted in one mode cannot be executed in another.

## Configuration (`src/ArvindJobHunter.Api/appsettings.json`)

- `Storage:DataDirectory` – JSON data root (default `data`, env `Storage__DataDirectory`).
- `Runtime:Mode`, `Runtime:LlmProvider` (`Demo` | `OpenAI`), `Runtime:MasterResumePath`, `Runtime:QualificationThreshold`, `Runtime:ApprovalTtlHours`. Mode, LLM and master-resume values are the starting point until you save the **Settings** page (from then on `data/settings.json` wins for those fields); the qualification threshold and approval TTL are not editable in the UI and always come from configuration.
- `OpenAI:ApiKey`, `OpenAI:Model` – only used when `LlmProvider` is `OpenAI`.
- `Google:ClientId`, `Google:ClientSecret`, `Google:RedirectUri` – Gmail integration. Saving them on the Settings page writes them into `appsettings.json` in plain text — do not commit that file with real values (prefer user secrets or environment variables).
- `Web:BaseUrl` – CORS origin and OAuth return URL.
- `Worker:BaseUrl`, `Worker:SharedSecret` – optional. When both are set the API proxies resume read/apply to the loopback Worker (`dotnet run --project src/ArvindJobHunter.Worker`, default `http://127.0.0.1:5310`) for process isolation; otherwise Open XML runs in-process. Writes never silently fall back.
- `Logging:MarkdownFile:*` – Markdown log folder, size/retention limits and per-category levels (see [docs/logging.md](docs/logging.md)).

DataProtection keys under `<DataDirectory>/keys` are DPAPI-protected on Windows (per-user). On other OSes they are file-persisted only.

Secrets belong in user-secrets or environment variables, not in the repo.

## Safety model

1. The agent can only call tools in the registry; `SendGmailEmailTool`, `CreateGmailDraftTool`, `SubmitApplicationTool` are marked high-risk and the registry refuses to hand them to the agent.
2. Every proposed side effect becomes an `ApprovalRequest` with a SHA-256 hash of the exact payload, an action type, a mode, and an expiry.
3. `ExecuteApprovedActionCommand` is the sole execution path. `ExternalActionGuard` re-checks status, expiry, hash, action, and mode; any mismatch raises `ApprovalViolationException` (HTTP 409) and is audited as `EXECUTION_BLOCKED`.
4. Editing a draft or re-proposing resume changes invalidates prior approvals.
5. Execution is idempotent per key and produces an `ExecutionReceipt`; the approval is consumed exactly once.
6. Job descriptions and emails are passed to the LLM as untrusted data, never as instructions.
7. Application submission to job boards is intentionally **not** enabled in LIVE mode.

## Documentation

- [docs/architecture.md](docs/architecture.md) – layers, workflows, data model
- [docs/approval-engine.md](docs/approval-engine.md) – approval lifecycle and guard
- [docs/agent-runtime.md](docs/agent-runtime.md) – tools, orchestrator, runs
- [docs/llm-provider.md](docs/llm-provider.md) – Demo/OpenAI providers
- [docs/gmail-integration.md](docs/gmail-integration.md) – OAuth and Gmail boundary
- [docs/json-storage.md](docs/json-storage.md) – persistence guarantees
- [docs/logging.md](docs/logging.md) – Markdown log files, correlation ids, what is logged, configuration
- [docs/code-review-findings.md](docs/code-review-findings.md) – code review: bugs fixed and recommended improvements
- [docs/local-development.md](docs/local-development.md)
- [docs/roadmap.md](docs/roadmap.md) – what is next

## Also included

- **Import from URL** (Jobs page) - fetches a public posting once, extracts schema.org `JobPosting` JSON-LD or page text, and prefills the form for your review. Private/loopback hosts are blocked; nothing is saved until you click *Add job*.
- **Follow-up reminders** (Applications page, Dashboard) - schedule a due date/note per application; due items surface on the dashboard. Reminders only - any email still goes through approval.
- **Interview prep** (nav, or from a job) - curated outbound links grouped by company research, role/behavioral, each analyzed skill (gaps first), and practice/system design. Links point to YouTube search, Microsoft Learn, GeeksforGeeks, LeetCode, HackerRank, Glassdoor/Google searches and open in a new tab; nothing is embedded, fetched, or stored.
- **Backup & restore** (Settings) - zip export of data files (secrets, keys, tokens and the LLM API key excluded) and validated import with an automatic pre-import backup; importing an export keeps the API key already configured on this machine.

## Not yet implemented
Job-board discovery/scraping, browser automation, automatic application submission, agent-proposed follow-up emails, multi-user support, and a database backend. See the roadmap.

## Contributing

1. Fork the repository and create a feature branch (`git checkout -b feature/my-change`).
2. Keep the safety model intact: no new side-effect path may bypass `ExecuteApprovedActionCommand` and `ExternalActionGuard`.
3. Add or update tests (`dotnet test ArvindJobHunter.slnx`, `npm test` in `src/ArvindJobHunter.Web`).
4. Never commit secrets, `data/` or `keys/` directories — see `.gitignore`.
5. Open a pull request describing the change and any configuration impact.

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.
