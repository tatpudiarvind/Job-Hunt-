# ArvindJobHunter – First-Time User Guide

This guide walks you through using ArvindJobHunter for the first time, from starting the app to sending your first application and follow-up.

**The one rule to remember:** the app never sends an email or submits an application on its own. It prepares everything, then waits for you to **approve** it and then **execute** it on the **Approvals** page.

---

## 1. Before you start

You need:

- **.NET 10 SDK** and **Node.js with npm** (see [STARTUP_GUIDE.md](STARTUP_GUIDE.md)).
- Your **master resume** as a Word file (`.docx`). The app keeps its layout and fonts when it tailors it.
- *(Optional)* A Google account, if you want to send emails through Gmail.
- *(Optional)* The **Worker** and its Chromium browser, if you want the app to fill in online application forms (see section 9).

## 2. Start the app

From the repository folder, run one of:

```
jobhunter-start.bat
```
```
node start.js
```

Then open **http://localhost:4200** in your browser. The API runs at http://localhost:5228.

Other commands: `.\jobhunter.ps1 stop`, `.\jobhunter.ps1 restart`, `.\jobhunter.ps1 status`.

## 3. Create your account

1. The first time, you are sent to **Sign up**. Choose a username and a strong password.
2. Sign in. Your data is stored locally on this machine, in the app's data folder; it is not uploaded anywhere.
3. If you forget your password, use **Forgot password** on the sign-in page.

## 4. Set up the app (Settings)

Open **Settings** from the menu and work through these cards:

| Card | What to do |
|---|---|
| **Execution mode** | Start in **DEMO**. See the table below. |
| **Job search preferences** | Target roles, countries, locations, salary and whether you need visa sponsorship. Click **Save job preferences**. |
| **Master resume** | Click **Upload resume** and choose your `.docx`. All tailored resumes are based on this file. |
| **Google** | *(Optional)* Click **Connect Google** to send emails through Gmail. See [GOOGLE_OAUTH_CONFIGURATION_GUIDE.md](GOOGLE_OAUTH_CONFIGURATION_GUIDE.md). |
| **Browser auto-apply sites** | *(Optional)* Turn sites on or off and set a daily limit for each. Click **Save sites**. |
| **Backup & restore** | Click **Download export** now and then to keep a copy of your data. |

**Execution modes**

| Mode | What happens when you execute an approved action |
|---|---|
| **DEMO** | Nothing real happens. Use this to learn the app. |
| **DRY_RUN** | Everything is checked and prepared, but nothing is sent or submitted. |
| **LIVE** | Emails are really sent and applications are really submitted. |

Stay in DEMO or DRY_RUN until you are comfortable with the flow.

## 5. Build your profile

Open **Profile**. The app only uses facts about you that you have **verified**, so it never makes up skills or experience.

1. Under **Add a fact**, add your skills, jobs, education, certifications and so on, with where the fact comes from (for example "Resume, 2024"), and click **Add as unverified**.
2. Click **Mark verified** on each fact you are happy with. Unverified facts are not used in resumes, matching or forms.

The more verified facts you add, the better the matching and tailoring.

## 6. Add jobs

Open **Jobs**. There are four ways to add a job:

1. **Paste** the job description into the **New job** form and save.
2. **Import from a URL**: paste the job link and let the app fill in the form. Check it, then save.
3. **From the visible browser**: click **Open in browser**, log in to the site yourself if needed, then click **Use page open in browser**. The form is filled from the page you are looking at. Check the company name, then save.
4. **Discover sponsored jobs**: search connected job sources for roles in the UK, Europe, Singapore and the UAE that mention visa sponsorship and match your preferences.

Nothing is saved until you click save.

## 7. Review a job and prepare your documents

Click a job to open its detail page. You will see:

- **Analysis** – the key requirements of the role.
- **Match against verified facts** – how well you fit, with a score.
- **Country conventions** – the resume style expected in that country (for example no photo in the UK, photo common in Germany).
- **Proposed resume changes** – the edits suggested for this job. Accept the ones you want, or click **Keep my template**.

Then:

1. Click **Preview resume** to check the tailored resume.
2. Click **Prepare application** to create the application package (resume and cover letter).
3. Optionally click **Interview prep** to get a study plan for the role.

## 8. Approve and execute (the Approvals page)

Anything that would leave your machine goes to **Approvals** first.

