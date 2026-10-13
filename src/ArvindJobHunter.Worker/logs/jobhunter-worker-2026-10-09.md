# 📒 Job Hunter Worker — log for Friday, 9 October 2026

> Written by the Job Hunter Markdown logger. Times are **local time (UTC+05:30)**; every application start appends a new session.
> Exceptions are attached under their entry in collapsible blocks (⤵). Tokens, API keys, passwords and OAuth codes are masked as `***`.
>
> **Levels:** 🔍 TRACE · 🐞 DEBUG · ℹ️ INFO · ⚠️ WARN · ❌ ERROR · 🔥 CRITICAL


---

## ▶️ Session started · 10:53:58 · PID 45852

| Property | Value |
| :--- | :--- |
| Application | ArvindJobHunter.Worker `1.0.0+5b7419e287dc2a34d5b4dd398bd88951354471a8` |
| Environment | Production |
| Machine | IMW1031333C |
| Runtime | .NET 10.0.12 · Microsoft Windows 10.0.22631 |
| Content root | `D:\Job-Hunt-\src\ArvindJobHunter.Worker` |
| Log folder | `D:\Job-Hunt-\src\ArvindJobHunter.Worker\logs` |

| Time | Level | Source | Message | Correlation |
| :--- | :--- | :--- | :--- | :--- |
| 10:53:58.828 | ℹ️ INFO | ArvindJobHunter.Worker | Resume worker listening on 127.0.0.1:5310; output directory D:\Job-Hunt-\src\ArvindJobHunter.Worker\output; Markdown logs in D:\Job-Hunt-\src\ArvindJobHunter.Worker\logs |  |
| 10:53:58.832 | ⚠️ WARN | ArvindJobHunter.Worker | Worker:SharedSecret is not configured; a one-time secret was generated and printed to the console only. Configure the same Worker:SharedSecret for the worker and the API |  |
| 10:53:58.854 | ℹ️ INFO | Hosting.Lifetime | Now listening on: http://127.0.0.1:5310 |  |
| 10:53:58.854 | ℹ️ INFO | Hosting.Lifetime | Application started. Press Ctrl+C to shut down. |  |
| 10:53:58.854 | ℹ️ INFO | Hosting.Lifetime | Hosting environment: Production |  |
| 10:53:58.854 | ℹ️ INFO | Hosting.Lifetime | Content root path: D:\Job-Hunt-\src\ArvindJobHunter.Worker |  |
| 10:54:49.804 | ⚠️ WARN | ArvindJobHunter.Worker | Rejected GET /: missing or wrong X-Worker-Secret | `4c4c9c3f6a090b6df2929268c0ec44b5` |
| 10:54:49.805 | ⚠️ WARN | Logging.RequestLoggingMiddleware | GET / → 401 in 3.4 ms | `4c4c9c3f6a090b6df2929268c0ec44b5` |
