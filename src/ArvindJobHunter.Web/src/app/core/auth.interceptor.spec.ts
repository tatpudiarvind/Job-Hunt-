import { TestBed } from '@angular/core/testing';
import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { authInterceptor, describeError } from './auth.interceptor';
import { AuthService } from './auth.service';
import { environment } from '../../environments/environment';

describe('authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting(), provideRouter([])]
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });

  afterEach(() => backend.verify());

  it('adds a Bearer header to API requests when a token exists', () => {
    auth.token.set('abc');
    http.get(`${environment.apiBaseUrl}/api/jobs`).subscribe();
    const req = backend.expectOne(`${environment.apiBaseUrl}/api/jobs`);
    expect(req.request.headers.get('Authorization')).toBe('Bearer abc');
    req.flush([]);
  });

  it('does not attach the token to non-API requests', () => {
    auth.token.set('abc');
    http.get('https://example.com/data').subscribe();
    const req = backend.expectOne('https://example.com/data');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });

  it('clears the session on 401 from a protected API endpoint', async () => {
    auth.token.set('abc');
    const pending = firstValueFrom(http.get(`${environment.apiBaseUrl}/api/jobs`));
    backend.expectOne(`${environment.apiBaseUrl}/api/jobs`).flush('', { status: 401, statusText: 'Unauthorized' });
    await expect(pending).rejects.toBeInstanceOf(HttpErrorResponse);
    expect(auth.token()).toBeNull();
  });

  it('keeps the session on 401 from auth endpoints (bad password)', async () => {
    auth.token.set('abc');
    const pending = firstValueFrom(http.post(`${environment.apiBaseUrl}/api/auth/login`, {}));
    backend.expectOne(`${environment.apiBaseUrl}/api/auth/login`).flush('', { status: 401, statusText: 'Unauthorized' });
    await expect(pending).rejects.toBeInstanceOf(HttpErrorResponse);
    expect(auth.token()).toBe('abc');
  });
});

describe('describeError', () => {
  it('explains an unreachable API', () => {
    expect(describeError(new HttpErrorResponse({ status: 0 }))).toContain('API is unavailable');
  });

  it('prefers error, then detail, then title, then validation errors', () => {
    expect(describeError(new HttpErrorResponse({ status: 400, error: { error: 'boom' } }))).toBe('boom');
    expect(describeError(new HttpErrorResponse({ status: 400, error: { detail: 'd' } }))).toBe('d');
    expect(describeError(new HttpErrorResponse({ status: 400, error: { title: 't' } }))).toBe('t');
    expect(describeError(new HttpErrorResponse({ status: 400, error: { errors: { a: ['x'], b: ['y'] } } }))).toBe('x y');
  });

  it('falls back to status text and a generic message', () => {
    expect(describeError(new HttpErrorResponse({ status: 500, statusText: 'Server Error' }))).toBe('500 Server Error');
    expect(describeError(new Error('x'))).toBe('Unexpected error.');
  });
});
