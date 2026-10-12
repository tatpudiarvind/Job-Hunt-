# Google OAuth and Gmail Configuration Guide

## 1. Purpose

This guide is the primary reference for configuring Google OAuth and Gmail integration in ArvindJobHunter. It covers the required Google Cloud setup, in-application configuration, runtime behavior, implementation notes, and the most common errors encountered during setup.

Use this guide as the main source of truth for local OAuth setup and troubleshooting.

---

## 2. Scope

ArvindJobHunter uses Google OAuth to connect a Google account so the application can:

- read Gmail messages
- create Gmail drafts
- send emails after approval
- identify the connected Google account

The application supports entering the OAuth settings directly in the Settings UI. Those values are then persisted into the API configuration through the backend settings endpoint.

---

## 3. Required Configuration Values

You must obtain the following values from Google Cloud Console:

1. Google Client ID
2. Google Client Secret
3. Redirect URI

Local development redirect URI:

- `http://localhost:5228/api/integrations/google/callback`

Optional HTTPS local redirect URI when applicable:

- `https://localhost:7288/api/integrations/google/callback`

The redirect URI used by the application must exactly match the value registered in the Google OAuth client configuration.

---

## 4. Application-Side Configuration

## 4.1 Configure from the Settings UI

1. Start the application.
2. Open the frontend.
3. Navigate to **Settings**.
4. Open the **Google / Gmail** section.
5. Enter:
   - Google Client ID
   - Google Client Secret
   - Redirect URI
6. Select **Save Google settings**.
7. After saving, select **Connect Google**.
8. Sign in to Google and approve access.
9. Verify that the connected account appears in the Settings UI.

## 4.2 Backend Persistence Behavior

The frontend uses these backend settings endpoints:

- `GET /api/settings/google-oauth`
- `PUT /api/settings/google-oauth`

The backend writes the saved values into:

- `src/ArvindJobHunter.Api/appsettings.json`

Configuration keys:

- `Google:ClientId`
- `Google:ClientSecret`
- `Google:RedirectUri`

---

## 5. Google Cloud / Google Auth Platform Setup

## 5.1 Select the Correct Google Cloud Project

1. Open Google Cloud Console.
2. Select the project intended for ArvindJobHunter OAuth.
3. Confirm that the OAuth client belongs to that same project.

## 5.2 Enable Gmail API

1. Open **APIs & Services**.
2. Open **Library**.
3. Search for **Gmail API**.
4. Enable it.

## 5.3 Configure Google Auth Platform

Configure the following sections.

### Branding
Use values such as:

- App name: `Job Hunt`
- User support email: your Google account
- Developer contact email: your Google account

For local testing, links such as logo, privacy policy, and terms may remain optional if Google permits them.

### Audience
Use:

- User type: `External`
- Publishing status: `Testing`

Add your Google account under **Test users**.

### Clients
Open the OAuth client and register the redirect URIs.

Add:

- `http://localhost:5228/api/integrations/google/callback`

Add this as well if your local backend uses HTTPS:

- `https://localhost:7288/api/integrations/google/callback`

### Data Access
Add the required scopes:

- `https://www.googleapis.com/auth/gmail.readonly`
- `https://www.googleapis.com/auth/gmail.compose`
- `https://www.googleapis.com/auth/userinfo.email`

---

## 6. Recommended Setup Sequence

Use the following order:

1. create or select the Google Cloud project
2. enable Gmail API
3. configure Branding
4. configure Audience and add test users
5. configure Clients and redirect URIs
6. configure Data Access scopes
7. start the application
8. open Settings in ArvindJobHunter
9. save Client ID, Client Secret, and Redirect URI
10. restart the backend if needed
11. select **Connect Google**
12. approve access
13. confirm the connected account appears

---

## 7. Local Validation Checklist

Before attempting the connection flow, verify all of the following:

