# Production Deployment Guide - Google OAuth Integration

## Overview

This guide explains how to deploy the OAuth-enabled ArvindJobHunter application to production with proper Google OAuth configuration.

---

## Pre-Deployment Checklist

### 1. Update Google Cloud Console

**Register Production Redirect URI:**

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Select your production project
3. Navigate to **Credentials**
4. Edit your OAuth 2.0 Client ID (Web application)
5. Add your production redirect URI to **Authorized redirect URIs**:
   ```
   https://yourdomain.com/api/integrations/google/callback
   ```
6. Save changes

**Important**: Google requires exact URI match. If your domain is `example.com`, the URI must be exactly `https://example.com/api/integrations/google/callback` (note HTTPS, no trailing slash).

### 2. Prepare OAuth Credentials

**Never commit credentials to version control.**

Obtain from Google Cloud Console:
- Client ID (shareable)
- Client Secret (keep secure!)
- OAuth credentials file (if using service account)

### 3. Prepare Configuration

Choose your deployment platform and configure credentials appropriately (see sections below).

---

## Deployment Platform-Specific Instructions

### Option A: Azure App Service (Recommended)

#### Step 1: Deploy Application

```bash
# Publish .NET backend
cd src/ArvindJobHunter.Api
dotnet publish -c Release -o ./publish

# Upload to Azure (using Visual Studio or Azure CLI)
az webapp up --name your-app-name --resource-group your-resource-group --plan your-app-plan
```

#### Step 2: Configure OAuth Credentials in Azure

Via **Azure Portal**:

1. Go to your App Service
2. Select **Configuration** → **Application settings**
3. Add new settings:
   - `Google__ClientId`: `your-production-client-id`
   - `Google__ClientSecret`: `your-production-client-secret`
   - `Google__RedirectUri`: `https://your-app-name.azurewebsites.net/api/integrations/google/callback`
4. Click **Save** and **Continue** when prompted to restart

Via **Azure CLI**:

```bash
az webapp config appsettings set \
  --resource-group your-resource-group \
  --name your-app-name \
  --settings \
	Google__ClientId="your-client-id" \
	Google__ClientSecret="your-client-secret" \
	Google__RedirectUri="https://your-app-name.azurewebsites.net/api/integrations/google/callback"
```

#### Step 3: Verify Configuration

```bash
az webapp config appsettings list \
  --resource-group your-resource-group \
  --name your-app-name
```

### Option B: Docker / Kubernetes

#### Step 1: Build Docker Image

```dockerfile
# Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10 AS build
WORKDIR /build
COPY . .
RUN dotnet build
RUN dotnet publish -c Release -o publish

FROM mcr.microsoft.com/dotnet/aspnet:10
WORKDIR /app
COPY --from=build /build/publish .
ENTRYPOINT ["dotnet", "ArvindJobHunter.Api.dll"]
```

#### Step 2: Configure Environment Variables

**In docker-compose.yml:**

```yaml
version: '3.8'
services:
  arvind-job-hunter:
	image: your-registry/arvind-job-hunter:latest
	environment:
	  Google__ClientId: ${GOOGLE_CLIENT_ID}
	  Google__ClientSecret: ${GOOGLE_CLIENT_SECRET}
	  Google__RedirectUri: https://yourdomain.com/api/integrations/google/callback
	  Web__BaseUrl: https://yourdomain.com
	ports:
	  - "5228:5228"
```

**.env file (Git-ignored):**

```bash
GOOGLE_CLIENT_ID=your-production-client-id
GOOGLE_CLIENT_SECRET=your-production-client-secret
```

**Deploy:**

```bash
docker-compose up -d
```

#### Step 3: Kubernetes Secrets (For K8s)

```bash
kubectl create secret generic google-oauth \
  --from-literal=clientId=your-client-id \
  --from-literal=clientSecret=your-client-secret

# Use in deployment.yaml:
env:
  - name: Google__ClientId
	valueFrom:
	  secretKeyRef:
		name: google-oauth
		key: clientId
  - name: Google__ClientSecret
	valueFrom:
	  secretKeyRef:
		name: google-oauth
		key: clientSecret
  - name: Google__RedirectUri
	value: "https://yourdomain.com/api/integrations/google/callback"
```

### Option C: Traditional Server / VPS

#### Step 1: Deploy Application

```bash
# SSH into server
ssh user@your-server.com

# Clone/update repository
cd /var/www/arvind-job-hunter
git pull origin main

# Build and publish
cd src/ArvindJobHunter.Api
dotnet publish -c Release -o /var/www/arvind-job-hunter/app

# Set permissions
sudo chown -R www-data:www-data /var/www/arvind-job-hunter/app
```

