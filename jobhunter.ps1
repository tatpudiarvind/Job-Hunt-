#Requires -Version 5.1
<#
.SYNOPSIS
	Starts or stops the Job Hunter application (Worker + API + Angular frontend).
.EXAMPLE
	.\jobhunter.ps1 start
	.\jobhunter.ps1 stop
	.\jobhunter.ps1 status
	.\jobhunter.ps1 start -Browser chrome
	.\jobhunter.ps1 browser default   # save preference: chrome | edge | firefox | default
#>
[CmdletBinding()]
param(
	[Parameter(Position = 0)]
	[ValidateSet('start', 'stop', 'status', 'restart', 'browser')]
	[string]$Command = 'start',

	[Parameter(Position = 1)]
	[ValidateSet('', 'chrome', 'edge', 'firefox', 'default')]
	[string]$Browser = '',

	[switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'
$root    = $PSScriptRoot
$apiDir  = Join-Path $root 'src\ArvindJobHunter.Api'
$workerDir = Join-Path $root 'src\ArvindJobHunter.Worker'
$webDir  = Join-Path $root 'src\ArvindJobHunter.Web'
$workerPort = 5310
$apiPort = 5228
$webPort = 4200
$appUrl  = "http://localhost:$webPort"
$settingsFile = Join-Path $root '.jobhunter.settings.json'

function Get-BrowserPreference {
	if (Test-Path $settingsFile) {
		try { $saved = (Get-Content $settingsFile -Raw | ConvertFrom-Json).browser; if ($saved) { return $saved } } catch { }
	}
	return 'chrome'
}

function Set-BrowserPreference([string]$name) {
	if (-not $name) { Write-Host "Current browser: $(Get-BrowserPreference)  (options: chrome, edge, firefox, default)"; return }
	@{ browser = $name } | ConvertTo-Json | Set-Content -Path $settingsFile -Encoding UTF8
	Write-Host "Browser preference saved: $name" -ForegroundColor Green
}

function Find-BrowserExe([string]$name) {
	$exe = @{ chrome = 'chrome.exe'; edge = 'msedge.exe'; firefox = 'firefox.exe' }[$name]
	if (-not $exe) { return $null }
	foreach ($hive in 'HKCU:', 'HKLM:') {
		$path = (Get-ItemProperty "$hive\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\$exe" -ErrorAction SilentlyContinue).'(default)'
		if ($path -and (Test-Path $path)) { return $path }
	}
	$candidates = @{
		chrome  = @("$env:ProgramFiles\Google\Chrome\Application\chrome.exe", "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe", "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe")
		edge    = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe")
		firefox = @("$env:ProgramFiles\Mozilla Firefox\firefox.exe", "${env:ProgramFiles(x86)}\Mozilla Firefox\firefox.exe")
	}[$name]
	return $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}

function Open-App {
	$name = if ($Browser) { $Browser } else { Get-BrowserPreference }
	if ($name -ne 'default') {
		$exePath = Find-BrowserExe $name
		if ($exePath) { Start-Process -FilePath $exePath -ArgumentList $appUrl; return }
		Write-Host "  [!!] $name not found; using the system default browser" -ForegroundColor Yellow
	}
	Start-Process $appUrl
}

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

	if (Get-ListeningPid $workerPort) { Write-Host "  [--] Worker already running on port $workerPort" -ForegroundColor DarkGray }
	else {
		Write-Host "  Launching Worker..."
		Start-Process powershell.exe -WorkingDirectory $workerDir -ArgumentList '-NoLogo', '-NoProfile', '-Command', "`$Host.UI.RawUI.WindowTitle = 'Job Hunter - Worker'; dotnet run"
	}

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

	$workerOk = Wait-ForPort $workerPort 'Worker'
	$apiOk = Wait-ForPort $apiPort 'API'
	$webOk = Wait-ForPort $webPort 'Frontend'

	if ($workerOk -and $apiOk -and $webOk) {
		Write-Host "`nJob Hunter is running at $appUrl" -ForegroundColor Green
		if (-not $NoBrowser) { Open-App }
	}
	Write-Host "Run 'jobhunter stop' to shut everything down.`n" -ForegroundColor DarkGray
}

function Stop-App {
	Write-Host "`nStopping Job Hunter" -ForegroundColor Cyan
	Stop-OnPort $webPort 'Frontend'
	Stop-OnPort $apiPort 'API'
	Stop-OnPort $workerPort 'Worker'
	Write-Host ""
}

function Show-Status {
	Write-Host "`nJob Hunter status" -ForegroundColor Cyan
	foreach ($svc in @(@{ Name = 'Worker'; Port = $workerPort }, @{ Name = 'API'; Port = $apiPort }, @{ Name = 'Frontend'; Port = $webPort })) {
		$procId = Get-ListeningPid $svc.Port
		if ($procId) { Write-Host "  [OK] $($svc.Name) running on port $($svc.Port) (PID $procId)" -ForegroundColor Green }
		else         { Write-Host "  [--] $($svc.Name) not running" -ForegroundColor DarkGray }
	}
	Write-Host ""
}

switch ($Command) {
	'start'   { Start-App }
	'stop'    { Stop-App }
	'restart' { Stop-App; Start-Sleep -Seconds 2; Start-App }
	'status'  { Show-Status }
	'browser' { Set-BrowserPreference $Browser }
}
