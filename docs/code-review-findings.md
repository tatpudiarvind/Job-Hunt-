# Code review findings — October 2026

Scope: the whole solution (Domain, Application, Agents, Infrastructure, ResumeAutomation, Worker, API, Angular web app, tests and docs).
Method: read every source file, ran the existing suites (67 .NET + 13 Vitest tests, all green), then ran the API and worker end-to-end against a throw-away data directory.

The architecture is sound: a clear approval-first core (payload-hash-bound, single-use approvals behind one execution gateway and a guard), explicit tool registry, versioned JSON persistence with recovery, and good test coverage of the domain. The issues below are mostly at the edges — integration wiring, configuration, and failure paths. Before this review the code base had **no logging at all** (only the console defaults), which made several of these failures invisible.

## 1. Bugs fixed in this change

| # | Severity | Area | Problem | Fix | Regression test |
| --- | --- | --- | --- | --- | --- |
| 1 | High | Gmail / OAuth | `GoogleOAuthService` was registered **Transient** but kept the OAuth `state` in an instance dictionary. `/authorize` and `/callback` got different instances, so the callback never found the state and **Gmail could never be connected** (the access-token cache was useless too). | Registered as a singleton that creates `HttpClient`s from `IHttpClientFactory`; token cache stored atomically. | `GoogleOAuthService_IsASingleton…` |
| 2 | High | Settings | Uploading a master resume rebuilt `RuntimeSettings` with only five fields, **resetting the LLM display name, base URL, model and API key**. The OpenAI key was deleted from `settings.json`. | `RuntimeSettings` is now a record; the upload uses `current with { MasterResumePath = … }`. | `MasterResumeUpload_KeepsTheConfiguredLlmProviderAndApiKey` |
| 3 | High | Web ↔ API | The Emails page called `POST /api/emails/{id}/request-approval` **without the required `send` flag**, so "Request approval to send" always failed (HTTP 500 in Development, 400 otherwise). | Angular client sends `?send=true`; missing/invalid parameters now return 400 in every environment. | `api.service.spec.ts`, `EmailApprovalToSend_…` |
| 4 | High | Safety model | `DRY_RUN` **wrote tailored resume files** (only `DEMO` was short-circuited), contradicting the documented guarantee. In `LIVE` with a missing master file the receipt said "Demo mode … SUCCESS" although nothing was written. | Only `LIVE` writes documents; `LIVE` without a master resume returns a `FAILED` receipt and keeps the approval. | `DryRun_RecordsTheApproval_WithoutWritingADocument`, `Live_WithAMissingMasterResume_…` |
| 5 | Medium | Safety model | Executions were not serialized: two clicks/tabs with different idempotency keys could both pass the guard before either consumed the approval → **email sent twice**. | Executions run behind a process-wide lock and re-read the approval inside it. | `ConcurrentExecutions_WithDifferentKeys_SendTheEmailOnlyOnce` |
| 6 | Medium | Execution | Executor exceptions (Open XML, worker, I/O, HTTP) escaped as a bare 500 with **no receipt and no audit entry**. A Gmail exception after the request may have reached Google was indistinguishable from "not sent". | Unexpected failures produce a `FAILED` receipt (approval stays usable); Gmail exceptions produce `UNKNOWN` and consume the approval so it cannot be blindly retried. | `UnexpectedExecutorFailure_…`, `GmailException_IsUnknown_…` |
| 7 | Medium | Agents | A cancelled request left the agent run **`RUNNING` forever** (the `finally` block saved with the cancelled token, which throws). | Cancellation marks the run failed and persistence/audit use `CancellationToken.None`. | covered by `AgentFailure_Returns502_…` flow |
| 8 | Medium | Configuration | `Runtime:*` settings in `appsettings.json` were **ignored**: the "no settings file yet" check tested `LlmProvider`, which defaults to `"Demo"`. `QualificationThreshold`/`ApprovalTtlHours` (not editable in the UI) could never be configured. | `IJsonStore.ExistsAsync`; configured values apply until the Settings page is saved; threshold and TTL always come from configuration. | `JsonRuntimeSettingsProviderTests` |
| 9 | Medium | Security (SSRF) | Job import only checked the literal host of the first URL. **Redirects** (auto-followed) and **host names resolving to private IPs** reached loopback/LAN services; IPv6 private ranges were not covered. | Redirects are followed manually and every hop is validated, host names are resolved and checked, IPv6 ULA/link-local/site-local and CGNAT ranges are blocked, and a `SocketsHttpHandler.ConnectCallback` re-checks the address actually dialled (closes the DNS-rebinding window; proxies keep working). | `Redirect_ToAPrivateAddress_IsBlocked…`, `HostResolvingToAPrivateAddress_IsBlocked`, `ConnectCallback_RefusesToDialAPrivateAddress…`, `IsPublicAddress_…` |
| 10 | Medium | Security | Data export zipped `settings.json` **including the plaintext LLM API key**, although the UI promises "secrets excluded". | Key blanked on export; importing such an archive keeps the key already configured locally. | `Export_RemovesTheLlmApiKey_…`, `Import_OfAnExportWithoutKey_…` |
| 11 | Medium | Security | `PUT /api/settings` **echoed the full settings, including the API key**, back to the browser. | Returns the same `SettingsResponse` as `GET` (key is write-only). | `Settings_CanPersistOpenAiCompatibleConfiguration…` |
| 12 | Medium | Robustness | Saves sporadically failed with `UnauthorizedAccessException` on `File.Move` (Windows Defender/indexer briefly locking freshly written files; reproduced while seeding 28 facts). | Short retry around the backup copy and the atomic replace. | full API suite run repeatedly |
| 13 | Low | Agents API | Expected LLM/tool failures (bad key, timeout, unusable output) returned **HTTP 500** with no detail. | `AgentRunFailedException` → **502** ProblemDetails with the provider's message and the `runId`. | `AgentFailure_Returns502_AndPersistsTheFailedRunWithItsToolCall` |
| 14 | Low | Agents | The failed tool call was **dropped from the persisted run** when analysis/matching failed. | Runs are tracked in a mutable session so every tool call is kept. | same as above |
| 15 | Low | Agents | OpenAI errors lost the response body ("401 Unauthorized" without the reason). | Error message from `{"error":{"message":…}}` is included and logged. | — |
| 16 | Low | Agents | Decimal/string match scores (`85.6`, `"72"`) and LLM emails without a body crashed the run. | Lenient number parsing; missing subject/body is a clean tool failure. | `ToolRobustnessTests` |
| 17 | Low | Agents | The recruiter-email workflow wrote no `AGENT_RUN_*` audit event; HTTP timeouts escaped the tool invoker. | Uniform run finishing; timeouts become recorded tool failures. | `ToolInvoker_TreatsTimeouts_AsToolFailures` |
| 18 | Low | Email | A send approval could be requested with an empty or malformed recipient (cover letters have `To = ""`); CR/LF in `To` allowed **MIME header injection** (e.g. a hidden `Bcc:`). | Recipients are validated (`MailAddress`, no control characters) before approval. | `EmailRecipientValidationTests`, `EmailApprovalToSend_…` |
| 19 | Low | OAuth | Denying consent (`error=access_denied`, no `code`) showed a raw 400 page. | Callback redirects to `/settings?google=failed`; the page shows the result. | `GoogleCallback_WhenConsentIsDenied…` |
| 20 | Low | Worker proxy | A worker **timeout** did not fall back to in-process reads (only connection errors did). | Timeouts fall back as well. | — |
| 21 | Low | Quality | Compiler warnings CS9113 (unused `options`) and CS0108 (`Scheme` hid the base member). | Fixed (`SchemeName`); the solution now builds without warnings. | — |
| 22 | Low | Tests | API test fixtures read the data directory from process-wide environment variables lazily; a test that disposed another fixture first could redirect a later host to a shared folder. | Fixtures build their host eagerly. | — |
| 23 | Medium | Safety model | A sent email could still be sent again: if the browser aborted the request after Gmail accepted the message, or saving the draft/application failed afterwards, no consuming receipt was written and the approval stayed reusable. | The Gmail call is a non-cancellable point of no return; receipts, consumption and audit are always recorded; a bookkeeping failure after a successful send still yields a SUCCESS receipt that consumes the approval. | `BrowserAbortAfterGmailAcceptedTheEmail…`, `BookkeepingFailureAfterASuccessfulSend…` |

