# Product & business analysis — where Job Hunter should go next

> Perspective: business analyst · 8 October 2026 · based on the current product (branch `Arvind/logImplementation_BugFix_Improvements`) and the job-search tools market as known to the author. Competitor details are qualitative — verify them before external use.

## 1. Executive summary

Job Hunter is a **private, approval-first AI copilot for job applications**. It analyses a job against *verified* facts about you, proposes tailored resume changes and emails, and does nothing externally until you approve and execute it. Few tools combine **truthfulness (verified facts), consent (an approval gate) and privacy (local-first)** — that combination is the product's defensible position.

To grow into a product it has to close three gaps:

1. **Reach** — today only developers can run it (.NET SDK, Node, two dev servers).
2. **Friction at the start** — jobs come in only by URL or paste, and a fresh install is seeded with the author's own profile (name, employer and 14 skills already marked verified), which every other user has to clean up by hand.
3. **Table-stakes features** — pipeline board, ATS keyword scoring, resume preview and export, inbox and calendar awareness, interview practice, analytics.

**Top 5 recommendations**

1. One-click **installer and guided onboarding** — import your CV, the AI proposes facts, you verify them.
2. A **browser extension** that saves any job posting in one click.
3. An **ATS match score plus resume preview/diff and PDF export** inside the approval step.
4. A **pipeline board and follow-up assistant** — reminders, approval-gated follow-up drafts, Gmail/Outlook reply detection.
5. **Outcome analytics** — responses and interviews per resume version and job source, to prove the product works.

**Positioning statement**

> *For professionals who want quality over quantity, Job Hunter is the private AI job-search copilot that tailors every application from facts you have verified and never sends anything without your approval — unlike auto-apply bots and cloud resume tools that guess, spam or keep your data.*

## 2. Current capabilities (as-is)

| Capability | Today | Maturity |
| --- | --- | --- |
| Job intake | Manual entry or URL import (JSON-LD or page text), protected against private-network URLs | MVP |
| Job analysis & match | LLM extracts skills and seniority; match score against verified skills; qualification threshold | MVP |
| Candidate profile | Name, role, company and facts with manual verification; a fresh install is seeded with the author's own profile and 14 pre-verified skills | Basic, single-person |
| Resume tailoring | Small text replacements on a copy of the master `.docx`/`.pdf` | MVP |
| Emails | Cover letter and recruiter email drafts; Gmail draft/send after approval | MVP |
| Approval engine | Hash-bound, expiring, single-use approvals; DEMO / DRY_RUN / LIVE | **Strong** |
| Tracking | 16-state application status, follow-up reminders, dashboard counts | Basic |
| Interview prep | Curated outbound links per skill and gap | Basic |
| Trust & operations | Audit trail, Markdown logs, backup and restore, local data | **Strong** |
| Distribution | Developer setup only | Missing |

## 3. Who it is for

| Persona | Situation | Job to be done | What matters most |
| --- | --- | --- | --- |
| **Priya — senior engineer (primary)** | Employed and selective; 5–15 targeted applications a month | "Make each application clearly fit the role without inventing anything, and keep me organised." | Quality, truthfulness, time saved, privacy |
| **Max — active seeker** | Laid off or graduating; 30+ applications a month | "Get through volume fast without looking like spam, and learn what works." | Speed, tracking, analytics |
| **Sara — privacy-conscious professional** | Healthcare, finance or public sector; wary of uploading a CV to cloud tools | "Use AI without handing my career data to a third party." | Local models, encryption, control |
| **Coach — career coach or outplacement firm (B2B)** | Supports 20–200 candidates | "See my candidates' pipelines, review their materials, report outcomes." | Multi-candidate view, review workflow, reports |

## 4. Market landscape (qualitative)

| Segment | Typical products | What they do well | Where they fall short — Job Hunter's opening |
| --- | --- | --- | --- |
| Job trackers / career CRMs | Huntr, Teal, Careerflow | Browser extension, Kanban pipeline, contacts | Cloud-only data; AI writing is not grounded in verified facts |
| Resume and ATS optimisers | Jobscan, Rezi, Kickresume, Enhancv | ATS keyword score, templates, PDF export | No pipeline; generic AI text can overstate experience |
| Auto-apply and autofill | LazyApply, Simplify | Speed and volume | Spam risk, low quality, platform terms-of-service risk, no consent step |
| Interview practice | Google Interview Warmup, Yoodli, Final Round AI | Practice with feedback | Disconnected from the actual job and your facts |
| Platforms adding AI | LinkedIn, Indeed | Huge reach and native data | Locked to one platform; your data stays with them |

