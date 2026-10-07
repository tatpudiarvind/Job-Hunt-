import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { AuthService } from '../../core/auth.service';
import { describeError } from '../../core/auth.interceptor';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule, MatProgressBarModule],
  template: `
    <div class="wrap">
      <aside class="hero">
        <div class="logo"><mat-icon class="material-symbols-rounded">work</mat-icon></div>
        <h1>Arvind AI Job Hunter</h1>
        <p>An approval-first assistant that analyzes jobs, tailors your resume, and drafts outreach — and never acts externally without your explicit go-ahead.</p>
        <ul>
          <li><mat-icon class="material-symbols-rounded">lock</mat-icon> Runs locally. Single user. Your data stays on this machine.</li>
          <li><mat-icon class="material-symbols-rounded">fact_check</mat-icon> Every side effect is proposed, approved, then executed by you.</li>
          <li><mat-icon class="material-symbols-rounded">history</mat-icon> Complete audit trail of what the agent did and why.</li>
        </ul>
      </aside>
      <mat-card class="panel" appearance="outlined">
        @if (busy()) { <mat-progress-bar mode="indeterminate" /> }
        <mat-card-header>
          <mat-card-title>{{ configured() ? 'Welcome back' : 'Create your local account' }}</mat-card-title>
          <mat-card-subtitle>{{ configured() ? 'Sign in to continue.' : 'This is the only account; choose a password of at least 12 characters.' }}</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          <form class="stack" (ngSubmit)="submit()">
            <mat-form-field appearance="outline">
              <mat-label>Username</mat-label>
              <mat-icon matPrefix class="material-symbols-rounded">person</mat-icon>
              <input matInput name="username" [(ngModel)]="username" autocomplete="username" required />
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>Password</mat-label>
              <mat-icon matPrefix class="material-symbols-rounded">key</mat-icon>
              <input matInput name="password" [type]="show() ? 'text' : 'password'" [(ngModel)]="password" autocomplete="current-password" required minlength="12" />
              <button mat-icon-button matSuffix type="button" (click)="show.set(!show())" [attr.aria-label]="show() ? 'Hide password' : 'Show password'">
                <mat-icon class="material-symbols-rounded">{{ show() ? 'visibility_off' : 'visibility' }}</mat-icon>
              </button>
              @if (!configured()) { <mat-hint>Minimum 12 characters</mat-hint> }
            </mat-form-field>
            @if (message()) { <div class="alert error"><mat-icon class="material-symbols-rounded">error</mat-icon><span>{{ message() }}</span></div> }
            <button mat-flat-button color="primary" type="submit" class="submit" [disabled]="busy()">{{ configured() ? 'Sign in' : 'Create account & sign in' }}</button>
          </form>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .wrap { min-height: 100vh; display: grid; grid-template-columns: 1.1fr 1fr; }
    .hero { background: linear-gradient(160deg, #0f2a5a 0%, #1f5fbf 60%, #3b82f6 100%); color: #fff; padding: 4rem 3.5rem; display: flex; flex-direction: column; justify-content: center; gap: 1.25rem; }
    .hero h1 { font-size: 2rem; }
    .hero p { opacity: .9; line-height: 1.6; max-width: 36rem; }
    .hero ul { list-style: none; padding: 0; margin: 1rem 0 0; display: grid; gap: .9rem; }
    .hero li { display: flex; gap: .7rem; align-items: flex-start; opacity: .95; line-height: 1.5; }
    .hero li mat-icon { flex: none; }
    .logo { width: 52px; height: 52px; border-radius: 14px; background: rgba(255,255,255,.15); display: grid; place-items: center; }
    .panel { align-self: center; justify-self: center; width: min(100%, 440px); margin: 2rem; overflow: hidden; }
    mat-card-header { padding-bottom: 1rem; }
    .submit { height: 46px; }
    @media (max-width: 880px) { .wrap { grid-template-columns: 1fr; } .hero { padding: 2.5rem 1.5rem; } }
  `]
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly configured = signal(true);
  readonly busy = signal(false);
  readonly show = signal(false);
  readonly message = signal('');
  username = '';
  password = '';

  constructor() {
    if (this.auth.isAuthenticated) void this.router.navigateByUrl('/dashboard');
    this.auth.status().then(s => this.configured.set(s.configured)).catch(e => this.message.set(describeError(e)));
  }

  async submit(): Promise<void> {
    this.busy.set(true);
    this.message.set('');
    try {
      if (!this.configured()) {
        await this.auth.setup(this.username, this.password);
        this.configured.set(true);
      }
      await this.auth.login(this.username, this.password);
      await this.router.navigateByUrl('/dashboard');
    } catch (e) {
      this.message.set(describeError(e) || 'Setup requires a username and a password of at least 12 characters.');
    } finally {
      this.busy.set(false);
    }
  }
}
