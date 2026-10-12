# ArvindJobHunter Startup Guide

## Quick Start

I've created startup scripts to build and run your entire application (backend + frontend) with a single command.

### Files Created

Three startup options are now available:

1. **jobhunter-start.bat** - Windows Batch Script (recommended for Command Prompt)
2. **start.js** - Node.js/Cross-Platform Script (works on Windows, Mac, Linux)
3. **docs/STARTUP_GUIDE.md** - Complete documentation

## How to Use

### Option 1: Windows Batch (Easiest)

```cmd
jobhunter-start.bat
```

This will:
- Clean the backend solution
- Build the backend (.NET)
- Install frontend dependencies
- Build the frontend (Angular)
- Start both services in separate windows

### Option 2: Node.js (Cross-Platform)

```bash
node start.js
```

Same functionality as the batch script, works on any OS with Node.js installed.

### Option 3: PowerShell

PowerShell script creation is in progress. You can also run directly:

```powershell
cd D:\Job-Hunt-
dotnet clean
dotnet build
cd src\ArvindJobHunter.Web
npm install
npm run build
npm start
# (and in another terminal)
cd src\ArvindJobHunter.Api
dotnet run
```

## Skip Options

Both scripts support skip flags:

```cmd
# Skip build, only start services
jobhunter-start.bat --skip-build

# Skip frontend build and startup
jobhunter-start.bat --skip-backend

# Skip backend build and startup  
jobhunter-start.bat --skip-frontend
```

```bash
node start.js --skip-build
node start.js --skip-backend
node start.js --skip-frontend
```

## Service URLs After Startup

| Service | URL | Port |
|---------|-----|------|
| Backend API | http://localhost:5228 | 5228 |
| Frontend | http://localhost:4200 | 4200 |

## Troubleshooting

### "Port already in use" error

Kill the existing process:

```powershell
# Find process on port 5228 (backend)
Get-NetTCPConnection -LocalPort 5228 | Select OwningProcess

# Kill by PID
taskkill /PID <PID> /F

# Or for port 4200 (frontend)
Get-NetTCPConnection -LocalPort 4200 | Select OwningProcess
taskkill /PID <PID> /F
```

### "Node.js not found"

Download from: https://nodejs.org/

```powershell
node --version
npm --version
```

### ".NET SDK not found"

Download from: https://dotnet.microsoft.com/download/dotnet/10

```powershell
dotnet --version
```

### "npm packages missing"

```bash
cd src\ArvindJobHunter.Web
npm install
```

## Development Workflow

### When making C# changes:
1. Press `Ctrl+C` in backend window
2. Make your changes
3. Run `jobhunter-start.bat --skip-build` to rebuild and restart
4. Or if only service restart needed: `dotnet run` in backend directory

### When making Angular changes:
1. Just save - Angular dev server auto-refreshes
2. No restart needed!

### When adding packages:
1. Backend: Edit `.csproj` files, then rebuild
2. Frontend: Run `npm install` in `src\ArvindJobHunter.Web`, then rebuild

## Creating a Desktop Shortcut (Windows)

For one-click startup:

1. Right-click desktop → New → Shortcut
2. Enter: `cmd.exe /c "D:\Job-Hunt-\jobhunter-start.bat"`
3. Name it "Start ArvindJobHunter"
4. Click Finish
5. (Optional) Right-click shortcut → Properties → Advanced → Check "Run as administrator"

## Environment Variables

Set before running if you need to override settings:

```powershell
$env:Google__ClientId = "your-id"
$env:Google__ClientSecret = "your-secret"
node start.js
```

## Build Artifacts

After building:

- **Backend**: `bin/Debug/net10.0/`
- **Frontend**: `dist/arvind-job-hunter-web/`

## Next Steps

1. Ensure .NET 10 SDK is installed
2. Ensure Node.js is installed
3. Run `jobhunter-start.bat` from project root
4. Open http://localhost:4200 in your browser
5. Start developing!

## Advanced: Running Services Separately

### Backend Only
```bash
cd D:\Job-Hunt-\src\ArvindJobHunter.Api
dotnet run
```

### Frontend Only
```bash
cd D:\Job-Hunt-\src\ArvindJobHunter.Web
npm start
```

### Without Building
```bash
node start.js --skip-build
```

## Questions?

Check `docs/STARTUP_GUIDE.md` for more details and troubleshooting.
