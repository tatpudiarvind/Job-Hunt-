# Browser Auto-Apply (Approval-Gated)

The Worker drives a **visible** Chromium browser with a persistent profile so you stay signed in to job sites between runs. Nothing is submitted without your approval.

## Setup

1. Build the Worker once, then install the Playwright browser:
   `pwsh src/ArvindJobHunter.Worker/bin/Debug/net10.0/playwright.ps1 install chromium`
2. Start the Worker (`http://127.0.0.1:5310`). The profile lives under the Worker's `Browser/profile` folder.
3. In **Settings → Browser auto-apply sites**, enable the sites you want (`company`, `linkedin`, `indeed`) and set a daily cap. LinkedIn and Indeed are off by default.

## Flow

1. **Applications → Fill application in browser.** The Worker opens the job URL, fills fields from your verified profile facts and preferences, uploads the selected resume, and **stops before the final Submit**. A screenshot is saved.
2. If the form asks questions you haven't answered, they appear as inputs. Saving answers creates a new package and invalidates any earlier approval.
3. When all fields are answered, a `SUBMIT_APPLICATION` approval is created. It is bound to a hash of the URL, adapter, resume file hash, cover text and every field value.
4. Approve and execute it on the **Approvals** page. The executor re-checks the approval, execution mode, site switch, daily cap and resume file hash, then asks the Worker to click Submit in the same browser session.
5. On success the application moves to **APPLIED** and the confirmation text and screenshot are recorded.

In DEMO and DRY_RUN modes the final click is simulated.

## Resume selection

The order is: the tailored resume generated for the job, then a role mapping (`RoleResumePaths`), then a keyword match from `ResumeLibrary`, then the country or default master resume. A PDF next to the DOCX is used if one exists.

## Rules

- Login, MFA and CAPTCHA are always done by you in the visible window. The Worker never bypasses them. Preparation stops and reports the blocker.
- There is no background scraping. Browser activity only happens when you click a button.
- Site selectors live in `src/ArvindJobHunter.Worker/Browser/sites.json`. They may need tuning when sites change their pages.
- If the Worker restarts, open sessions are lost. Prepare the application again before submitting.
