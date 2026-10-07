# ADR-002: JSON Persistence for Local Phase 2

## Status

Accepted.

## Context

The first deployment is a single-user Windows application. Phase 2 must run without SQL Server, Docker, or any database service.

## Decision

Use in-memory state backed by versioned, human-readable JSON files. Infrastructure owns file I/O, atomic replacement, locking, backups, corruption recovery, and schema migration. Application and Domain depend on interfaces only.

## Consequences

The application starts without infrastructure, supports offline local use, and allows direct inspection/backups of data. It is intentionally limited to one process and user. Introducing database persistence later requires a new Infrastructure implementation rather than an Application or Domain rewrite.
