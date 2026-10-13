# Roadmap

## Done (Phase 3 — approval-first core)

- Domain aggregates and validated application state machine
- Payload-bound approvals with TTL, invalidation, one-shot consumption
- Single execution gateway with guard, idempotency, receipts, audit
- Agent tool registry, orchestrator, Demo + OpenAI LLM providers
- JSON persistence with recovery, runtime settings, Google OAuth, Gmail adapter
- Open XML resume automation (master read-only) and loopback worker
- Rewritten API and Angular UI; integration tests over the full pipeline

## Done (Phase 4 — gaps and roadmap items)

- Isolated API test data directory; stale `data/` excluded from build output
- DPAPI-protected DataProtection keys (Windows); optional Worker proxy for `IResumeDocumentService` via `Worker:BaseUrl`/`Worker:SharedSecret`
- Angular vitest unit tests (`npm test`)
- Import job from URL (JSON-LD `JobPosting` → fallback text; SSRF guard; preview only, user saves)
- Follow-up reminders on applications with dashboard surfacing (no auto emails)
- Interview prep: outbound-only resource links (YouTube/docs/practice sites) per job, gaps prioritized
- Data export/import (zip; secrets excluded; validated; pre-import backup; caches reloaded)

## Done (Phase 5 — observability and hardening)

- Markdown file logging for API and worker (daily files, size roll-over, retention, collapsible exceptions, secret masking), per-request log lines with `X-Correlation-Id`, correlation ids on audit events, browser error reporting — see [logging.md](logging.md)
- Bug fixes from the code review ([code-review-findings.md](code-review-findings.md)): Google OAuth callback state, master-resume upload wiping LLM settings, email approval request from the UI, DRY_RUN writing files, double execution race, unaudited executor failures, stuck RUNNING agent runs, ignored `Runtime:*` configuration, SSRF via redirects/DNS, API key in exports and in the settings response

## Next

1. **Password reset hardening** — the reset currently needs only the username; require the current password or a one-time recovery code (see the code review).
2. **Job discovery** — saved searches with explicit user-triggered fetches. No background scraping.
3. **Resume preview** — render before/after paragraphs from the generated `.docx` in the approval card; download link for generated copies.
4. **Follow-up email proposals** — agent drafts a follow-up when a reminder is due; each still goes through approval.
5. **Interview prep questions** — LLM-generated question sets from job analysis + verified facts, alongside the existing links.
6. **OpenAI structured outputs** — switch to JSON-schema response format when the API key is present; add token/cost accounting on `AgentRun`.
7. **Hardening** — encrypt JSON at rest (at least the LLM API key and Google client secret) with DataProtection, HTTPS dev certificates by default.
8. **Deferred by design** — automatic application submission and browser automation remain disabled until a reviewed per-site adapter with approval exists.
