import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { AuthService } from '../../core/auth.service';
import { describeError } from '../../core/auth.interceptor';

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [FormsModule, RouterLink, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule, MatProgressBarModule],
  template: `
    <div class="wrap">
      <mat-card class="panel" appearance="outlined">
        @if (busy()) { <mat-progress-bar mode="indeterminate" /> }
        <mat-card-header>
          <mat-card-title>Forgot password</mat-card-title>
          <mat-card-subtitle>Reset the single local account password stored for this machine.</mat-card-subtitle>
        </mat-card-header>
        <mat-card-content>
          @if (!configured()) {
            <div class="stack">
              <div class="alert info"><mat-icon class="material-symbols-rounded">info</mat-icon><span>No local account exists yet. Create one first.</span></div>
              <a mat-flat-button color="primary" routerLink="/signup" class="submit">Open sign up</a>
            </div>
          } @else {
            <form class="stack" (ngSubmit)="submit()">
              <mat-form-field appearance="outline">
                <mat-label>Username</mat-label>
                <mat-icon matPrefix class="material-symbols-rounded">person</mat-icon>
                <input matInput name="username" [(ngModel)]="username" autocomplete="username" required />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>New password</mat-label>
                <mat-icon matPrefix class="material-symbols-rounded">key</mat-icon>
                <input matInput class="password-input" name="password" [type]="showPassword() ? 'text' : 'password'" [(ngModel)]="password" autocomplete="new-password" required minlength="12" />
                <button mat-icon-button matSuffix type="button" (click)="showPassword.set(!showPassword())" [attr.aria-label]="showPassword() ? 'Hide password' : 'Show password'">
                  <mat-icon class="material-symbols-rounded">{{ showPassword() ? 'visibility_off' : 'visibility' }}</mat-icon>
                </button>
                <mat-hint>Minimum 12 characters</mat-hint>
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>Confirm new password</mat-label>
                <mat-icon matPrefix class="material-symbols-rounded">password</mat-icon>
                <input matInput class="password-input" name="confirmPassword" [type]="showConfirm() ? 'text' : 'password'" [(ngModel)]="confirmPassword" autocomplete="new-password" required minlength="12" />
                <button mat-icon-button matSuffix type="button" (click)="showConfirm.set(!showConfirm())" [attr.aria-label]="showConfirm() ? 'Hide password' : 'Show password'">
                  <mat-icon class="material-symbols-rounded">{{ showConfirm() ? 'visibility_off' : 'visibility' }}</mat-icon>
                </button>
              </mat-form-field>
              @if (message()) { <div class="alert" [class.success]="success()" [class.error]="!success()"><mat-icon class="material-symbols-rounded">{{ success() ? 'check_circle' : 'error' }}</mat-icon><span>{{ message() }}</span></div> }
              <button mat-flat-button color="primary" type="submit" class="submit" [disabled]="busy()">Save new password</button>
            </form>
          }
        </mat-card-content>
        <mat-card-actions align="end" class="actions">
          <a mat-button routerLink="/login">Back to sign in</a>
        </mat-card-actions>
      </mat-card>
    </div>
  `,
  styles: [`
    .wrap { min-height: 100vh; display: grid; place-items: center; padding: 2rem; background: linear-gradient(160deg, #f4f7fb 0%, #eef4ff 100%); }
    .panel { width: min(100%, 480px); overflow: hidden; }
    .stack { display: grid; gap: 1rem; }
    .actions { padding: 0 1rem 1rem; }
    .alert { display: flex; gap: .5rem; align-items: flex-start; padding: .85rem 1rem; border-radius: 12px; }
    .alert.info { background: #eef4ff; color: #1f3f84; }
    .alert.success { background: #ecfdf5; color: #166534; }
    .alert.error { background: #fff1f2; color: #9f1239; }
    .submit { height: 46px; }
    :host ::ng-deep .password-input::-ms-reveal,
    :host ::ng-deep .password-input::-ms-clear { display: none; }
    :host ::ng-deep .password-input::-webkit-credentials-auto-fill-button,
    :host ::ng-deep .password-input::-webkit-textfield-decoration-container { margin-right: 0; }
  `]
})
export class ForgotPasswordComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly configured = signal(true);
  readonly busy = signal(false);
  readonly success = signal(false);
  readonly showPassword = signal(false);
  readonly showConfirm = signal(false);
  readonly message = signal('');
  username = '';
  password = '';
  confirmPassword = '';

  constructor() {
    if (this.auth.isAuthenticated) void this.router.navigateByUrl('/dashboard');
    this.auth.status().then(s => this.configured.set(s.configured)).catch(e => this.message.set(describeError(e)));
  }

  async submit(): Promise<void> {
    if (this.password !== this.confirmPassword) {
      this.success.set(false);
      this.message.set('Passwords do not match.');
      return;
    }

    this.busy.set(true);
    this.success.set(false);
    this.message.set('');
    try {
      await this.auth.resetPassword(this.username, this.password);
      this.success.set(true);
      this.message.set('Password updated. Sign in with your new password.');
      this.password = '';
      this.confirmPassword = '';
      setTimeout(() => void this.router.navigateByUrl('/login'), 800);
    } catch (e) {
      this.success.set(false);
      this.message.set(describeError(e) || 'Password reset failed.');
    } finally {
      this.busy.set(false);
    }
  }
}
