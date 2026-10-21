# ArvindJobHunter Documentation Index

## Purpose

This directory contains the official product, architecture, operational, and integration documentation for ArvindJobHunter.

The goals of this documentation set are to:

- describe the product clearly and formally
- support local development and operations
- document architecture and implementation decisions
- guide users through external integrations such as Google OAuth and Gmail
- provide a stable source of truth for the in-application Help center

---

## Documentation Structure

## 1. Product and Platform Overview

- [architecture.md](architecture.md)  
  High-level system architecture, application structure, and major runtime components.

- [roadmap.md](roadmap.md)  
  Product roadmap, completed milestones, and future direction.

- [phase2.md](phase2.md)  
  Historical implementation plan and milestone context for Phase 2 work.

---

## 2. Local Development and Operations

- [local-development.md](local-development.md)  
  Local development prerequisites, build commands, run commands, and environment expectations.

- [STARTUP_GUIDE.md](STARTUP_GUIDE.md)  
  Unified startup workflow for backend and frontend, including troubleshooting.

- [PRODUCTION_DEPLOYMENT_GUIDE.md](PRODUCTION_DEPLOYMENT_GUIDE.md)  
  Production deployment preparation, configuration, and operational guidance.

---

## 3. AI, Runtime, and Storage

- [agent-runtime.md](agent-runtime.md)  
  Agent orchestration behavior, execution model, and runtime expectations.

- [approval-engine.md](approval-engine.md)  
  Approval-first execution model, control points, and action governance.

- [llm-provider.md](llm-provider.md)  
  LLM provider modes, configuration model, and runtime boundaries.

- [json-storage.md](json-storage.md)  
  JSON persistence model, storage layout, and file-based data behavior.

---

## 4. Google OAuth and Gmail Integration

### Primary Guide

- [GOOGLE_OAUTH_CONFIGURATION_GUIDE.md](GOOGLE_OAUTH_CONFIGURATION_GUIDE.md)  
  Primary end-to-end guide for Google OAuth and Gmail configuration, including application UI setup, Google Auth Platform setup, setup order, troubleshooting context, and FAQ.

### Supporting References

- [GOOGLE_OAUTH_USER_FLOW.md](GOOGLE_OAUTH_USER_FLOW.md)  
  User-oriented explanation of the Google account connection flow and administrative setup background.

- [GMAIL_GOOGLE_CLOUD_SETUP.md](GMAIL_GOOGLE_CLOUD_SETUP.md)  
  Supporting Google Cloud Console reference with project, API, consent, and credential setup details.

- [gmail-integration.md](gmail-integration.md)  
  General Gmail integration behavior within the application.

- [GMAIL_INTEGRATION_LOCAL_TESTING.md](GMAIL_INTEGRATION_LOCAL_TESTING.md)  
  Local development testing workflow for Gmail integration.

- [GMAIL_OAUTH_401_FIX.md](GMAIL_OAUTH_401_FIX.md)  
  Focused troubleshooting reference for invalid_client / 401 configuration issues.

- [OAUTH_IMPLEMENTATION_SUMMARY.md](OAUTH_IMPLEMENTATION_SUMMARY.md)  
  Historical implementation summary and change record for the OAuth feature.

- [BROWSER_AUTO_APPLY.md](BROWSER_AUTO_APPLY.md)  
  Visible-browser form filling, site caps, and approval-gated final submission.

---

## 5. Recommended Reading Paths

### For End Users
Start with:
1. `GOOGLE_OAUTH_USER_FLOW.md`
2. `STARTUP_GUIDE.md`
3. relevant Help content inside the application

### For Developers
Start with:
1. `architecture.md`
2. `local-development.md`
3. `agent-runtime.md`
4. `approval-engine.md`
5. `json-storage.md`

### For OAuth / Gmail Setup
Start with:
1. `GOOGLE_OAUTH_CONFIGURATION_GUIDE.md` as the primary guide
2. `GMAIL_GOOGLE_CLOUD_SETUP.md` for Google Cloud Console reference details
3. `GOOGLE_OAUTH_USER_FLOW.md` for user/admin connection flow context
4. `GMAIL_INTEGRATION_LOCAL_TESTING.md` for local validation
5. `GMAIL_OAUTH_401_FIX.md` for focused invalid_client troubleshooting

### For Production Readiness
Start with:
1. `PRODUCTION_DEPLOYMENT_GUIDE.md`
2. `approval-engine.md`
3. `llm-provider.md`
4. `roadmap.md`

---

## 6. Documentation Standards

When updating or adding documentation:

- use clear section headings
- keep tone formal and product-oriented
- separate setup, operations, and troubleshooting concerns
- prefer checklists and step-by-step instructions where appropriate
- keep implementation-specific troubleshooting in FAQ or dedicated troubleshooting sections
- ensure user-facing behavior is consistent with the actual UI and API

---

## 7. Relationship to the Help Center

The in-application Help center is derived from the documented behavior in this folder. Documentation should therefore be treated as the authoritative source for:

- setup guidance
- operational instructions
- troubleshooting answers
- product behavior explanations

When a feature changes, the corresponding document should be updated so the Help center remains accurate.