#### Step 2: Configure Environment Variables

**Edit systemd service file:**

```bash
sudo nano /etc/systemd/system/arvind-job-hunter.service
```

```ini
[Unit]
Description=ArvindJobHunter
After=network.target

[Service]
Type=notify
User=www-data
WorkingDirectory=/var/www/arvind-job-hunter/app
ExecStart=/usr/bin/dotnet /var/www/arvind-job-hunter/app/ArvindJobHunter.Api.dll

Environment="Google__ClientId=your-production-client-id"
Environment="Google__ClientSecret=your-production-client-secret"
Environment="Google__RedirectUri=https://yourdomain.com/api/integrations/google/callback"
Environment="Web__BaseUrl=https://yourdomain.com"

Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
```

**Reload and start:**

```bash
sudo systemctl daemon-reload
sudo systemctl enable arvind-job-hunter
sudo systemctl start arvind-job-hunter
sudo systemctl status arvind-job-hunter
```

---

## Configuration Best Practices

### 1. Never Expose Secrets

❌ **Do NOT**:
- Commit secrets to version control
- Log secrets to stdout/files
- Include secrets in Docker image layers
- Pass secrets via URL parameters

✅ **DO**:
- Use environment variables
- Use cloud provider secret managers (Azure Key Vault, AWS Secrets Manager)
- Use Kubernetes Secrets
- Rotate secrets regularly

### 2. Environment-Specific Configuration

Create separate OAuth credentials for each environment:

| Environment | Client ID | Client Secret | Redirect URI |
|------------|-----------|---------------|--------------|
| **Local Dev** | test-dev-id | test-dev-secret | `http://localhost:5228/api/integrations/google/callback` |
| **Staging** | staging-id | staging-secret | `https://staging.yourdomain.com/api/integrations/google/callback` |
| **Production** | prod-id | prod-secret | `https://yourdomain.com/api/integrations/google/callback` |

### 3. Verify Redirect URI Matches Exactly

Google requires **exact** URI match:
- ✅ `https://yourdomain.com/api/integrations/google/callback`
- ❌ `https://yourdomain.com/api/integrations/google/callback/` (trailing slash)
- ❌ `https://www.yourdomain.com/api/integrations/google/callback` (missing www prefix if registered without)

### 4. HTTPS Only for Production

