@echo off
REM ArvindJobHunter Start Script - Windows Batch
REM This script builds and starts both backend and frontend
REM Usage: jobhunter-start.bat [--skip-build] [--skip-frontend] [--skip-backend]

setlocal enabledelayedexpansion

set "RootDir=%~dp0"
set "BackendDir=%RootDir%src\ArvindJobHunter.Api"
set "FrontendDir=%RootDir%src\ArvindJobHunter.Web"

REM Parse arguments
set "SkipBuild=0"
set "SkipFrontend=0"
set "SkipBackend=0"

for %%A in (%*) do (
	if "%%A"=="--skip-build" set "SkipBuild=1"
	if "%%A"=="--skip-frontend" set "SkipFrontend=1"
	if "%%A"=="--skip-backend" set "SkipBackend=1"
)

echo.
echo =====================================
echo   ArvindJobHunter Startup Script
echo =====================================
echo.

REM Build Backend
if %SkipBackend% equ 0 (
	echo ========== BACKEND BUILD ==========
	echo.

	if %SkipBuild% equ 0 (
		echo [*] Cleaning backend solution...
		cd /d "%RootDir%"
		call dotnet clean
		if errorlevel 1 (
			echo [-] Backend clean failed
			exit /b 1
		)
		echo [+] Clean completed
		echo.

		echo [*] Building backend solution...
		cd /d "%RootDir%"
		call dotnet build
		if errorlevel 1 (
			echo [-] Backend build failed
			exit /b 1
		)
		echo [+] Build completed
		echo.
	)
)

REM Build Frontend
if %SkipFrontend% equ 0 (
	echo ========== FRONTEND BUILD ==========
	echo.

	cd /d "%FrontendDir%"

	if not exist "node_modules" (
		echo [*] Installing frontend dependencies...
		call npm install
		if errorlevel 1 (
			echo [-] npm install failed
			exit /b 1
		)
		echo [+] Dependencies installed
		echo.
	) else (
		echo [+] Frontend dependencies already installed
		echo.
	)

	if %SkipBuild% equ 0 (
		echo [*] Building Angular frontend...
		call npm run build
		if errorlevel 1 (
			echo [-] Frontend build failed
			exit /b 1
		)
		echo [+] Build completed
		echo.
	)
)

REM Start Services
echo ========== STARTING SERVICES ==========
echo.

if %SkipBackend% equ 0 (
	echo [*] Starting Backend API...
	cd /d "%BackendDir%"
	start "ArvindJobHunter Backend" dotnet run
	echo [+] Backend started
	timeout /t 2 /nobreak > nul
)

if %SkipFrontend% equ 0 (
	echo [*] Starting Angular Dev Server...
	cd /d "%FrontendDir%"
	start "ArvindJobHunter Frontend" npm start
	echo [+] Frontend started
)

echo.
echo =====================================
echo   Services Started Successfully!
echo =====================================
echo.
echo Backend API:   http://localhost:5228
echo Frontend:      http://localhost:4200
echo.
echo Press Ctrl+C in each window to stop services
echo.

endlocal
