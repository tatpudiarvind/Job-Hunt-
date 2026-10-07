# ARVIND AI JOB HUNTER - Phase 1 Architecture

## 1. Scope and principles

Phase 1 establishes boundaries and a buildable solution skeleton. It does not implement real job providers, OpenAI calls, Gmail, browser automation, SQL persistence, authentication, or Word automation. Every external side effect remains behind an abstraction and requires an approval record in later phases.

Core principles:

- Verified candidate facts are authoritative and separate from generated content.
- External content is untrusted data, never instructions.
- Read-only master resume; every tailored document is a copy.
- Preview, approval, execution, and audit are separate steps.
- Demo mode is the default for development and cannot reach real providers.
- Operations are idempotent, restartable, correlated, and observable.

## 2. Logical architecture

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

The worker uses authenticated loopback HTTP because it is the smallest operational surface for a single Windows workstation. It must bind only to loopback, use a per-installation secret or local certificate, reject non-loopback requests, apply request limits, and report execution as `SUCCESS`, `FAILED`, or `UNKNOWN` with an idempotency key. The API never loads Microsoft Word Interop.

## 3. Projects and responsibilities

| Project | Responsibility |
| --- | --- |
| `ArvindJobHunter.Domain` | Entities, value objects, enums, invariants, status transitions. No infrastructure dependencies. |
| `ArvindJobHunter.Contracts` | Versioned API, tool, worker request/response DTOs and discriminated result shapes. |
| `ArvindJobHunter.Application` | Use cases, ports, approval checks, validation, authorization policies, transaction boundaries. |
| `ArvindJobHunter.Infrastructure` | Versioned JSON persistence, provider adapters, protected-secret access, audit and telemetry implementations. |
| `ArvindJobHunter.Agents` | Agent prompts, structured output schemas, tool selection, workflows, safety gates. Agents cannot call infrastructure directly. |
| `ArvindJobHunter.Api` | HTTP authentication, endpoint composition, middleware, rate limiting, problem details, correlation IDs. |
| `ArvindJobHunter.Worker` | Windows-hosted command receiver, job leasing, retry/idempotency handling, health reporting. |
| `ArvindJobHunter.ResumeAutomation` | Word document copy/read/edit/validate operations; master resume protection. |
| `ArvindJobHunter.Web` | Angular 22 application boundary and feature navigation. |

Tests mirror the important boundaries: domain, application, API, agents, and integration.

## 4. Domain model

Candidate aggregate: `CandidateProfile`, `CandidateSkill`, `CandidateExperience`, `CandidateEducation`, `CandidateProject`, `CandidatePreference`, `CandidateDocument`.

Job aggregate: `Job`, `JobSource`, `JobRequirement`, `JobAnalysis`, `JobMatch`.

Recruiter aggregate: `Recruiter`, `RecruiterEmail`.

Application aggregate: `Application`, `ApplicationAnswer`, `Interview`, `FollowUp`.

Content and execution: `ResumeTemplate`, `ResumeVersion`, `ResumeChange`, `CoverLetter`, `EmailDraft`, `EmailMessage`, `ApprovalRequest`, `ApprovalAction`, `AgentRun`, `AgentToolCall`, `AuditLog`, `IntegrationConnection`.

All persisted records use GUID identifiers, UTC timestamps, optimistic concurrency tokens, and explicit status enums. Candidate facts require provenance and verification state. Generated text is immutable once approved; a content hash is stored with each approval.

## 5. API boundary

The API is organized by resource/use case, not by provider:

- `/api/candidate-profile` for verified profile facts and documents.
- `/api/jobs`, `/api/jobs/{id}/analysis`, `/api/jobs/{id}/match` for discovery and analysis.
- `/api/recruiters` for discovered contacts and verification state.
- `/api/resumes` and `/api/resumes/{id}/changes` for versions and reports.
- `/api/applications` for preparation and tracking.
- `/api/approvals` for action-specific approval lifecycle.
- `/api/emails` for drafts, metadata, and authorized send operations.
- `/api/dashboard`, `/api/market-intelligence`, `/api/agent-runs`, `/api/audit-logs` for read models.
- `/api/integrations` for OAuth connection lifecycle, never raw tokens.

External actions use command endpoints with idempotency keys and return an execution receipt. They cannot be invoked by read endpoints or generic agent endpoints.

## 6. Agent and tool boundaries

Agents are bounded by capability: Job Discovery, Job Analysis, Job Match, Recruiter Discovery, Resume Tailoring, Cover Letter, Application Preparation, Email, Follow-up, and Market Intelligence. The orchestrator owns state and sequencing; agents only propose structured outputs.

Tools are explicit, schema-validated, authorized, and audited: `SearchJobsTool`, `AnalyzeJobTool`, `MatchJobTool`, `FindRecruiterTool`, `VerifyEmailTool`, `ReadResumeTool`, `CustomizeResumeTool`, `GenerateCoverLetterTool`, `PrepareApplicationTool`, `CreateGmailDraftTool`, `SendGmailEmailTool`, `SubmitApplicationTool`, `TrackApplicationTool`, `ScheduleFollowUpTool`, and `AnalyzeMarketTool`.

