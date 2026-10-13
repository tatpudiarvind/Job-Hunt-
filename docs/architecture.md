# ArvindJobHunter Architecture Guide

## 1. Purpose

This document describes the high-level architecture of ArvindJobHunter, including its major runtime boundaries, project responsibilities, workflow controls, persistence model, and security constraints.

It is intended to serve as the architectural reference for developers and maintainers.

---

## 2. Architectural Principles

The system is designed around the following principles:

- verified candidate facts are authoritative and separate from generated output
- external content is treated as untrusted data
- tailored resume output is produced from copies, never by editing the master source in place
- preview, approval, execution, and audit are distinct stages
- demo-safe behavior is the default development posture
- operations should be observable, restartable, and idempotent where practical

---

## 3. Logical System Structure

```text
Angular 22 Web
    -> ASP.NET Core API
        -> Application use cases and authorization
            -> Domain entities and invariants
            -> Contracts (API/tool schemas)
            -> Infrastructure (JSON persistence, providers, secrets, audit)
            -> Agents (orchestration only through explicit tools)
        -> Local JSON files

Windows Worker (separate process)
    <- authenticated localhost HTTP command channel
    -> ResumeAutomation (Word Interop and local file policy)
    -> Playwright adapters where permitted
```

The API is the central orchestration boundary. The Angular frontend interacts with the API, while worker-based operations remain isolated behind a loopback boundary.

---

## 4. Solution and Project Responsibilities

| Project | Responsibility |
| --- | --- |
| `ArvindJobHunter.Domain` | Domain entities, value objects, status transitions, and invariants with no infrastructure coupling. |
| `ArvindJobHunter.Contracts` | API contracts, DTOs, request/response shapes, and cross-boundary schemas. |
| `ArvindJobHunter.Application` | Use cases, validation, ports, approval checks, and authorization-aware workflow coordination. |
| `ArvindJobHunter.Infrastructure` | JSON persistence, provider adapters, secret handling, and audit/telemetry implementations. |
| `ArvindJobHunter.Agents` | Agent prompts, structured outputs, tool-selection workflows, and safety-bounded orchestration. |
| `ArvindJobHunter.Api` | HTTP authentication, endpoint composition, middleware, rate limiting, and public API behavior. |
| `ArvindJobHunter.Worker` | Local worker command receiver and isolated execution boundary for worker-backed operations. |
| `ArvindJobHunter.ResumeAutomation` | Resume editing, validation, and working-copy protections. |
| `ArvindJobHunter.Web` | Angular user interface and feature navigation boundary. |

Tests mirror the major boundaries across domain, application, API, agents, and integration layers.

---

## 5. Domain and Workflow Model

Primary business areas include:

- candidate profile and verified facts
- jobs and job analysis
- applications and application tracking
- tailored resume and email artifacts
- approvals and execution receipts
- integration connections and audit records

Representative workflow states include:

- Job: `DISCOVERED -> ANALYZED -> QUALIFIED`
- Application execution: `QUALIFIED -> PREPARED -> WAITING_FOR_APPROVAL -> APPROVED -> EXECUTING -> SUCCESS | FAILED | UNKNOWN`

Workflow transitions are validated and audited. Any approval-sensitive change to payload, recipient, attachment, or generated content invalidates the prior approval and returns the flow to an approval-required state.

---

## 6. API Boundary

The API is organized around product use cases rather than provider-specific implementations.

Key areas include:

- candidate profile management
- jobs, analysis, and matching
- resumes and resume changes
- applications and follow-up workflows
- approvals and execution
- email drafts and send operations
- dashboard, activity, audit, and read-model endpoints
- integrations such as Google OAuth and Gmail

The integrations surface is intentionally designed so raw secrets and tokens are not exposed through ordinary read endpoints.

---

## 7. Agent and Tool Boundaries

Agents are bounded by capability area, such as:

- job discovery
- job analysis and match
- recruiter discovery
- resume tailoring
- cover letter generation
- application preparation
- email preparation
- follow-up assistance
- market intelligence

Agents do not directly invoke infrastructure. They operate through explicit tools and structured outputs controlled by the orchestrator.

High-risk actions require an approval context and must satisfy action, target, content, and user validation constraints before execution.

---

## 8. Persistence and Local Storage

The current implementation uses JSON-based local persistence rather than a database dependency.

Characteristics:

- versioned JSON document storage
- local data directory under configured storage settings
- backup and corruption-recovery support owned by infrastructure
- application/domain layers remain unaware of file and serialization details

Sensitive integration data such as refresh tokens and API keys must be protected using the supported secret-protection mechanisms rather than plain ordinary storage.

Related document:

- `docs/json-storage.md`

---

## 9. Resume Automation Boundary

Resume automation is intentionally constrained.

Key rules:

- the master resume is never edited in place
- all tailoring occurs on working copies
- worker-side processing validates source/destination safety rules
- generated changes remain bounded and evidence-driven
- change reporting records what was modified and why

This protects the source resume while preserving traceability over generated output.

---

## 10. Security Architecture

Security expectations include:

- authenticated access and resource-level authorization
- DTO and URL validation
- upload scanning and size restrictions
- rate limiting for sensitive operations
- secret redaction in logs
- strict separation between untrusted external data and trusted instructions
- approval and audit around externally impactful side effects

Demo mode further reduces risk by preventing real external-provider actions.

---

## 11. Configuration Model

Configuration may be layered through:

- `appsettings.json`
- environment-specific appsettings
- development user secrets
- environment variables
- production secret managers

Secret values must not be committed to source control.

Examples of configuration areas include:

- LLM provider configuration
- Google OAuth credentials
- worker endpoint and shared secret
- storage configuration
- future provider and deployment settings

---

## 12. Local Development Context

Local development expects:

- .NET 10 SDK
- Node.js
- Angular-compatible frontend tooling
- no SQL Server or database service for the current JSON-backed workflow

Microsoft Word is not required for the current local development path unless a later worker scenario explicitly depends on it.

Related document:

- `docs/local-development.md`

---

## 13. Testing Strategy

The architecture supports layered testing across:

- domain invariants and state transitions
- approval and payload hashing behavior
- API authorization and validation
- integration behavior with isolated temporary JSON storage
- end-to-end workflow checks for approval and execution boundaries
- security-oriented cases such as SSRF, hostile content, and secret redaction

---

## 14. Governance and Product Decisions

Some architectural areas require explicit owner decisions before production hardening or later implementation phases. These include:

1. authentication strategy
2. local data-directory and backup policy
3. worker transport credential model
4. resume source and working-directory policy
5. approved provider integrations and sources
6. Google OAuth and Gmail consent/scopes
7. LLM deployment and budget constraints
8. follow-up automation policy
9. retention and deletion policy
10. user-sensitive default answers and compliance behavior

---

## 15. Summary

ArvindJobHunter is designed as a local-first, approval-first system with clear boundaries between UI, API, orchestration, storage, integrations, and worker-backed execution. Its architecture prioritizes safety, observability, and controlled side effects while supporting structured automation across the job-search workflow.
