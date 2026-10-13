#!/usr/bin/env node

/**
 * ArvindJobHunter Startup Script (Cross-Platform)
 * 
 * This Node.js script builds and starts both backend (.NET) and frontend (Angular)
 * Works on Windows, macOS, and Linux
 * 
 * Usage:
 *   node start.js                    # Full build and start
 *   node start.js --skip-build       # Only start services
 *   node start.js --skip-frontend    # Backend only
 *   node start.js --skip-backend     # Frontend only
 */

const { spawn, spawnSync } = require('child_process');
const path = require('path');
const fs = require('fs');

// Detect OS
const isWindows = process.platform === 'win32';
const isMac = process.platform === 'darwin';
const isLinux = process.platform === 'linux';

// Parse command line arguments
const args = process.argv.slice(2);
const skipBuild = args.includes('--skip-build');
const skipFrontend = args.includes('--skip-frontend');
const skipBackend = args.includes('--skip-backend');

// Directory paths
const rootDir = path.resolve(__dirname);
const backendDir = path.join(rootDir, 'src', 'ArvindJobHunter.Api');
const frontendDir = path.join(rootDir, 'src', 'ArvindJobHunter.Web');

// Colors for console output
const colors = {
    reset: '\x1b[0m',
    cyan: '\x1b[36m',
    green: '\x1b[32m',
    yellow: '\x1b[33m',
    red: '\x1b[31m',
};

function log(message, color = 'reset') {
    console.log(`${colors[color]}${message}${colors.reset}`);
}

function logSection(title) {
    log(`\n========== ${title} ==========\n`, 'cyan');
}

function runCommand(command, args, options = {}) {
    return new Promise((resolve, reject) => {
        const child = spawnSync(command, args, {
            cwd: options.cwd || process.cwd(),
            stdio: 'inherit',
            shell: isWindows,
        });

        if (child.error) {
            reject(child.error);
        } else if (child.status !== 0) {
            reject(new Error(`Command failed with exit code ${child.status}`));
        } else {
            resolve();
        }
    });
}

async function main() {
    try {
        log('\n=====================================', 'cyan');
        log('  ArvindJobHunter Startup Script', 'cyan');
        log('=====================================\n', 'cyan');

        // Build Backend
        if (!skipBackend) {
            logSection('BACKEND BUILD');

            if (!skipBuild) {
                log('► Cleaning backend solution...', 'yellow');
                await runCommand('dotnet', ['clean'], { cwd: rootDir });
                log('✓ Clean completed\n', 'green');

                log('► Building backend solution...', 'yellow');
                await runCommand('dotnet', ['build'], { cwd: rootDir });
                log('✓ Build completed\n', 'green');
            }
        }

        // Build Frontend
        if (!skipFrontend) {
            logSection('FRONTEND BUILD');

            // Check if node_modules exists
            if (!fs.existsSync(path.join(frontendDir, 'node_modules'))) {
                log('► Installing frontend dependencies...', 'yellow');
                await runCommand('npm', ['install'], { cwd: frontendDir });
                log('✓ Dependencies installed\n', 'green');
            } else {
                log('✓ Frontend dependencies already installed\n', 'green');
            }

            if (!skipBuild) {
                log('► Building Angular frontend...', 'yellow');
                await runCommand('npm', ['run', 'build'], { cwd: frontendDir });
                log('✓ Build completed\n', 'green');
            }
        }

        // Start Services
        logSection('STARTING SERVICES');

        const processes = [];

        // Start Backend
        if (!skipBackend) {
            log('Starting Backend API...', 'yellow');
            const backendProcess = spawn('dotnet', ['run'], {
                cwd: backendDir,
                stdio: 'inherit',
                shell: isWindows,
            });
            processes.push(backendProcess);
            log(`✓ Backend started (PID: ${backendProcess.pid})\n`, 'green');
        }

        // Start Frontend
        if (!skipFrontend) {
            log('Starting Angular Dev Server...', 'yellow');
            const frontendProcess = spawn('npm', ['start'], {
                cwd: frontendDir,
                stdio: 'inherit',
                shell: isWindows,
            });
            processes.push(frontendProcess);
            log(`✓ Frontend started (PID: ${frontendProcess.pid})\n`, 'green');
        }

        // Display startup info
        log('=====================================', 'green');
        log('  Services Started Successfully!', 'green');
        log('=====================================\n', 'green');

        log('Backend API:   http://localhost:5228', 'cyan');
        log('Frontend:      http://localhost:4200', 'cyan');
        log('\nPress Ctrl+C to stop all services\n', 'yellow');

        // Handle graceful shutdown
        process.on('SIGINT', () => {
            log('\nShutting down services...', 'yellow');
            processes.forEach((proc) => {
                if (proc && !proc.killed) {
                    proc.kill('SIGTERM');
                }
            });
            setTimeout(() => process.exit(0), 1000);
        });

        // Wait for processes
        await Promise.all(
            processes.map(
                (proc) =>
                    new Promise((resolve) => {
                        proc.on('exit', resolve);
                    })
            )
        );
    } catch (error) {
        log(`\n✗ Error: ${error.message}`, 'red');
        process.exit(1);
    }
}

main();
