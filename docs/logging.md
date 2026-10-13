# Logging

Job Hunter writes a **human-readable Markdown log** for the API and the resume worker, next to the normal console output.
Open it in VS Code's Markdown preview (or on GitHub) to get tables, colour-coded levels and collapsible exception details;
in a plain text editor it is still one line per event.

## Where the files are

| Process | Folder | File name |
| --- | --- | --- |
| API | `<Storage:DataDirectory>/logs` (by default `src/ArvindJobHunter.Api/data/logs`) | `jobhunter-api-2026-10-08.md` |
| Worker | `<worker content root>/logs` (by default `src/ArvindJobHunter.Worker/logs`) | `jobhunter-worker-2026-10-08.md` |

* One file per day (local time). When a file reaches `MaxFileSizeBytes` the day continues in `…-002.md`, `…-003.md`.
* Only the newest `RetainedFileCountLimit` files are kept (default 31); older ones are deleted automatically.
* Every application start appends a **session** with a metadata table (version, environment, machine, runtime, folders).
  A clean shutdown closes the session with a summary line (uptime, number of entries, warnings, errors).
  A forced stop (for example `jobhunter.ps1 stop`, which kills the process) simply ends without that summary — entries are flushed continuously, so nothing else is lost.
* The file is opened only for the moment each batch is appended, so you can open, copy or even delete it while the app runs (a deleted file is started again with its headers). If a write fails because another program locks the file, it is retried every few seconds.
* `logs/` folders are git-ignored.

The API also prints the log folder at startup (`Job Hunter API ready … Markdown logs: …`).

## What a file looks like

```markdown
# 📒 Job Hunter API — log for Thursday, 8 October 2026

## ▶️ Session started · 14:47:17 · PID 45576

| Time | Level | Source | Message | Correlation |
| :--- | :--- | :--- | :--- | :--- |
| 14:47:54.474 | ⚠️ WARN | Features.LocalAuthenticationService | Login failed for arvind: wrong password | `0bd8b6f2…` |
| 14:47:54.765 | ℹ️ INFO | Agents.AgentOrchestrator | Agent run b336… started: JobAnalysisAgent/AnalyzeAndMatch for job … (LLM Demo, DEMO mode) | `12a6af99…` |
| 14:47:54.860 | ℹ️ INFO | Logging.RequestLoggingMiddleware | POST /api/jobs/…/analyze → 200 in 132.3 ms | `12a6af99…` |
| 14:47:57.723 | ⚠️ WARN | Agents.AgentOrchestrator | Tool AnalyzeJobTool failed in 2046 ms (run 2817…): No connection could be made … ⤵ | `cfd9c0f4…` |

<details>
<summary>⚠️ 14:47:57.723 · <code>System.Net.Http.HttpRequestException: No connection could be made …</code></summary>

    (full exception and stack trace)

</details>
```

* **Level** — 🔍 TRACE · 🐞 DEBUG · ℹ️ INFO · ⚠️ WARN · ❌ ERROR · 🔥 CRITICAL.
* **Source** — the last two segments of the logger category (`Features.ExecuteApprovedActionCommand`, `Hosting.Lifetime`, `Web.Client`, …).
* **⤵** — the entry has an exception; it follows in a collapsible block.
* **Correlation** — the W3C trace id of the HTTP request (see below).

## Correlation ids

Every API response carries an `X-Correlation-Id` header. The same id appears

* in the **Correlation** column of every log line written while handling that request (request line, audit events, agent and tool calls, LLM and Gmail calls, exceptions);
* as `correlationId` on the **audit events** created by that request — the Activity page shows it as `log #…`;
* inside the `traceId` of ProblemDetails error responses (`00-<correlation id>-…-00`).

