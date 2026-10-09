import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from './auth.service';
import { environment } from '../../environments/environment';

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
  });

  afterEach(() => http.verify());

  it('starts unauthenticated when no token is stored', () => {
    expect(service.isAuthenticated).toBe(false);
    expect(service.token()).toBeNull();
  });

  it('signup posts credentials to the signup endpoint', async () => {
    const pending = service.signup('arvind', 'correct-horse-battery-staple');
    const req = http.expectOne(`${environment.apiBaseUrl}/api/auth/signup`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ username: 'arvind', password: 'correct-horse-battery-staple' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    await pending;
  });

  it('resetPassword posts the new password payload to the reset endpoint', async () => {
    const pending = service.resetPassword('arvind', 'new-correct-horse-battery-staple');
    const req = http.expectOne(`${environment.apiBaseUrl}/api/auth/reset-password`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ username: 'arvind', newPassword: 'new-correct-horse-battery-staple' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    await pending;
  });

  it('login stores the session token in sessionStorage and the signal', async () => {
    const pending = service.login('arvind', 'secret');
    const req = http.expectOne(`${environment.apiBaseUrl}/api/auth/login`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ username: 'arvind', password: 'secret' });
    req.flush({ token: 'tok-123', expiresAt: '2099-01-01T00:00:00Z' });
    await pending;

    expect(service.isAuthenticated).toBe(true);
    expect(service.token()).toBe('tok-123');
    expect(sessionStorage.getItem('ajh.session')).toBe('tok-123');
  });

  it('logout clears the token even when the API call fails and redirects to login', async () => {
    sessionStorage.setItem('ajh.session', 'tok');
    service.token.set('tok');

    const pending = service.logout();
    http.expectOne(`${environment.apiBaseUrl}/api/auth/logout`).flush('gone', { status: 401, statusText: 'Unauthorized' });
    await pending;

    expect(service.isAuthenticated).toBe(false);
    expect(sessionStorage.getItem('ajh.session')).toBeNull();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
  });
});
