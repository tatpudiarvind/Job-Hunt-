# Google OAuth - User Connection Flow

## Overview

ArvindJobHunter now supports a **user-friendly OAuth 2.0 connection flow** for Gmail integration. Users can connect their Google accounts with a single click, similar to "Sign in with Google" on other websites.

This guide explains:
1. **For Administrators**: How to configure the application with Google OAuth credentials (one-time setup)
2. **For Users**: How to connect their Google account to the application

---

## Part 1: Administrator Setup (One-Time Configuration)

### Prerequisites
- Google Cloud Console access
- A project in Google Cloud Console (create one if needed)
- Gmail API enabled in the project

### Step 1: Get Your OAuth Credentials

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Select your project (or create a new one)
3. Enable the Gmail API:
   - Search for "Gmail API" in the search box
   - Click "Enable"
4. Create OAuth 2.0 Client ID:
   - Go to **Credentials** (left sidebar)
   - Click **Create Credentials** → **OAuth 2.0 Client ID**
   - Choose **Web application**
   - Add Authorized redirect URIs:
	 - **Local development**: `http://localhost:5228/api/integrations/google/callback`
	 - **Production**: `https://yourdomain.com/api/integrations/google/callback`
   - Copy the **Client ID** and **Client Secret**

### Step 2: Configure the Application

Configure the OAuth credentials via **environment variables** or **configuration file**.

#### Option A: Environment Variables (Recommended for All Environments)

Set these environment variables before running the application:

```bash
# Windows PowerShell
$env:Google__ClientId = "your-client-id-here"
$env:Google__ClientSecret = "your-client-secret-here"
$env:Google__RedirectUri = "http://localhost:5228/api/integrations/google/callback"
```

```bash
# Linux/macOS
export Google__ClientId="your-client-id-here"
export Google__ClientSecret="your-client-secret-here"
export Google__RedirectUri="http://localhost:5228/api/integrations/google/callback"
```

```bash
# Docker / Container environments
# Set in your .env file or deployment configuration:
Google__ClientId=your-client-id-here
Google__ClientSecret=your-client-secret-here
Google__RedirectUri=https://yourdomain.com/api/integrations/google/callback
```

#### Option B: Configuration File (Local Development Only)

Edit `src/ArvindJobHunter.Api/appsettings.json`:

```json
{
  "Google": {
	"ClientId": "your-client-id-here",
	"ClientSecret": "your-client-secret-here",
	"RedirectUri": "http://localhost:5228/api/integrations/google/callback"
  }
}
```

⚠️ **WARNING**: Never commit the appsettings.json file with secrets to version control. Use environment variables in production.

### Step 3: Verify Configuration

Start the application and check:
1. Navigate to Settings page
2. Verify the warning message "Gmail actions are unavailable because Google OAuth is not configured" is **gone**
3. If you see a "Connect Google" button, configuration is successful

---

## Part 2: User Connection Flow

### How to Connect Your Google Account

1. **Open Settings Page**
   - Log in to ArvindJobHunter
   - Navigate to **Settings** (gear icon)

2. **Locate Google/Gmail Section**
   - Scroll to the **Google / Gmail** card
   - You should see a "**Connect Google**" button

3. **Click "Connect Google"**
   - You will be redirected to Google's official login/authorization page
   - This is Google's standard OAuth consent screen

4. **Sign In to Your Google Account**
   - If not already signed in, enter your Google email and password
   - Complete any required 2-factor authentication

5. **Grant Permissions**
   - Review the permissions requested:
	 - **Gmail Compose**: Send emails on your behalf (after your approval)
	 - **Gmail Read**: Read your emails to search and analyze them
	 - **Email Access**: Retrieve your email address
   - Click **Allow** to grant permissions

6. **Connection Successful**
   - You'll be redirected back to Settings
   - You'll see: "**Connected as your-email@gmail.com**"
   - A "**Disconnect Google**" button appears to revoke access anytime

### What Permissions Does ArvindJobHunter Get?

- ✅ Read Gmail messages (search, analyze, retrieve)
- ✅ Compose and send emails (only after you manually approve each action)
- ✅ Access your email address
- ❌ **Cannot**: Delete emails, modify labels, or access other Google services
- ❌ **Cannot**: Be done without your approval (every action requires explicit permission)

### Disconnecting Your Account

If you want to disconnect:
1. Go to Settings → Google / Gmail section
2. Click "**Disconnect Google**"
3. Your Google account and tokens are immediately removed from the application
4. Your Google account itself remains unchanged

### Reconnecting

Simply click "Connect Google" again. You'll go through the OAuth flow again.

---

## Part 3: Technical Details

### OAuth 2.0 Authorization Code Flow

The connection uses the standard OAuth 2.0 Authorization Code Flow:

