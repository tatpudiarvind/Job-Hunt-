# ArvindJobHunter Startup Guide

## 1. Purpose

This guide explains how to build and start the complete ArvindJobHunter application for local use. It covers the combined startup workflow for the backend API and Angular frontend, available command options, environment prerequisites, and common troubleshooting steps.

---

## 2. Application Components

The local application consists of the following primary services:

| Component | Default URL | Purpose |
| --- | --- | --- |
| Frontend | `http://localhost:4200` | Angular user interface |
| Backend API | `http://localhost:5228` | ASP.NET Core API |

The startup workflow is intended to:

1. clean and build the backend solution
2. install frontend dependencies when required
3. build the frontend
4. start both services

---

## 3. Prerequisites

Before starting the application, ensure that the following tools are installed:

- .NET 10 SDK
- Node.js
- npm
- PowerShell or Command Prompt on Windows

Recommended verification commands:

```powershell
dotnet --version
node --version
npm --version
```

---

## 4. Standard Startup Options

## 4.1 Batch Startup

From the repository root, run:

```cmd
jobhunter-start.bat
```

This is the primary Windows startup option when using Command Prompt.

## 4.2 Node-Based Startup

From the repository root, run:

```bash
node start.js
```

This provides a cross-platform startup path where Node.js is available.

## 4.3 Manual Startup

If you prefer to control each step manually:

```powershell
cd D:\Job-Hunt-
dotnet clean
dotnet build

cd src\ArvindJobHunter.Web
npm install
npm run build

cd D:\Job-Hunt-\src\ArvindJobHunter.Api
dotnet run
```

In a separate terminal:

```powershell
cd D:\Job-Hunt-\src\ArvindJobHunter.Web
npm start
```

---

## 5. Optional Startup Modes

## 5.1 Skip Build

Use this mode when you only want to restart already built services:

```cmd
jobhunter-start.bat --skip-build
```

```bash
node start.js --skip-build
```

## 5.2 Backend Only

Use this mode when you do not want to start the frontend:

```cmd
jobhunter-start.bat --skip-frontend
```

```bash
node start.js --skip-frontend
```

## 5.3 Frontend Only

Use this mode when you do not want to start the backend:

```cmd
jobhunter-start.bat --skip-backend
```

```bash
node start.js --skip-backend
```

---

## 6. Environment Configuration

Some features require environment-specific configuration. For example, Google OAuth can be provided through environment variables before startup.

Example PowerShell configuration:

```powershell
$env:Google__ClientId = "your-client-id"
$env:Google__ClientSecret = "your-client-secret"
$env:Google__RedirectUri = "http://localhost:5228/api/integrations/google/callback"
jobhunter-start.bat
```

Example Command Prompt configuration:

```cmd
set Google__ClientId=your-client-id
set Google__ClientSecret=your-client-secret
set Google__RedirectUri=http://localhost:5228/api/integrations/google/callback
jobhunter-start.bat
```

These values are commonly used for:

- Google OAuth and Gmail integration
- environment-specific overrides
- local testing of external integrations

---

## 7. Verifying Successful Startup

After startup completes, verify:

1. the frontend opens at `http://localhost:4200`
2. the backend responds at `http://localhost:5228`
3. the health endpoint returns successfully at `http://localhost:5228/health`

If the UI loads and the API health endpoint responds, the basic startup path is working correctly.

---

## 8. Development Workflow Guidance

## 8.1 Backend Code Changes

After C# changes:

1. stop the backend process
2. rebuild or rerun the backend
3. use the combined startup command again if both services must restart

## 8.2 Frontend Code Changes

Angular local development typically supports live reload. In most cases:

1. save the file
2. allow the frontend dev server to refresh automatically

## 8.3 Dependency Changes

If package or project dependencies change:

- rerun the full startup/build workflow
- use `npm install` again when frontend dependencies change
- rebuild the .NET solution when project references or packages change

---

## 9. Troubleshooting

### 9.1 Port Already in Use

If `5228` or `4200` is already in use, stop the conflicting process.

PowerShell example:

```powershell
Get-NetTCPConnection -LocalPort 5228 | Select-Object OwningProcess
Get-NetTCPConnection -LocalPort 4200 | Select-Object OwningProcess
```

Then terminate the relevant process if required.

### 9.2 PowerShell Execution Policy

If a PowerShell startup script is blocked by execution policy:

```powershell
Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process
```

### 9.3 Node.js or npm Not Found

Install Node.js and then confirm:

```powershell
node --version
npm --version
```

### 9.4 .NET SDK Not Found

Install the .NET 10 SDK and confirm:

```powershell
dotnet --version
```

### 9.5 Frontend Dependency Issues

Reinstall frontend dependencies:

```powershell
cd src\ArvindJobHunter.Web
npm install
```

---

## 10. Operational Notes

- The frontend and backend are separate long-running processes.
- Local startup is intended for workstation development and testing.
- For deployment guidance, see the production deployment documentation.

Related documents:

- `docs/local-development.md`
- `docs/PRODUCTION_DEPLOYMENT_GUIDE.md`
- `docs/GOOGLE_OAUTH_CONFIGURATION_GUIDE.md`

---

## 11. Summary

Use the repository-root startup commands to build and run the application consistently. Prefer the combined startup workflow for normal local development, and use skip options only when you intentionally want to limit what is rebuilt or started.