High-risk tools (`SendGmailEmailTool`, `SubmitApplicationTool`, upload, and follow-up send) require a matching, unexpired approval context. The tool verifies the action type, target, content hash, resume version, application payload hash, user, and demo/real provider mode.

## 7. Workflow state machines

Job discovery: `DISCOVERED -> ANALYZED -> QUALIFIED`.

Application: `QUALIFIED -> PREPARED -> WAITING_FOR_APPROVAL -> APPROVED -> EXECUTING -> SUCCESS | FAILED | UNKNOWN`.

Application tracking uses `DISCOVERED`, `MATCHED`, `SHORTLISTED`, `RESUME_PREPARED`, `AWAITING_APPROVAL`, `APPROVED`, `APPLIED`, `RECRUITER_CONTACTED`, `RECRUITER_REPLIED`, `INTERVIEW`, `TECHNICAL_INTERVIEW`, `HR_INTERVIEW`, `OFFER`, `REJECTED`, `WITHDRAWN`, and `CLOSED`.

Every transition is validated, persisted with an audit event, and safe to replay. Approval is action-specific. Any change to recipient, body, attachment, resume version, or application payload invalidates the approval and returns the workflow to `WAITING_FOR_APPROVAL`.

## 8. Local persistence

Phase 2 has no database dependency. In-memory state is loaded from versioned JSON files during startup and mutations persist the changed aggregate atomically. Infrastructure owns JSON envelopes, in-process write locks, backups, corrupt-file quarantine, and future schema migration. Application and Domain do not know file paths or JSON details.

Sensitive integration data is outside ordinary JSON files; OAuth refresh tokens and API keys are protected with Windows DPAPI or a configured secret manager. Audit records store actor, action, target, outcome, correlation ID, timestamp, and redacted metadata.

## 9. Resume automation

The API requests a worker operation with the master path, working-copy destination, approved change set, and content hash. The worker verifies that source and destination differ, source is configured as read-only by policy, destination is inside an allowed working directory, and the operation is idempotent. Word Interop runs only in the worker process, closes documents and the application in `finally` paths, and validates that the source hash is unchanged and the output opens successfully. The master file is never saved in place.

Tailoring is constrained to small edits supported by verified facts. A change report records old/new text, section, reason, protected-field checks, unsupported-claim checks, and estimated modification percentage. No change is forced when evidence is insufficient.

## 10. Security architecture

Use authenticated users and resource authorization. Validate DTOs and URLs, enforce SSRF allowlists and DNS/IP checks, scan and size-limit uploads, redact secrets and personal data in logs, and apply rate limits per user and integration. Treat job descriptions, recruiter pages, emails, and uploaded documents as hostile data. Prompt templates clearly delimit data and state that data cannot override system instructions. Structured outputs are validated against schemas and candidate facts before persistence.

Approval and audit are mandatory around side effects. Demo mode uses fake providers only, has a hard no-network policy for external integrations, and is covered by tests.

## 11. Configuration and local development

Configuration layers: `appsettings.json`, environment-specific appsettings, user secrets for development, environment variables for deployment, and an external secret manager in production. Never commit tokens or the master resume path if it is user-specific.

Required future variables include `ConnectionStrings__SqlServer`, `Auth__Authority`, `Auth__ClientId`, `OpenAI__Endpoint`, `OpenAI__ApiKey`, `Google__ClientId`, `Google__ClientSecret`, `Worker__BaseUrl`, `Worker__SharedSecret`, `Resume__MasterPath`, `Resume__WorkingDirectory`, `Providers__Mode`, and `Providers__AllowedDomains`. Secret values must be supplied through a secret store, not checked into source.

Local prerequisites: .NET 10 SDK, Node.js, and Angular CLI 22. Microsoft Word is not required for Phase 2; it is only needed for the later Windows worker phase. No SQL Server, Docker, or database service is required. The application persists local state under `Storage:DataDirectory` using JSON files.

## 12. Demo mode

Demo mode is explicit configuration and the default test mode. It supplies deterministic sample jobs, recruiters, email drafts, fake resume copies, and fake application portals. It cannot send Gmail, call OpenAI, browse real portals, or submit a real application. Every demo receipt is labeled `DEMO` and every test asserts no real provider was reached.

## 13. Testing strategy

Unit tests cover invariants, scoring, approval hashes, protected resume fields, and state transitions. Agent tests use deterministic structured fixtures and prompt-injection cases. API tests cover authorization, validation, idempotency, and problem details. Integration tests use isolated temporary JSON directories. Resume tests use a fixture DOCX and verify source hash, formatting-sensitive markers, output existence, and cleanup. End-to-end tests cover approval UI to demo execution. Security tests cover SSRF, upload validation, secret redaction, hostile external content, and demo isolation.

## 14. Decisions requiring user approval

1. Authentication provider and whether the first deployment is single-user local-only.
2. Local data-directory location and backup retention.
3. Worker transport credential: shared secret or local certificate.
4. Master resume path and working directory.
5. Real provider subscriptions and permitted job sources.
6. Google OAuth consent configuration and Gmail scopes.
7. OpenAI deployment/model, retention settings, and budget limits.
8. Whether any follow-up policy may be automated after explicit opt-in.
9. Data retention, backup, and deletion policy.
10. Countries, work-authorization answers, and other sensitive application defaults.

Phase 2 must not begin until these decisions are confirmed where they affect implementation.
