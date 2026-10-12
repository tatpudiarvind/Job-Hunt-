# Gmail Integration - Local Testing Procedure

## Overview

This document provides detailed steps to manually test the Gmail OAuth 2.0 integration locally.

## Prerequisites

- Completed setup from `GMAIL_GOOGLE_CLOUD_SETUP.md`
- Google Client ID and Client Secret obtained
- Google account added as a test user in OAuth consent screen
- ArvindJobHunter application source code
- Visual Studio 2026 or PowerShell for running the application

## Part 1: Environment Setup

### Step 1.1: Set Environment Variables

Open PowerShell and set the required environment variables:

```powershell
# Navigate to your workspace
cd D:\Job-Hunt-

# Set Google OAuth credentials
$env:Google__ClientId = "YOUR_ACTUAL_CLIENT_ID"
$env:Google__ClientSecret = "YOUR_ACTUAL_CLIENT_SECRET"
$env:Google__RedirectUri = "http://localhost:5228/api/integrations/google/callback"

# Verify they're set
Write-Host "Environment variables set:"
Write-Host "Google__ClientId: $($env:Google__ClientId.Substring(0, 20))..." 
Write-Host "Google__RedirectUri: $env:Google__RedirectUri"
```

**Keep this PowerShell window open** while running the application.

### Step 1.2: Start the Application

In a new PowerShell window:

```powershell
cd D:\Job-Hunt-

# Restore and build
dotnet build

# Run the API
dotnet run --project src/ArvindJobHunter.Api
```

The API should start at `http://localhost:5228`

You should see output like:
```
info: Microsoft.Hosting.Lifetime[14]
	  Now listening on: http://localhost:5228
```

## Part 2: Initial Setup

### Step 2.1: Open the API in HTTP Client or Postman

You can use:
- VS Code REST Client extension
- Postman
- PowerShell `Invoke-RestMethod`
- Or the provided `.http` file in the project

The ArvindJobHunter.Api.http file contains pre-configured requests.

### Step 2.2: Check Auth Status

**Request:**
```
GET http://localhost:5228/api/auth/status
```

**Response:**
```json
{
  "configured": false,
  "authenticated": false
}
```

### Step 2.3: Setup Local Authentication

First, create a local user account:

**Request:**
```
POST http://localhost:5228/api/auth/setup
Content-Type: application/json

{
  "username": "testuser",
  "password": "secure-password-12345"
}
```

**Response:** `204 No Content`

### Step 2.4: Login

**Request:**
```
POST http://localhost:5228/api/auth/login
Content-Type: application/json

{
  "username": "testuser",
  "password": "secure-password-12345"
}
```

**Response:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresAt": "2024-12-20T12:34:56Z",
  "userId": "12345678-1234-1234-1234-123456789012"
}
```

**Save the token** - you'll need it for all subsequent requests.

### Step 2.5: Verify Google Configuration Status

**Request:**
```
GET http://localhost:5228/api/integrations/google/status
Authorization: Bearer {YOUR_TOKEN}
```

**Response (without configured credentials):**
```json
{
  "configured": false,
  "connected": false,
  "accountEmail": null
}
```

## Part 3: Connect Gmail Account

### Step 3.1: Get Authorization URI

This generates the URL where the user logs in and grants permission.

**Request:**
```
GET http://localhost:5228/api/integrations/google/authorize
Authorization: Bearer {YOUR_TOKEN}
```

**Response:**
```json
{
  "url": "https://accounts.google.com/o/oauth2/v2/auth?client_id=YOUR_CLIENT_ID&redirect_uri=http%3A%2F%2Flocalhost%3A5228%2Fapi%2Fintegrations%2Fgoogle%2Fcallback&response_type=code&scope=https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fgmail.compose%20https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fgmail.readonly%20https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fuserinfo.email&access_type=offline&prompt=consent&state=ABC123DEF456..."
}
```

### Step 3.2: Complete OAuth Flow Manually (For Testing)

Since we can't automatically open a browser, manually:

1. Copy the `url` from the response above
2. Open it in your web browser
3. **Choose your test Google account** (the one you added as an authorized test user)
4. **Review permissions:**
   - Compose emails
   - View and manage your Gmail mailbox
   - View your email address
5. Click **Allow**
6. You'll be redirected to: `http://localhost:5228/api/integrations/google/callback?code=AUTHORIZATION_CODE&state=STATE`

The redirect URI will show in your browser address bar but may fail to display a page (that's expected - we didn't implement a frontend).

**Copy the Authorization Code** from the URL parameter. It looks like:
```
4/0AY0e-g5X7q2KJ9mQ4...
```

### Step 3.3: Verify Connection Status

**Request:**
```
GET http://localhost:5228/api/integrations/google/status
Authorization: Bearer {YOUR_TOKEN}
```

**Response (if successfully connected):**
```json
{
  "configured": true,
  "connected": true,
  "accountEmail": "your.email@gmail.com"
}
```

## Part 4: Test Gmail Read Operations

### Step 4.1: List Labels

**Request:**
```
GET http://localhost:5228/api/integrations/gmail/labels
Authorization: Bearer {YOUR_TOKEN}
```

**Response:**
```json
{
  "labels": [
	{
	  "id": "INBOX",
	  "name": "Inbox",
	  "messageCount": 42,
	  "threadCount": 15
	},
	{
	  "id": "SENT",
	  "name": "[Gmail]/Sent Mail",
	  "messageCount": 128,
	  "threadCount": 80
	}
  ]
}
```