To investigate something you saw in the UI, take the id from the Activity page (or the browser's network tab) and search the day's log for it.

## What is logged

| Area | Examples |
| --- | --- |
| HTTP | One line per request: method, path (query string with secrets masked), status, duration. 4xx = WARN, 5xx = ERROR; `OPTIONS` and `/health` at DEBUG. Unhandled exceptions with stack traces. Rate-limit rejections. |
| Startup | Data/log folders, runtime settings summary (mode, LLM provider/model, *whether* an API key is set, master resume), Google OAuth / worker status, a warning when LIVE mode is active, seeding. |
| Authentication | Sign-up, login success/failure (with reason), logout, session expiry, password reset (WARN). Passwords and tokens are never logged, and a username that does not match the account is not logged either (it may be a password typed into the wrong box). |
| Audit trail | Every audit event (approvals requested/approved/rejected/invalidated, executions, settings changes, facts, jobs, emails, follow-ups, data import/export, resume uploads, agent runs) is mirrored into the log. Problem outcomes (`BLOCKED`, `FAILED`, `UNKNOWN`) are WARN. |
| Approvals & execution | Execution start, idempotent replays, guard violations, results with duration, approval expiry and supersede (which are not audited). |
| Agents & LLM | Run start/end with duration and tool count, every tool call with duration and summary or error (+ exception), match score vs. threshold, LLM latency and token usage, the provider's error message when a call fails, a one-time warning when the selected provider cannot be used. |
| Integrations | Google OAuth start/connect/disconnect and token-exchange failures (Google's error body), Gmail sends/drafts with recipient, status and Gmail id, job-import fetches and blocked (private/redirected) URLs, resume worker calls and fallbacks. |
| Persistence | Corrupt JSON files quarantined and restored from backup (ERROR), saves at DEBUG, data import (WARN) and export. |
| Browser | Unexpected Angular errors are posted to `POST /api/client-logs` and written under the `Web.Client` source with the browser stack. HTTP errors are not re-reported because the API already logged them. |
| Worker | Startup, rejected requests (non-loopback or wrong shared secret), read/apply results, idempotent replays. |

## Secrets

Before anything is written, messages and exception texts pass through a redactor that masks

* `Bearer …` tokens and OpenAI-style `sk-…` keys,
* JSON properties such as `"password"`, `"llmApiKey"`, `"clientSecret"`, `"refresh_token"`, `"access_token"`,
* query parameters such as `code`, `state`, `token`, `client_secret`, `key`,
* `key=value` / `key: value` pairs such as `password=…`, `ApiKey=…`, `X-Worker-Secret: …`.

`RuntimeSettings.ToString()` also masks the API key, and the worker's generated one-time secret is printed to the console only.
Log files can still contain personal data (your email address, recruiter addresses, file paths), so keep them local.

## Configuration

All settings live under `Logging:MarkdownFile` in `appsettings.json` (or environment variables such as `Logging__MarkdownFile__Enabled=false`).

| Setting | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `true` | Turn the Markdown file off without removing the provider. |
| `Directory` | API: `<data>/logs`, Worker: `<content root>/logs` | Absolute, or relative to the content root. Environment variables are expanded. |
| `FileNamePrefix` | `jobhunter-api` / `jobhunter-worker` | File name prefix. |
| `Title` | `Job Hunter API` / `Job Hunter Worker` | Heading at the top of each file. |
| `MaxFileSizeBytes` | `10485760` (10 MB) | Start a `-002` part when reached; `0` disables size rolling. |
| `RetainedFileCountLimit` | `31` | Files to keep; `0` keeps all. |
| `UseUtcTimestamps` | `false` | Use UTC instead of local time (also for the midnight roll-over). |
| `QueueCapacity` | `10000` | Entries buffered for the background writer; overflow is dropped and reported in the log. |
| `MaxMessageLength` | `8000` | Longer messages are truncated. |
| `RedactSecrets` | `true` | Mask secrets as described above. |
| `LogLevel` | `Default: Information`, `Microsoft.AspNetCore: Warning`, `System.Net.Http.HttpClient: Warning` | Standard per-category filters for this provider only. Set e.g. `"ArvindJobHunter": "Debug"` to also see JSON saves and LLM request sizes. |

## Using it in code

The provider is a normal `ILoggerProvider`, so any class can take an `ILogger<T>` and use message templates:

```csharp
logger.LogInformation("Approval {ApprovalId} finished with {Result} in {ElapsedMs} ms", approval.Id, receipt.Result, elapsed);
```

Hosting helpers (project `ArvindJobHunter.Logging`):

```csharp
builder.Logging.AddMarkdownFile(o => { o.Directory = "..."; o.FileNamePrefix = "jobhunter-api"; o.Title = "Job Hunter API"; });
app.LogUnhandledExceptions("ArvindJobHunter.Api"); // crashes are flushed to disk before the process ends
app.UseMarkdownRequestLogging();                    // register before UseExceptionHandler
```
