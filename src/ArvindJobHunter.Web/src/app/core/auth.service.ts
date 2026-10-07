import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthStatus, Session } from './models';

const TOKEN_KEY = 'ajh.session';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  readonly token = signal<string | null>(sessionStorage.getItem(TOKEN_KEY));

  get isAuthenticated(): boolean { return this.token() !== null; }

  status(): Promise<AuthStatus> {
    return firstValueFrom(this.http.get<AuthStatus>(`${environment.apiBaseUrl}/api/auth/status`));
  }

  async setup(username: string, password: string): Promise<void> {
    await firstValueFrom(this.http.post<void>(`${environment.apiBaseUrl}/api/auth/setup`, { username, password }));
  }

  async signup(username: string, password: string): Promise<void> {
    await firstValueFrom(this.http.post<void>(`${environment.apiBaseUrl}/api/auth/signup`, { username, password }));
  }

  async resetPassword(username: string, newPassword: string): Promise<void> {
    await firstValueFrom(this.http.post<void>(`${environment.apiBaseUrl}/api/auth/reset-password`, { username, newPassword }));
  }

  async login(username: string, password: string): Promise<void> {
    const session = await firstValueFrom(this.http.post<Session>(`${environment.apiBaseUrl}/api/auth/login`, { username, password }));
    sessionStorage.setItem(TOKEN_KEY, session.token);
    this.token.set(session.token);
  }

  async logout(): Promise<void> {
    try { await firstValueFrom(this.http.post<void>(`${environment.apiBaseUrl}/api/auth/logout`, {})); } catch { /* session may already be gone */ }
    this.clear();
  }

  clear(): void {
    sessionStorage.removeItem(TOKEN_KEY);
    this.token.set(null);
    void this.router.navigateByUrl('/login');
  }
}