```
User clicks "Connect Google"
		↓
App redirects to Google Authorization Endpoint
		↓
User signs in and approves permissions
		↓
Google redirects to callback with authorization code
		↓
App backend exchanges code for tokens (secure, server-side)
		↓
App stores encrypted refresh token
		↓
User redirected back to Settings with success message
```

### Token Security

- **Access Token**: Short-lived (1 hour), used to make Gmail API calls
- **Refresh Token**: Long-lived, stored **encrypted** on the server
- **Client Secret**: Never exposed to frontend, only used on backend
- **Your Data**: Kept in your local application files, never sent to ArvindJobHunter servers

### Redirect URIs

The redirect URI must match **exactly** what's registered in Google Cloud Console:

| Environment | Redirect URI |
|-------------|--------------|
| Local Dev | `http://localhost:5228/api/integrations/google/callback` |
| Production | `https://yourdomain.com/api/integrations/google/callback` |

### Troubleshooting

#### "Gmail actions are unavailable because Google OAuth is not configured yet"

**Cause**: Application doesn't have ClientId/ClientSecret configured

**Solution**: 
1. Check environment variables: `echo $env:Google__ClientId` (PowerShell) or `echo $Google__ClientId` (bash)
2. Or check appsettings.json for Google section
3. Restart the application after configuring
4. Verify in browser console that the authorization URL returns in the connect endpoint

#### "Google authorization failed"

**Possible causes**:
1. Redirect URI doesn't match what's registered in Google Cloud Console
2. OAuth credentials are incorrect
3. Browser cookies/cache issues

**Solutions**:
1. Clear browser cookies/cache for Google and localhost
2. Verify redirect URI in Google Cloud Console matches exactly:
   - `http://localhost:5228/api/integrations/google/callback` (note: `http://` not `https://` for local dev)
3. Verify ClientId and ClientSecret are correct
4. Try in an incognito/private browser window

#### "Google redirected back but no success message"

**Possible cause**: Frontend didn't detect the ?google=connected query parameter

**Solution**:
1. Check browser console for errors (F12)
2. Manually refresh the Settings page
3. Check that Settings page loads successfully

---

## Part 4: Security Best Practices

### For Administrators

1. **Never commit secrets to version control**
   - Use environment variables or secure secret management
   - Use `.gitignore` to exclude appsettings.json with secrets

2. **Rotate credentials regularly**
   - Update Google OAuth credentials periodically
   - Revoke old credentials from Google Cloud Console

3. **Monitor token usage**
   - Tokens are used only for Gmail API calls
   - Check Gmail API usage in Google Cloud Console

### For Users

1. **Understand permissions**
   - Review what permissions you're granting
   - Only connect if you trust the application

2. **Disconnect when not needed**
   - Disconnect your Google account if you stop using Gmail features
   - This revokes access immediately

3. **Use strong Google account password**
   - Protects your Google account and ArvindJobHunter connection

4. **Be aware of data retention**
   - Once disconnected, tokens are deleted
   - Your email data in ArvindJobHunter is separate from your Google account

---

## Part 5: Testing

### Local Development Testing

1. **Prepare test credentials**:
   ```bash
   $env:Google__ClientId = "your-test-client-id"
   $env:Google__ClientSecret = "your-test-client-secret"
   $env:Google__RedirectUri = "http://localhost:5228/api/integrations/google/callback"
   ```

2. **Start the application**:
   ```bash
   jobhunter-start.bat
   # or
   node start.js
   ```

3. **Test the flow**:
   - Open http://localhost:4200
   - Log in with your local account
   - Go to Settings
   - Click "Connect Google"
   - Use a test Google account (or your personal account)
   - Verify you see "Connected as your-email@gmail.com"

4. **Test disconnection**:
   - Click "Disconnect Google"
   - Verify you see "Connect Google" button again

---

## FAQ

**Q: Can multiple users connect different Google accounts?**
A: Currently, the application supports a single user with one Google account connection. Each user would have their own connection stored locally.

**Q: Is my email visible to the developers?**
A: No. Your emails are read locally by your instance of the application only. Gmail tokens and emails are never sent to our servers.

**Q: Can I use a Google Workspace/Organization account?**
A: Yes, as long as Google Workspace admin allows OAuth 2.0 access for third-party applications.

**Q: What if I delete my Google account?**
A: The connection will fail the next time the application tries to use it. Simply disconnect in Settings.

**Q: How often do I need to reconnect?**
A: Only once. The refresh token keeps your connection alive indefinitely (unless you manually disconnect or revoke).

---

## Additional Resources

- [Google OAuth 2.0 Documentation](https://developers.google.com/identity/protocols/oauth2)
- [Gmail API Documentation](https://developers.google.com/gmail/api)
- [Google Cloud Console](https://console.cloud.google.com/)
