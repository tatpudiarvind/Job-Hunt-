# JSON Storage

## Layout

`Storage:DataDirectory` defaults to `./data`. The store creates it as needed. Phase 2 files are `candidate.json`, `preferences.json`, `jobs.json`, `recruiters.json`, `applications.json`, `resumes.json`, `approvals.json`, `agent-runs.json`, `audit.json`, `notifications.json`, `settings.json`, and `scheduler.json`.

Each document is a UTF-8 JSON envelope:

```json
{
  "schemaVersion": 1,
  "lastUpdated": "2026-09-22T12:00:00+00:00",
  "data": {}
}
```

## Write protocol

A per-file `SemaphoreSlim` serializes writes inside this process. The store serializes an envelope to `*.tmp.json`, flushes it, deserializes it for validation, creates a bounded backup where configured, and moves the temporary file over the destination. Readers use cached state; JSON is never reread per request.

## Recovery

An unreadable file is renamed to `*.corrupt.<UTC timestamp>.json`. The latest valid `*.bak` is then tried. If unavailable, the store returns an empty state and emits a `PERSISTENCE_RECOVERY` audit event when audit is available. A future schema version is rejected safely; migration services own supported upgrades.

## Limits

This is single-user, local persistence. It does not support cross-process concurrent writers or transactional changes across multiple files. A database migration remains appropriate when multi-user access, high write volume, query complexity, or distributed execution becomes necessary.
