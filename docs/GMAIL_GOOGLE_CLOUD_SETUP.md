# Gmail Account Integration - Google Cloud Configuration Guide

## Overview

This guide provides step-by-step instructions to configure Google Cloud for Gmail OAuth 2.0 integration with the ArvindJobHunter application.

## Prerequisites

- A Google Cloud account (free or paid)
- Access to Google Cloud Console (https://console.cloud.google.com)
- Basic understanding of OAuth 2.0

## Phase 1: Create or Select a Google Cloud Project

### Step 1.1: Create a New Project
1. Go to [Google Cloud Console](https://console.cloud.google.com)
2. Click on the project dropdown at the top of the page
3. Click **NEW PROJECT**
4. Enter project name: `ArvindJobHunter-Gmail-OAuth` (or similar)
5. Click **CREATE**
6. Wait for the project to be created (this may take a minute)

### Step 1.2: Select the Project
1. Click on the project dropdown again
2. Select your newly created project
3. Confirm you're in the correct project by checking the project ID displayed

## Phase 2: Enable Required APIs

### Step 2.1: Enable Gmail API
1. In the Google Cloud Console, go to **APIs & Services** > **Library**
2. Search for "Gmail API"
3. Click on the Gmail API result
4. Click **ENABLE**
5. Wait for the API to be enabled (you'll see a checkmark)

### Step 2.2: Enable OpenID Connect API (for User Info)
1. Go back to **APIs & Services** > **Library**
2. Search for "Google+ API" or "OpenID Connect"
3. Click on the result
4. Click **ENABLE**

## Phase 3: Configure OAuth Consent Screen

### Step 3.1: Access Consent Screen Configuration
1. Go to **APIs & Services** > **OAuth consent screen**
2. Select **External** as the User Type (for testing outside your organization)
3. Click **CREATE**

### Step 3.2: Fill in Application Information

**OAuth Consent Screen:**
- **App name**: ArvindJobHunter
- **User support email**: Your email address
- **Developer contact information**: Your email address
- Click **SAVE AND CONTINUE**

**Scopes:**
- Click **ADD OR REMOVE SCOPES**
- Add the following scopes:
  - `https://www.googleapis.com/auth/gmail.compose` (Send emails)
  - `https://www.googleapis.com/auth/gmail.readonly` (Read emails)
  - `https://www.googleapis.com/auth/userinfo.email` (Get email address)
  - `https://www.googleapis.com/auth/userinfo.profile` (Get profile info)
- Click **UPDATE**
- Click **SAVE AND CONTINUE**

**Test Users:**
- Click **ADD USERS**
- Add your Google account email address (the one you'll use for testing)
- This is required for testing before general release
- Click **SAVE AND CONTINUE**

### Step 3.3: Review and Finish
- Review the summary
- Click **BACK TO DASHBOARD**

## Phase 4: Create OAuth 2.0 Credentials

### Step 4.1: Create OAuth Client
1. Go to **APIs & Services** > **Credentials**
2. Click **+ CREATE CREDENTIALS**
3. Select **OAuth 2.0 Client ID**
4. Choose application type: **Web application**
5. Enter a name: `ArvindJobHunter Local Dev` (for local testing)

### Step 4.2: Configure Authorized Redirect URIs

**For Local Development:**
- Click **+ ADD URI** under **Authorized redirect URIs**
- Enter: `http://localhost:5228/api/integrations/google/callback`
- Click **ADD URI**

**For Production (when ready):**
- If deploying to a server, add:
  - `https://yourdomain.com/api/integrations/google/callback`
  - Replace `yourdomain.com` with your actual domain

### Step 4.3: Save Credentials
1. Click **CREATE**
2. A dialog will show your credentials
3. **Client ID**: Copy and save this
4. **Client Secret**: Copy and save this securely (never commit to source control)
5. Click **OK**

**Important:** Never share your Client Secret. Treat it like a password.

## Phase 5: Store Credentials in Application

### Step 5.1: Using Environment Variables (Recommended for Development)

For local development with PowerShell:

```powershell
# Set the environment variables
$env:Google__ClientId = "YOUR_CLIENT_ID_HERE"
$env:Google__ClientSecret = "YOUR_CLIENT_SECRET_HERE"
$env:Google__RedirectUri = "http://localhost:5228/api/integrations/google/callback"

# Verify they're set (Client Secret will be masked)
Write-Host "ClientId: $env:Google__ClientId"
Write-Host "RedirectUri: $env:Google__RedirectUri"
```

### Step 5.2: Using appsettings.json (For Testing Only)

Edit `src/ArvindJobHunter.Api/appsettings.Development.json`:

```json
{
  "Google": {
	"ClientId": "YOUR_CLIENT_ID_HERE",
	"ClientSecret": "YOUR_CLIENT_SECRET_HERE",
	"RedirectUri": "http://localhost:5228/api/integrations/google/callback"
  }
}
```

**⚠️ WARNING:** Never commit actual credentials to source control. Use environment variables instead.

### Step 5.3: Using appsettings.json Runtime Update

The application also supports updating credentials at runtime through the API. See the testing section below.

## Phase 6: Verification Checklist

- [ ] Google Cloud project created
- [ ] Gmail API enabled
- [ ] OAuth consent screen configured
- [ ] OAuth client created
- [ ] Redirect URI registered: `http://localhost:5228/api/integrations/google/callback`
- [ ] Client ID and Secret obtained
- [ ] Credentials set in environment variables or configuration
- [ ] Your Google test account added as an authorized test user

## Phase 7: Important Notes on Google Approval

### Current Scopes Status

The application uses these scopes:
- `gmail.compose` - Send emails ✅ Allows composition and sending
- `gmail.readonly` - Read emails ✅ Allows reading inbox and labels
- `userinfo.email` - Get email address ✅ Allows retrieving user email

### Restricted Scopes

**Important:** Some Gmail scopes are restricted by Google:
- `gmail.full` - Not recommended without additional justification
- Sensitive scopes may require Google Security Assessment

### When Will Google Approval Be Required?

**During Testing:**
- Using test scopes with authorized test users only
- No approval needed

**For Production/Public Release:**
- When accessing Gmail on behalf of users outside your test user list
- You must submit your app for verification
- Google will review your use case and data handling practices
- This typically takes 1-3 weeks

**For This Application (Current Status):**
- Testing is possible without verification
- Add your test Google account as an authorized user
- No external users can connect until your app is verified

### How to Get Verification (When Ready)

When you're ready for production:
1. Go to **APIs & Services** > **OAuth consent screen**
2. Look for a "Submit for verification" button (appears when your app is ready)
3. Follow Google's verification process
4. Provide information about:
   - Your application name and purpose
   - How you use the Gmail data
   - Data retention and security practices
   - Privacy policy
   - Demonstrated testing

## Phase 8: Troubleshooting

### "Redirect URI mismatch" Error
- Verify the redirect URI exactly matches: `http://localhost:5228/api/integrations/google/callback`
- Check for trailing slashes or typos
- Restart your application

### "Client ID or Client Secret is invalid"
- Verify you copied the entire value (no extra spaces)
- Check that you're using the correct credentials for your project
- Regenerate credentials if needed

### "Consent screen not configured"
- You must complete all required fields in the OAuth consent screen
- Add at least one test user (your email)

### "Access denied" or "Unauthorized" When Connecting
- Ensure your Google account is added as a test user
- Log out and log back in to Google
- Clear browser cookies and try again

## Phase 9: Local Testing Procedure

See `GMAIL_INTEGRATION_LOCAL_TESTING.md` for step-by-step testing instructions.

## Additional Resources

- [Google OAuth 2.0 Documentation](https://developers.google.com/identity/protocols/oauth2)
- [Gmail API Documentation](https://developers.google.com/gmail/api)
- [Google Cloud Console](https://console.cloud.google.com)
- [OAuth 2.0 Security Best Practices](https://tools.ietf.org/html/draft-ietf-oauth-security-topics)
