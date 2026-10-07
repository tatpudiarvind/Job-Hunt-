import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { environment } from '../../environments/environment';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const token = auth.token();
  const isApi = req.url.startsWith(environment.apiBaseUrl);
  const request = token && isApi ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;
  return next(request).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401 && isApi && !req.url.includes('/api/auth/')) auth.clear();
      return throwError(() => error);
    })
  );
};

export function describeError(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) return 'The local API is unavailable. Start the API and try again.';
    const body = error.error;
    if (body && typeof body === 'object') {
      if (typeof body['error'] === 'string') return body['error'];
      if (typeof body['detail'] === 'string') return body['detail'];
      if (typeof body['title'] === 'string') return body['title'];
      if (body['errors'] && typeof body['errors'] === 'object') return Object.values(body['errors'] as Record<string, string[]>).flat().join(' ');
    }
    return `${error.status} ${error.statusText}`;
  }
  return 'Unexpected error.';
}