- Gmail API is enabled
- Google Auth Platform has been configured
- your Google account is listed as a test user
- the redirect URI matches exactly
- the Client ID and Client Secret belong to the same OAuth client
- the values are saved in the application configuration
- the backend has been restarted when configuration behavior must be refreshed

---

## 8. Runtime Flow

The runtime flow is:

1. the user saves Google OAuth settings from the UI
2. the backend persists the values into configuration
3. the user selects **Connect Google**
4. the backend generates the Google authorization URL
5. the user signs in and approves permissions
6. Google redirects back to `/api/integrations/google/callback`
7. the backend exchanges the authorization code for tokens
8. the application stores the refresh token securely
9. the user is redirected back to Settings and sees the connected account state

---

## 9. Implementation Notes

The application was updated to improve the Google OAuth experience and reliability.

Key changes include:

1. restoration of the Google OAuth configuration form in the Settings UI
2. support for saving Client ID, Client Secret, and Redirect URI through the application
3. reuse of the backend settings endpoints to update configuration
4. improved callback diagnostics so failures no longer appear only as generic authorization errors
5. correction of the OAuth service lifetime so in-memory authorization state survives the authorize/callback round trip

---

## 10. FAQ

### Q1. Why was I getting `invalid_client`?
This usually means Google rejected the OAuth client configuration.

Typical causes:

- incorrect client ID
- incorrect client secret
- client ID and client secret taken from different OAuth clients
- backend still using stale or unsaved configuration values

### Q2. Why was I getting `redirect_uri_mismatch`?
This occurs when the redirect URI sent by the application does not exactly match the authorized redirect URI in Google Cloud Console.

Correct local callback:

- `http://localhost:5228/api/integrations/google/callback`

Typical causes:

- wrong port
- `https` instead of `http`
- wrong callback path
- trailing slash mismatch

### Q3. Why was I getting `access_denied` or an app verification message?
This occurs when the OAuth app is still in Testing mode and the active Google account is not listed as a test user.

Fix:

- add the Google account under **Audience > Test users**

### Q4. Why did I get `Google authorization failed.`?
Originally, the callback returned only a generic failure message and did not expose enough detail to diagnose the real token-exchange or validation issue.

This behavior was improved so the application now surfaces more useful failure information.

### Q5. Why did I get `The Google sign-in session expired or is invalid.`?
This occurred because OAuth `state` was held in memory on a service instance that did not persist between the authorization and callback requests.

Fix applied:

- `IGoogleOAuthService` lifetime was changed from transient to singleton

### Q6. Why is so much Google configuration required?
Google requires explicit OAuth configuration when an application requests Gmail-related scopes. This includes client creation, redirect URI registration, audience configuration, consent setup, scope declaration, and test-user management while the app remains in testing.

### Q7. Can Client ID and Client Secret now be configured from the UI?
Yes.

The Settings page now includes fields for:

- Google Client ID
- Google Client Secret
- Redirect URI

Saving those values updates the API `appsettings.json` file through the backend.

### Q8. Do I still need to restart the backend after changing Google settings?
In most cases, restarting remains the safest operational step after changing OAuth settings, especially when validating that the running process is using the latest configuration behavior.

---

## 11. Related Documentation

Supporting references:

- `docs/GOOGLE_OAUTH_USER_FLOW.md`
- `docs/GMAIL_GOOGLE_CLOUD_SETUP.md`
- `docs/GMAIL_INTEGRATION_LOCAL_TESTING.md`
- `docs/GMAIL_OAUTH_401_FIX.md`
- `docs/PRODUCTION_DEPLOYMENT_GUIDE.md`

---

## 12. Summary

ArvindJobHunter now supports a UI-driven Google OAuth configuration flow, persistent local OAuth settings, Gmail connect/disconnect behavior, improved diagnostics, and stable callback state handling. This guide should be used as the primary reference whenever setting up or troubleshooting Google OAuth locally.
