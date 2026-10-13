import { ErrorHandler, Injectable, inject } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ClientLogService } from './client-log.service';

/**
 * Global handler for errors nothing else caught (template/runtime errors, unhandled promise rejections).
 * Keeps the console output and forwards the error to the API's Markdown log. HTTP errors are skipped:
 * the API already logged those requests itself.
 */
@Injectable()
export class GlobalErrorHandler implements ErrorHandler {
  private readonly clientLog = inject(ClientLogService);

  handleError(error: unknown): void {
    console.error(error);
    const actual = unwrap(error);
    if (actual instanceof HttpErrorResponse) return;

    if (actual instanceof Error) {
      this.clientLog.report('error', `${actual.name}: ${actual.message}`, actual.stack);
    } else {
      this.clientLog.report('error', typeof actual === 'string' ? actual : safeStringify(actual));
    }
  }
}

/** Zone.js wraps unhandled promise rejections as { rejection }. */
function unwrap(error: unknown): unknown {
  if (error && typeof error === 'object' && 'rejection' in error) return (error as { rejection: unknown }).rejection;
  return error;
}

function safeStringify(value: unknown): string {
  try { return JSON.stringify(value) ?? String(value); } catch { return String(value); }
}