## 2. Open issues — need a decision (not changed)

| Severity | Issue | Recommendation |
| --- | --- | --- |
| **High** | **Password reset needs only the username** (`/api/auth/reset-password`). Anything that can reach the API (another local user or process) can take over the account; the username is usually your e-mail address. | Require the current password, or issue a one-time recovery code at sign-up and require it for resets. Audit resets. |
| **Medium** | **Google client secret is stored in plain text in the tracked `appsettings.json`** (the Settings page writes it there) and `GET /api/settings/google-oauth` returns it to the browser. *Your working tree currently contains a real client secret in that file.* | Do not commit `appsettings.json` with these values (rotate the secret in Google Cloud Console if it was ever pushed). Store it in `data/` protected with DataProtection, or in user secrets, and return only a "configured" flag. |
| Medium | The LLM API key is stored in plain text in `data/settings.json` (the Google refresh token, by contrast, is DataProtection-encrypted). | Protect it with the existing DataProtection key ring. |
| Medium | `audit.json` grows without bound and every audit event re-serializes, re-validates and backs up the whole file. | Append-only JSON Lines (or monthly files) for the audit trail, or move to SQLite as the roadmap suggests. |
| Low | `CandidateProfileDocument` does not persist `CreatedAt`/`UpdatedAt`, so they reset on every restart. | Add both fields to the document. |
| Low | Deleting a job leaves its resume versions, drafts and **pending approvals**, which can still be executed. | Invalidate approvals and delete or archive dependent items. |
| Low | List/audit caches are updated before the save; if the save fails, memory and disk disagree until restart. | Update the cache only after a successful save. |
| Low | `/api/auth/setup` is not rate limited (sign-up and login are). | Add `RequireRateLimiting("auth")`. |
| Low | Approval expiry and "superseded" invalidations are not audited (they are now logged). | Record `APPROVAL_EXPIRED` / `APPROVAL_SUPERSEDED` audit events. |
| Low | A relative `Storage:DataDirectory` is resolved against the current directory, not the content root (only matters when the exe is started from another folder). | Resolve against `IHostEnvironment.ContentRootPath`. |
| Low | `crypto.randomUUID()` (idempotency keys) only exists in secure contexts; opening the UI via a LAN IP over HTTP would break execution. | Fallback UUID generator. |
| Low | `jobhunter.ps1 stop` kills the processes with `taskkill /F`, so shutdown hooks do not run (the log session has no summary line). | Try a graceful stop first. |

