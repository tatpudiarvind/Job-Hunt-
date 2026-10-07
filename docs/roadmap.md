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

## Next

1. **Job discovery** — saved searches with explicit user-triggered fetches. No background scraping.
2. **Resume preview** — render before/after paragraphs from the generated `.docx` in the approval card; download link for generated copies.
3. **Follow-up email proposals** — agent drafts a follow-up when a reminder is due; each still goes through approval.
4. **Interview prep questions** — LLM-generated question sets from job analysis + verified facts, alongside the existing links.
5. **OpenAI structured outputs** — switch to JSON-schema response format when the API key is present; add token/cost accounting on `AgentRun`.
6. **Hardening** — encrypt JSON at rest with DataProtection, HTTPS dev certificates by default.
7. **Deferred by design** — automatic application submission and browser automation remain disabled until a reviewed per-site adapter with approval exists.
