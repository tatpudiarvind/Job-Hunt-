#Requires -Version 5.1
<#
.SYNOPSIS
	Starts or stops the Job Hunter application (API + Angular frontend).
.EXAMPLE
	.\jobhunter.ps1 start
	.\jobhunter.ps1 stop
	.\jobhunter.ps1 status
#>
[CmdletBinding()]
param(
	[Parameter(Position = 0)]
	[ValidateSet('start', 'stop', 'status', 'restart')]
	[string]$Command = 'start',

	[switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'
$root    = $PSScriptRoot
$apiDir  = Join-Path $root 'src\ArvindJobHunter.Api'
$webDir  = Join-Path $root 'src\ArvindJobHunter.Web'
$apiPort = 5228
$webPort = 4200
$appUrl  = "http://localhost:$webPort"

function Get-ListeningPid([int]$port) {
	$conn = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
	if ($conn) { return $conn.OwningProcess }
	return $null
}

function Wait-ForPort([int]$port, [string]$name, [int]$timeoutSec = 90) {
	$sw = [Diagnostics.Stopwatch]::StartNew()
	while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) {
		if (Get-ListeningPid $port) { Write-Host "  [OK] $name is listening on port $port" -ForegroundColor Green; return $true }
		Start-Sleep -Milliseconds 500
	}
	Write-Host "  [!!] $name did not start within $timeoutSec seconds (port $port)" -ForegroundColor Yellow
	return $false
}

function Stop-OnPort([int]$port, [string]$name) {
	$procId = Get-ListeningPid $port
	if (-not $procId) { Write-Host "  [--] $name is not running" -ForegroundColor DarkGray; return }
	Write-Host "  Stopping $name (PID $procId)..." -NoNewline
	# Kill the process tree so the console window hosting it also closes.
	& taskkill.exe /PID $procId /T /F *> $null
	Write-Host " done" -ForegroundColor Green
}

function Start-App {
	Write-Host "`nStarting Job Hunter" -ForegroundColor Cyan

	if (Get-ListeningPid $apiPort) { Write-Host "  [--] API already running on port $apiPort" -ForegroundColor DarkGray }
	else {
		Write-Host "  Launching API..."
		Start-Process powershell.exe -WorkingDirectory $apiDir -ArgumentList '-NoLogo', '-NoProfile', '-Command', "`$Host.UI.RawUI.WindowTitle = 'Job Hunter - API'; dotnet run --launch-profile http"
	}

	if (Get-ListeningPid $webPort) { Write-Host "  [--] Frontend already running on port $webPort" -ForegroundColor DarkGray }
	else {
		if (-not (Test-Path (Join-Path $webDir 'node_modules'))) {
			Write-Host "  Installing frontend dependencies (first run)..." -ForegroundColor Yellow
			Push-Location $webDir; try { npm install } finally { Pop-Location }
		}
		Write-Host "  Launching frontend..."
		Start-Process powershell.exe -WorkingDirectory $webDir -ArgumentList '-NoLogo', '-NoProfile', '-Command', "`$Host.UI.RawUI.WindowTitle = 'Job Hunter - Web'; npx ng serve"
	}

	$apiOk = Wait-ForPort $apiPort 'API'
	$webOk = Wait-ForPort $webPort 'Frontend'

	if ($apiOk -and $webOk) {
		Write-Host "`nJob Hunter is running at $appUrl" -ForegroundColor Green
		if (-not $NoBrowser) { Start-Process $appUrl }
	}
	Write-Host "Run 'jobhunter stop' to shut everything down.`n" -ForegroundColor DarkGray
}

function Stop-App {
	Write-Host "`nStopping Job Hunter" -ForegroundColor Cyan
	Stop-OnPort $webPort 'Frontend'
	Stop-OnPort $apiPort 'API'
	Write-Host ""
}

function Show-Status {
	Write-Host "`nJob Hunter status" -ForegroundColor Cyan
	foreach ($svc in @(@{ Name = 'API'; Port = $apiPort }, @{ Name = 'Frontend'; Port = $webPort })) {
		$procId = Get-ListeningPid $svc.Port
		if ($procId) { Write-Host "  [OK] $($svc.Name) running on port $($svc.Port) (PID $procId)" -ForegroundColor Green }
		else         { Write-Host "  [--] $($svc.Name) not running" -ForegroundColor DarkGray }
	}
	Write-Host ""
}

switch ($Command) {
	'start'   { Start-App }
	'stop'    { Stop-App }
	'restart' { Stop-App; Start-Sleep -Seconds 1; Start-App }
	'status'  { Show-Status }
}