## 3. Improvement suggestions

1. **CI pipeline** — GitHub Actions running `dotnet build`, `dotnet test`, `npm test` and `ng build` on every push. (`dotnet test` alone does not compile the Worker — that is how a stale Worker binary went unnoticed during this review.)
2. **OpenAPI contract** — add `AddOpenApi()` in Development and generate the Angular models/client from it; bug #3 is exactly the kind of drift this prevents.
3. **Typed, validated configuration** — bind `Runtime`, `Google`, `Worker`, `Web` to `IOptions<T>` with `ValidateOnStart()` instead of hand-parsing in `Program.cs`.
4. **`TimeProvider`** for approval expiry, sessions and follow-ups, so time-based rules are unit-testable.
5. **Structured LLM output** — use OpenAI JSON-schema response formats when available and record token usage/cost on `AgentRun`.
6. **Richer health endpoint** — data directory writable, LLM configured, Gmail connected, worker reachable.
7. **End-to-end UI test** (Playwright) for job → prepare → approve → execute, and showing the `X-Correlation-Id` in error banners so a user can quote it.
8. **SQLite** once data grows (jobs, approvals, audit) — keeps the local-first model with transactional writes and indexed queries.

## 4. Logging that was added

See [logging.md](logging.md): a Markdown file provider (`ArvindJobHunter.Logging`) for the API and the worker, one log line per HTTP request with an `X-Correlation-Id` that also appears on audit events, structured log statements in authentication, approvals, execution, agents/tools, LLM, Gmail/OAuth, job import, persistence, data import/export and the worker, plus browser error reporting from the Angular app. Secrets are masked before anything is written.

The logger itself was put through an independent second review, which led to three hardening changes: failed writes are retried on a timer (not only when the next entry arrives), the file is opened only per batch so it can be read or deleted while the app runs, and a username that does not match the account is never logged (people sometimes type their password into that box).

## 5. Verification

* .NET: **169 tests** (was 67) — `dotnet test ArvindJobHunter.slnx`; the whole solution builds with 0 warnings (`dotnet build ArvindJobHunter.slnx`).
* Angular: **24 Vitest tests** (was 13) — `npm test`; `ng build` succeeds.
* Manual end-to-end run of the API and worker against a temporary data directory: happy path (analyze → prepare → approve → execute), wrong password, missing `send` flag, invalid recipient, browser error report, 404, blocked private import URL, denied OAuth consent, unreachable LLM (502 with run id), logout; the Markdown logs were checked for content, structure and the absence of passwords, tokens and keys.