### Step 4.2: List Messages

**Request:**
```
GET http://localhost:5228/api/integrations/gmail/messages?maxResults=5
Authorization: Bearer {YOUR_TOKEN}
```

**Response:**
```json
{
  "messages": [
	{
	  "id": "18e0b4c58c7f1234",
	  "threadId": "18e0b4c58c7f1234",
	  "labels": ["INBOX", "IMPORTANT"],
	  "isSpam": false,
	  "isStarred": false,
	  "isUnread": true,
	  "receivedAt": "2024-12-19T14:30:00Z",
	  "from": "sender@example.com",
	  "subject": "Important Update",
	  "preview": "This is an important message about..."
	}
  ],
  "nextPageToken": "12345abc...",
  "totalResults": 42
}
```

### Step 4.3: Search Messages

**Request:**
```
GET http://localhost:5228/api/integrations/gmail/messages/search?q=from:example@gmail.com&maxResults=5
Authorization: Bearer {YOUR_TOKEN}
```

**Response:** Similar to Step 4.2, but filtered by the search query.

### Step 4.4: Get Full Message

Use the message ID from Step 4.2:

**Request:**
```
GET http://localhost:5228/api/integrations/gmail/messages/18e0b4c58c7f1234
Authorization: Bearer {YOUR_TOKEN}
```

**Response:**
```json
{
  "id": "18e0b4c58c7f1234",
  "threadId": "18e0b4c58c7f1234",
  "labels": ["INBOX", "IMPORTANT"],
  "isSpam": false,
  "isStarred": false,
  "isUnread": true,
  "receivedAt": "2024-12-19T14:30:00Z",
  "snippet": "This is an important message about...",
  "textContent": "Full email text content here...",
  "htmlContent": "<html><body>Full email HTML content here...</body></html>",
  "payload": {
	"mimeType": "text/plain",
	"headers": [
	  {
		"name": "From",
		"value": "sender@example.com"
	  },
	  {
		"name": "Subject",
		"value": "Important Update"
	  }
	]
  }
}
```

## Part 5: Test Gmail Write Operations

### Step 5.1: Create Draft

**Request:**
```
POST http://localhost:5228/api/emails
Authorization: Bearer {YOUR_TOKEN}
Content-Type: application/json

{
  "jobId": null,
  "kind": "OTHER",
  "to": "recipient@example.com",
  "subject": "Test Email",
  "body": "This is a test email body."
}
```

This creates a draft in the application. The draft can then be sent via Gmail when approved.

## Part 6: Disconnect Account

### Step 6.1: Revoke Access

**Request:**
```
POST http://localhost:5228/api/integrations/google/revoke
Authorization: Bearer {YOUR_TOKEN}
```

**Response:** `204 No Content`

### Step 6.2: Verify Disconnection

**Request:**
```
GET http://localhost:5228/api/integrations/google/status
Authorization: Bearer {YOUR_TOKEN}
```

**Response (after revocation):**
```json
{
  "configured": true,
  "connected": false,
  "accountEmail": null
}
```

The account is now disconnected. To reconnect, repeat **Part 3** above.

## Part 7: Run Automated Tests

### Step 7.1: Unit Tests

```powershell
cd D:\Job-Hunt-

# Run Gmail client tests
dotnet test tests/ArvindJobHunter.Application.Tests/ -v normal
```

Expected: All GmailClientTests pass (6+ tests)

### Step 7.2: Integration Tests

```powershell
# Run integration tests
dotnet test tests/ArvindJobHunter.Api.Tests/ -v normal
```

Expected: 
- GmailIntegrationTests: Tests verify authorization and empty results when not connected
- GoogleOAuthFlowTests: Tests verify OAuth flow and proper HTTP status codes

## Part 8: Troubleshooting

### "Invalid Redirect URI" Error
- Verify your redirect URI is exactly: `http://localhost:5228/api/integrations/google/callback`
- Check the URI in your Google Cloud Console
- Restart the application

### 401 Unauthorized on Gmail Endpoints
- Ensure your authentication token is valid
- Include `Authorization: Bearer {YOUR_TOKEN}` header
- Re-login if your token has expired

### "Gmail is not connected" Message
- Call the authorize endpoint first
- Complete the OAuth flow in your browser
- Verify connection status shows `"connected": true`

### "Token Expired" or "Invalid Grant"
- The refresh token may have been revoked
- Reconnect your Gmail account
- Ensure your test account hasn't changed permissions in Google Account settings

### Empty Message Lists
- Verify you're connected (`status` shows `connected: true`)
- Check that your Gmail account actually has messages
- Try a specific search query: `?q=from:me` to find sent emails

## Part 9: Simulating Token Expiration (Advanced)

To test the token refresh logic:

1. Get a valid access token following Part 3
2. Delete the `google-token.json` file from the data directory
3. Try calling a Gmail endpoint
4. The endpoint should fail gracefully (not crash)
5. Reconnect via the authorize flow

## Performance Notes

- Message listing fetches full message data to include headers
- Large mailboxes may take time to list
- Use maxResults=5 for testing to speed up responses
- Search queries use Gmail's server-side filtering (fast)

## Next Steps

1. Implement frontend UI for Gmail connection in your frontend application
2. Integrate message display with your existing UI framework
3. Add additional Gmail operations as needed (archive, delete, reply)
4. Submit app for Google verification when ready for production