**Trends that favour Job Hunter**

- Recruiters are flooded with AI-generated mass applications, so **authentic, tailored, truthful** applications stand out.
- Privacy expectations and regulation are growing (GDPR in the EU, India's DPDP Act 2023), so **local-first** is a selling point.
- Capable small and local language models make privacy possible without a large quality or cost penalty.
- Job boards increasingly fight scraping and bots, so **user-initiated capture** (an extension) is the sustainable path; blind auto-apply is a dead end.

## 5. Gap analysis against market expectations

| What users expect | Market norm | Job Hunter today | Gap |
| --- | --- | --- | --- |
| Install and start in minutes | Web sign-up or installer | Developer setup | **Critical** |
| Save jobs from any site | One-click browser extension | URL import or paste | **High** |
| Profile from my CV | CV import → structured profile | Facts typed by hand; new installs start with the author's facts | **High** |
| Resume builder and export | Templates, PDF export, preview | Text replacement in a copy, no preview | **High** |
| ATS keyword match | Score plus missing keywords | Skill match against verified facts | Medium |
| Pipeline board | Drag-and-drop Kanban | Status list and dropdown | Medium |
| Inbox and calendar awareness | Detect replies, schedule interviews | None | Medium |
| Contacts and networking | Contacts per company, reminders | Recruiter email drafts only | Medium |
| Interview practice | AI mock interviews with feedback | Curated links | Medium |
| Insights | Funnel and response analytics | Dashboard counts | Medium |
| Mobile access | Mobile app or responsive web | Desktop browser on localhost | Medium |

## 6. Feature opportunities (epics)

User value: H / M / L. Effort for one developer: S (≤ 1 week) · M (2–4 weeks) · L (1–2 months) · XL (> 2 months).

### E1 — Frictionless start (activation)

| Feature | Value | Effort | Notes |
| --- | :---: | :---: | --- |
| **Neutral first run** — an empty profile instead of the author's seeded, pre-verified facts | H | S | Blocks every other user today (engineering review D16). |
| Installer and single-process app (tray icon, auto-start, auto-update) | H | L | Precondition for any non-developer user (engineering review A11). |
| **CV import → AI-proposed facts**, unverified until you confirm | H | M | The biggest activation lever, and it keeps the verified-facts promise. |
| Guided setup wizard (mode, LLM choice including local models, Gmail) | H | S | Replaces the dense Settings page for first-time users. |
| Sample workspace and demo tour | M | S | Shows value before anything is connected. |

### E2 — Job capture & discovery (top of funnel)

| Feature | Value | Effort | Notes |
| --- | :---: | :---: | --- |
| **Browser extension "Save to Job Hunter"** (Chrome/Edge) | H | M | Reuses the existing JSON-LD/page-text parser; user-initiated, so no scraping. |
| Job alerts from compliant sources | M | L | Official APIs or feeds and public company career boards; saved searches with explicit refresh. |
| Duplicate detection and company profile (size, rating, news links) | M | M | A cleaner pipeline and better research. |
| "Fit radar" explanation per job | M | S | Visualise matched and missing skills with evidence links. |

### E3 — Application materials studio

| Feature | Value | Effort | Notes |
| --- | :---: | :---: | --- |
| **ATS match score and missing keywords** | H | S | The analysis data already exists. |
| **Resume preview / side-by-side diff in the approval card** | H | M | Already on the roadmap; makes approvals faster and safer. |
| Resume builder from verified facts, with templates and PDF export | H | L | Goes beyond text replacement; every bullet keeps its evidence link. |
| Achievement bank (STAR stories) linked to facts | M | M | Reused in resumes, cover letters and interviews. |
| Multi-language versions (EN/DE/…) | M | M | Strong for the EU and India; translated from verified facts. |
| Cover-letter variants and tone control | M | S | Pick from 2–3 drafts before approval. |
| LinkedIn headline and About suggestions | L | S | Copy-paste only; no LinkedIn automation. |

### E4 — Pipeline & relationships

| Feature | Value | Effort | Notes |
| --- | :---: | :---: | --- |
| **Kanban pipeline board** | H | M | Drag to change status; uses the existing state machine. |
| **Follow-up assistant** (due reminder → drafted follow-up → approval) | H | M | Turns passive reminders into action; already a roadmap item. |
| Reply detection from Gmail/Outlook (opt-in) | H | L | Suggests status changes (interview invite, rejection); needs read access, a privacy review and Google verification. |
| Calendar integration for interviews | M | M | Create events from invites; prep reminders. |
| Contacts and referrals CRM | M | M | People per company, last contact, referral status. |
| Offer comparison and negotiation notes | M | S | Total-compensation calculator. |

### E5 — Interview readiness

| Feature | Value | Effort | Notes |
| --- | :---: | :---: | --- |
| AI mock interview (text first, voice later) grounded in the job and your facts | H | L | Feedback on structure (STAR), relevance and gaps. |
| Company- and role-specific question bank | M | M | Generated from the job analysis and saved per application. |
| Skill-gap learning plan with progress | M | M | Turns "missing skills" into a plan of courses and practice. |
| Salary negotiation coach | M | S | Scripts based on the offer and market ranges. |

### E6 — Insights & coaching

| Feature | Value | Effort | Notes |
| --- | :---: | :---: | --- |
| **Funnel analytics** (applied → responses → interviews → offers) | H | S | Built from the audit trail and statuses. |
| Resume-version and job-source A/B insights | H | M | "Version B gets twice the responses" — proves the value. |
| Weekly goals, streaks and nudges | M | S | Habit-forming and motivating. |
| Market insights from saved jobs (skill demand, salary ranges) | M | M | Aggregated locally from your own pipeline. |

### E7 — Trust, privacy & safety (keep investing — this is the moat)

| Feature | Value | Effort | Notes |
| --- | :---: | :---: | --- |
| One-click local model "private mode" (for example Ollama) | H | M | No data leaves the machine. |
| Encryption at rest, data export and deletion | H | M | Also needed for compliance. |
| "Why?" panel for every AI suggestion, with evidence | M | S | Builds confidence and speeds up approvals. |
| Outreach rate limits and quality checks | M | S | Protects the user's reputation; anti-spam by design. |

### E8 — Multi-device & collaboration

| Feature | Value | Effort | Notes |
| --- | :---: | :---: | --- |
| Mobile companion (PWA) for approvals and reminders | M | L | Needs a secure relay or encrypted sync. |
| Share an application pack with a mentor for review | M | M | Read-only link or export. |
| Coach / team edition | M (B2B: H) | XL | Multi-candidate dashboards, review workflow, reports. |

### E9 — Ecosystem

| Feature | Value | Effort | Notes |
| --- | :---: | :---: | --- |
| Outlook / Microsoft 365 mail | M | M | Many professionals live in Outlook. |
| Export to Sheets or Notion; webhooks (Zapier/Make) | L | S | For power users and coaches. |
| Notifications (desktop, Telegram, Slack, Teams) | M | S | Reminders and approval requests. |

## 7. Example user stories for the "Now" items

**CV import → proposed facts**
*As a new user, I want to import my CV and get suggested facts, so that I don't have to type my profile by hand.*

- Given a `.docx` or `.pdf` CV, the app proposes facts (skills, roles, employers, education, certifications) marked **unverified**, each linked to the CV line it came from.
- Unverified facts are never used for tailoring.
- I can verify, edit or reject facts individually or in bulk.
- Import works with the Demo and local-model providers — no cloud needed.

**Save a job from the browser**
*As a job seeker browsing a job board, I want to click "Save to Job Hunter", so that the posting lands in my pipeline without copy-paste.*

- The extension reads the active tab only after I click, extracting title, company, location and description (JSON-LD first, page text as fallback).
- I see an editable preview before saving; duplicates are detected by URL or company + title.
- The extension talks only to the local app, using a one-time pairing token.

**ATS match score**
*As a candidate, I want to see how well my tailored resume covers the job's keywords, so that I can close gaps before approving.*

- A 0–100 score with matched and missing keywords, before and after the proposed changes.
- Missing keywords not backed by verified facts are flagged "only add if true".
- The score appears on the job page and in the approval card.

## 8. Recommended roadmap

| Horizon | Theme | Deliverables | Success signal |
| --- | --- | --- | --- |
| **Now (0–3 months)** | *Usable by anyone* | Neutral first run · installer and single-process app · setup wizard · CV → facts · browser extension · ATS score · resume diff preview · Kanban · funnel analytics v1 · Gmail reconnect handling | A new user sends a first tailored application in under 15 minutes |
| **Next (3–6 months)** | *Close the loop* | Follow-up assistant · reply detection (opt-in) · calendar · contacts CRM · resume builder + PDF · multi-language · local-model private mode · Outlook | Response rate tracked per resume version; steady weekly use |
| **Later (6–12 months)** | *Scale and differentiate* | AI mock interviews (text → voice) · market insights · mobile companion · encrypted sync · coach/team edition · integrations | First paying users and a first B2B pilot |

Every item keeps the product rules: **verified facts only, nothing external without approval, private by default.**

## 9. How to measure success

| Level | Metric |
| --- | --- |
| **North star** | Interviews per 10 applications sent through Job Hunter |
| Activation | Time to first tailored application · % of users with ≥ 10 verified facts · % connecting a mail provider |
| Engagement | Weekly active users · jobs saved per week · approvals executed per week |
| Outcomes | Response, interview and offer rates against the user's own baseline · uplift per resume version |
| Trust | % of AI suggestions approved without edits · zero unapproved external actions · blocked unsafe actions |
| Retention and satisfaction | Week-4 and week-12 retention · NPS |

The audit trail can serve as a local event stream for these metrics; any telemetry that leaves the device must be **opt-in and anonymised**.

## 10. Business model & go-to-market options

| Option | How it works | Fit |
| --- | --- | --- |
| **Open core** (recommended start) | MIT-licensed local core (already open source) plus paid Pro features | Builds trust; matches the privacy story |
| Pro subscription | Hosted models (no own API key), ATS scoring, mock interviews, analytics, sync | Recurring revenue from active seekers |
| One-time desktop licence | Pay once; bring your own key or a local model | Appeals to privacy-minded professionals |
| B2B seats | Outplacement firms, university career services, bootcamps | Higher contract value; needs the coach dashboard |
| Partnerships | Human resume reviewers; course providers for skill gaps (affiliate) | Extra revenue and extra value |

**Go-to-market:** start with a niche where the founder has credibility — for example senior software and health-tech professionals in India and Germany — through developer communities, LinkedIn content and the open-source repository, and use early users' outcome data as proof.

## 11. Risks & compliance

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Job-board terms of service (scraping, auto-apply) | Account bans, legal exposure | User-initiated capture only; no automated submission; compliant sources for alerts |
| Google verification for Gmail scopes | Gmail features can't be distributed; weekly disconnects while the OAuth client is in Testing | Prefer `gmail.send`; plan verification and the security assessment; add Outlook |
| Privacy law (GDPR, DPDP Act) | Fines and loss of trust | Local-first by default; consent and data-subject rights for any cloud feature; data processing agreements |
| AI regulation (for example the EU AI Act) | Obligations if positioned as hiring-decision support | Stay on the candidate side; label AI-generated content; keep human approval |
| LLM cost and quality | Margins and poor output | Model routing, caching, local models, evaluation sets, verified-facts grounding |
| Reputation (AI spam to recruiters) | Users and product get blamed | Approval gate, rate limits, quality checks — make "quality over quantity" the brand |
| Single-developer capacity | Slow delivery | Focus on E1–E3 first; CI and tests keep speed safe |

## 12. Next steps

1. Interview 10–15 target users (personas 1–3) to validate the top five recommendations; prototype the browser extension and the ATS score first.
2. Define a public-beta scope: installer, onboarding, capture, tailoring with preview, approvals, pipeline, analytics.
3. Add opt-in product analytics and an in-app feedback button.
4. Run competitor teardowns (sign up, record the flows) to sharpen the gap analysis — the landscape above is qualitative.
5. Write a lightweight spec for each "Now" item: problem, user stories, acceptance criteria, success metric.
