# Approval Engine

Approvals are persisted in `approvals.json` and bind a user, action type, target, payload hash, expiration, and status. Approval is single-use and action-specific. A changed payload invalidates existing approval. Phase 2 only permits demo execution; LIVE is rejected unconditionally by the external action guard.
