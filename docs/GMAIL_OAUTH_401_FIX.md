# Gmail OAuth 401 Error Fix - "invalid_client"

## Problem
You're getting **Error 401: invalid_client** from Google when trying to connect your Gmail account, even after entering valid Client ID and Client Secret.

## Root Cause
The application had a bug where the cached Google OAuth settings weren't being properly reloaded from the configuration file after saving. The provider would return empty/default values instead of your saved credentials.

## Solution Applied
Fixed the cache validation logic in `JsonGoogleOAuthSettingsProvider.cs` to:
1. Check if the cached configuration actually has credentials (not just if it exists)
2. Reload from file if credentials are missing
3. This ensures saved settings are immediately available without requiring an app restart

## Steps to Resolve

### Step 1: Rebuild the Application
The fix requires a rebuild to take effect.

```powershell
cd D:\Job-Hunt-
dotnet clean
dotnet build
```

### Step 2: Restart the Application
```powershell
# Stop any running instance (Ctrl+C if running in terminal)
# Then restart:
dotnet run --project src/ArvindJobHunter.Api
```

### Step 3: Verify Your Google Cloud Configuration

Before trying to connect, ensure you have:

1. **In Google Cloud Console (https://console.cloud.google.com):**
   - ✓ Project created and selected
   - ✓ Gmail API enabled
   - ✓ OAuth consent screen configured
   - ✓ OAuth 2.0 Client ID created (Web application type)
   - ✓ Authorized redirect URI set to exactly: `http://localhost:5228/api/integrations/google/callback`

2. **Your Google Account:**
   - ✓ Added as a test user in the OAuth consent screen
   - ✓ Not using 2FA or enable app-specific passwords if you are

### Step 4: Update Settings in Application

1. Open the application: http://localhost:4200
2. Go to **Settings**
3. Scroll to **Google / Gmail** section
4. Enter:
   - **Google Client ID**: Your actual Client ID from Google Cloud Console
   - **Google Client Secret**: Your actual Client Secret
   - **Redirect URI**: `http://localhost:5228/api/integrations/google/callback`
5. Click **"Save Google settings"** button
6. Wait for confirmation (message should say settings were saved)

### Step 5: Try Connecting
1. In Settings, click **"Connect Google"** button
2. You'll be redirected to Google's login
3. Log in with your test account (the one you added in OAuth consent screen)
4. **Grant the requested permissions** when asked
5. You should be redirected back and see "Not connected" change to show your email

## Troubleshooting

### Still getting "invalid_client" error?

**Check 1: Redirect URI Mismatch**
- In Google Cloud Console, go to **APIs & Services > Credentials**
- Click your OAuth client
- Under "Authorized redirect URIs", verify it contains exactly: `http://localhost:5228/api/integrations/google/callback`
- No typos, no trailing slashes, exact port 5228

**Check 2: Verify settings were saved**
- Look at `src/ArvindJobHunter.Api/appsettings.Development.json` or `appsettings.json`
- Should contain your Client ID and Secret:
```json
{
  "Google": {
	"ClientId": "YOUR_ACTUAL_ID",
	"ClientSecret": "YOUR_ACTUAL_SECRET",
	"RedirectUri": "http://localhost:5228/api/integrations/google/callback"
  }
}
```

**Check 3: Test account authorized?**
- Go to Google Cloud Console > **APIs & Services > OAuth consent screen**
- Under "Test users", verify your Gmail account is listed

**Check 4: Fresh start**
1. Delete `data/google-token.json` (removes any previous failed connection)
2. Restart the application
3. Try connecting again

### Getting different error?

- **"Google OAuth is not configured"**: Settings weren't saved, check `appsettings.json` has your credentials
- **"authorization failed"**: State validation timeout, try again immediately
- **Access blocked from Google**: Your account may not have Gmail API access enabled, check Google account settings

## Technical Details (What Was Fixed)

**Before:** The `JsonGoogleOAuthSettingsProvider` had a cache that would return empty credentials if the file hadn't been loaded yet.

**Code change:**
```csharp
// Before (buggy)
if (cached is not null) return cached;  // Could return empty config

// After (fixed)
if (cached is not null && !string.IsNullOrWhiteSpace(cached.ClientId)) return cached;
```

This ensures that if credentials haven't been saved yet, the provider will reload from file instead of returning empty values to Google.

## Next Steps

Once you've successfully connected your Gmail account:
1. You can list labels: GET `/api/integrations/gmail/labels`
2. You can view messages: GET `/api/integrations/gmail/messages`
3. You can search emails: GET `/api/integrations/gmail/messages/search?q=your-query`
4. You can disconnect anytime: POST `/api/integrations/google/revoke`

See `GMAIL_INTEGRATION_LOCAL_TESTING.md` for detailed API testing instructions.