- ✅ **Local**: `http://localhost:5228/api/integrations/google/callback` (HTTP allowed)
- ✅ **Production**: `https://yourdomain.com/api/integrations/google/callback` (HTTPS required)
- ✅ Configure SSL/TLS certificate (Let's Encrypt, AWS ACM, etc.)

### 5. Data Protection Keys

.NET uses data protection keys for encrypting sensitive data (refresh tokens):

**Azure:**
```bash
az webapp config appsettings set \
  --name your-app-name \
  --resource-group your-resource-group \
  --settings ASPNETCORE_ENVIRONMENT="Production"
```

**Server (Linux):**
```bash
# Store keys in Azure Key Vault or local secure directory
# Update Program.cs if using custom key storage
```

---

## Post-Deployment Verification

### 1. Health Check

```bash
curl https://yourdomain.com/health
# Expected response: {"status":"ok","phase":"phase-3-approval-first-core"}
```

### 2. OAuth Configuration Check

```bash
curl -X GET https://yourdomain.com/api/integrations/google/status \
  -H "Authorization: Bearer YOUR_SESSION_TOKEN"
# Expected: {"configured":true,"connected":false}
```

### 3. Manual OAuth Flow Test

1. Access the application: `https://yourdomain.com`
2. Log in with your credentials
3. Navigate to Settings → Google/Gmail
4. Click "Connect Google"
5. Verify you're redirected to Google's login/consent screen
6. Grant permissions
7. Verify you're redirected back with success message
8. Verify "Connected as your-email@gmail.com" appears

### 4. Check Logs

**Azure:**
```bash
az webapp log tail --resource-group your-resource-group --name your-app-name
```

**Server:**
```bash
sudo journalctl -u arvind-job-hunter -f
```

---

## Troubleshooting Production Issues

### Issue: "Invalid Redirect URI"

**Cause**: URI doesn't match what's registered in Google Cloud Console

**Solution**:
1. Check Google Cloud Console → Credentials → Your OAuth 2.0 Client ID
2. Verify redirect URI matches exactly (case-sensitive!)
3. Update URI in:
   - Google Cloud Console **AND**
   - Application environment variable (`Google__RedirectUri`)

### Issue: "Redirect URI Mismatch" After Login

**Cause**: `Web__BaseUrl` setting doesn't match domain

**Check environment variables:**
```bash
# Azure
az webapp config appsettings list --name your-app-name

# Docker
docker exec container-id env | grep Web__BaseUrl

# Server
cat /etc/systemd/system/arvind-job-hunter.service | grep Web__BaseUrl
```

**Solution**: Ensure `Web__BaseUrl` matches your production domain:
```
Web__BaseUrl=https://yourdomain.com
```

### Issue: "Google Authorization Failed" After Successful Google Consent

**Causes**:
1. Client Secret is incorrect
2. Google OAuth credentials are revoked/deleted
3. Network issue between backend and Google API

**Solutions**:
1. Verify `Google__ClientId` and `Google__ClientSecret` in environment
2. Check application logs for HTTP errors
3. Verify credentials are active in Google Cloud Console
4. Test Google API connectivity:
   ```bash
   curl https://oauth2.googleapis.com/token \
	 -d "client_id=YOUR_CLIENT_ID&client_secret=YOUR_CLIENT_SECRET&grant_type=refresh_token&refresh_token=test"
   ```

### Issue: Tokens Not Persisting (Connection Lost After Restart)

**Cause**: Data directory permissions or file storage issues

**Solution**:
1. Verify data directory permissions:
   ```bash
   ls -la /var/data/arvind-job-hunter/
   ```
2. Ensure application has write permissions to data directory
3. Check disk space:
   ```bash
   df -h
   ```
4. Review application logs for storage errors

---

## Monitoring & Maintenance

### 1. Monitor OAuth API Usage

**Google Cloud Console**:
1. Go to **APIs & Services** → **Credentials**
2. Click on your OAuth 2.0 Client ID
3. Review **Usage** tab
4. Set up alerts for quota usage

### 2. Rotate Credentials Periodically

**Quarterly recommended:**
1. Create new OAuth 2.0 Client ID in Google Cloud Console
2. Register new redirect URI
3. Update application environment variables
4. Test in staging environment
5. Deploy to production
6. Delete old credentials from Google Cloud Console

### 3. Monitor Token Expiration

Application automatically handles:
- ✅ Access token refresh (1-hour expiry)
- ✅ Refresh token storage and encryption
- ✅ Token revocation on disconnect

No manual intervention needed, but monitor logs for refresh failures.

### 4. Backup Data

The application stores tokens in:
- **Default**: `./data/google-token.json` (encrypted)
- **Custom**: Configured via application settings

Backup strategy:
```bash
# Daily backup
0 2 * * * tar -czf /backups/arvind-$(date +%Y%m%d).tar.gz /var/data/arvind-job-hunter/

# Keep 30 days of backups
find /backups -name "arvind-*.tar.gz" -mtime +30 -delete
```

---

## Security Hardening Checklist

- [ ] Credentials are environment variables, not in code
- [ ] Redirect URI is HTTPS only in production
- [ ] SSL/TLS certificate installed and valid
- [ ] Data directory is restricted to app user only
- [ ] Application logs don't expose secrets
- [ ] Firewall allows only necessary ports (80, 443)
- [ ] Regular security updates applied to OS and .NET
- [ ] Backup and disaster recovery plan in place
- [ ] Monitoring and alerting configured
- [ ] Secrets rotation schedule established

---

## Support & Additional Resources

- [Google OAuth 2.0 Documentation](https://developers.google.com/identity/protocols/oauth2)
- [Gmail API Security](https://developers.google.com/gmail/api/auth)
- [Azure App Service Configuration](https://learn.microsoft.com/en-us/azure/app-service/configure-common)
- [Docker Best Practices for Secrets](https://docs.docker.com/engine/swarm/secrets/)

---

## Rollback Plan

If OAuth stops working after deployment:

1. **Immediate**: Revert to previous app version
   ```bash
   git revert HEAD
   dotnet publish -c Release
   # Redeploy
   ```

2. **Communication**: Inform users Gmail features are temporarily unavailable

3. **Diagnosis**: Review logs and Google Cloud Console activity

4. **Fix**: Address configuration or credential issues

5. **Retest**: Fully test OAuth flow in staging before re-deploying to production

---

## Post-Launch Monitoring

**First 24 hours**:
- Monitor application logs for errors
- Test OAuth flow multiple times
- Verify token storage and persistence
- Check Google API quotas

**First week**:
- Collect user feedback
- Monitor performance metrics
- Verify backup integrity
- Document any issues or improvements

**Ongoing**:
- Weekly quota reviews
- Monthly backup tests
- Quarterly credential rotation
- Continuous security updates