1. Open **Approvals**. Pending items show what will be sent and where.
2. Click **Approve** if it looks right, or **Reject** if not.
3. Approved items move to **Ready to execute**. They still do nothing until you click **Execute**.

If anything changes after you approve it (for example you edit an answer or the resume file changes), the approval no longer matches and you must approve again.

## 9. Applying through the visible browser (optional)

The Worker fills in online application forms in a **browser window you can see**. You are always in control:

- You log in yourself, and complete any MFA or CAPTCHA yourself. The app never bypasses these.
- The app fills in the form and **stops before the final Submit button**.
- The final submit only happens when you approve and execute it.

**One-time setup** (details in [BROWSER_AUTO_APPLY.md](BROWSER_AUTO_APPLY.md)):

1. Build the solution, then install the browser:
   `pwsh src/ArvindJobHunter.Worker/bin/Debug/net10.0/playwright.ps1 install chromium`
2. Start the Worker (it listens on http://127.0.0.1:5310).
3. In **Settings → Browser auto-apply sites**, turn on the sites you want and set a low daily limit (start with 1).

**Applying:**

1. On **Applications**, choose **Fill application in browser**.
2. Log in in the browser window if asked.
3. Back in the app, check the screenshot and the filled answers. Answer any questions the app could not.
4. Approve and execute the submission on **Approvals**.

Use **DRY_RUN** first on each new site to check the form is filled correctly before you use **LIVE**.

## 10. Resumes from ResumeNow (optional)

If you build resume versions on ResumeNow:

1. In **Settings → Resume library**, click **Open ResumeNow**. It opens in the visible browser.
2. Log in, edit and **download** your resume as a PDF there yourself.
3. Click **Refresh downloads**, enter keywords (for example `uk, .net, backend`) and click **Import**.

The app picks the library resume whose keywords best match each job. If none match, it uses your master resume.

## 11. Track applications and follow up

Open **Applications** to see every application and its status (Applied, Recruiter replied, Interview, Offer, and so on). Move an application to its new status as things happen.

- When you mark an application as **Applied**, a reminder is set for **7 days later** automatically. You can change or clear it.
- Due reminders appear on the **Dashboard** under **Follow-ups due**.
- Click **Draft email** to create a polite follow-up. It is saved on the **Emails** page, where you can edit it and click **Request approval to send**. It is only sent after you approve and execute it.
- When the recruiter replies, the automatic reminder is cleared.

## 12. Other pages

| Page | Use it for |
|---|---|
| **Dashboard** | Pending approvals, top matches and due follow-ups at a glance. |
| **Emails** | Edit drafts (cover letters, outreach, follow-ups) and request approval to send. |
| **Interview prep** | Study plans and practice topics for each role. |
| **Activity** | A full record of everything the app did, and when. |
| **Help** | Search this documentation and ask questions about the app. |

## 13. Your first day – checklist

1. Start the app and sign up.
2. In **Settings**, keep **DEMO** mode, set your job preferences and upload your master resume.
3. In **Profile**, add at least 10 facts about yourself and mark them verified.
4. In **Jobs**, add one job you are interested in.
5. Open the job, review the match and resume changes, and click **Prepare application**.
6. Approve and execute it on **Approvals** (nothing real happens in DEMO).
7. Mark it **Applied** on **Applications** and see the 7-day reminder appear.
8. When you are comfortable, switch to **DRY_RUN**, then **LIVE**.

## 14. Troubleshooting

| Problem | What to try |
|---|---|
| The page does not load | Check the app is running with `.\jobhunter.ps1 status`, then restart it. |
| "The Worker browser is not configured" | Start the Worker and make sure Chromium is installed (section 9). |
| The browser form stopped early | You probably need to log in or solve a CAPTCHA in the browser window. Do it, then prepare the application again. |
| Approval says it no longer matches | Something changed after you approved. Review it and approve again. |
| Submit is blocked | The site may be turned off, its daily limit reached, or the app is not in LIVE mode. Check **Settings**. |
| The Worker was restarted | Open browser sessions are lost. Prepare the application again before submitting. |
| Emails are not sent | Connect Google in **Settings**, and make sure the app is in LIVE mode. |

For more detail, see [STARTUP_GUIDE.md](STARTUP_GUIDE.md), [approval-engine.md](approval-engine.md) and [BROWSER_AUTO_APPLY.md](BROWSER_AUTO_APPLY.md).
