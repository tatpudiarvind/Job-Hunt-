import { Injectable } from '@angular/core';
import { environment } from '../../environments/environment';

export type ClientLogLevel = 'error' | 'warning' | 'info';

/**
 * Sends unexpected browser errors to the API, which writes them into the same Markdown log as server events.
 * Uses fetch (not HttpClient) so reporting never re-enters interceptors or the error handler, and it is
 * deduplicated and rate limited so an error loop cannot flood the log. Failures to report are ignored.
 */
@Injectable({ providedIn: 'root' })
export class ClientLogService {
  static readonly maxReportsPerMinute = 10;
  static readonly duplicateWindowMs = 10_000;

  private readonly recent = new Map<string, number>();
  private sentTimestamps: number[] = [];

  report(level: ClientLogLevel, message: string, stack?: string | null): void {
    const text = (message || '').trim();
    if (!text || typeof fetch !== 'function') return;

    const now = Date.now();
    const key = `${level}|${text}`;
    const last = this.recent.get(key);
    if (last !== undefined && now - last < ClientLogService.duplicateWindowMs) return;
    this.sentTimestamps = this.sentTimestamps.filter(t => now - t < 60_000);
    if (this.sentTimestamps.length >= ClientLogService.maxReportsPerMinute) return;

    this.recent.set(key, now);
    this.sentTimestamps.push(now);
    const body = JSON.stringify({
      level,
      message: text.slice(0, 2000),
      url: typeof location !== 'undefined' ? location.href.slice(0, 500) : null,
      stack: stack ? stack.slice(0, 8000) : null
    });
    try {
      void fetch(`${environment.apiBaseUrl}/api/client-logs`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body, keepalive: true })
        .catch(() => undefined);
    } catch {
      // Reporting is best effort; never let it throw.
    }
  }
}
