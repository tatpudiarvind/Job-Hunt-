import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { describeError } from '../../core/auth.interceptor';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressBarModule } from 'primeng/progressbar';

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [ButtonModule, InputTextModule, ProgressBarModule, FormsModule, RouterLink],
  template: `
    <div class="wrap">
      <section class="card panel">
        @if (busy()) { <p-progressbar mode="indeterminate" styleClass="busy-bar" /> }
        <header class="card-header">
          <h2 class="card-heading">Forgot password</h2>
          <p class="muted">Reset the single local account password stored for this machine.</p>
        </header>
        <div class="card-body">
          @if (!configured()) {
            <div class="stack">
              <div class="alert info"><span class="material-symbols-rounded">info</span><span>No local account exists yet. Create one first.</span></div>
              <a pButton routerLink="/signup" class="submit">Open sign up</a>
            </div>
          } @else {
            <form class="stack" (ngSubmit)="submit()">
              <div class="field">
                <label class="field-label" for="f-username">Username</label>
                <span class="material-symbols-rounded field-prefix">person</span>
                <input id="f-username" pInputText name="username" [(ngModel)]="username" autocomplete="username" required />
              </div>
              <div class="field">
                <label class="field-label" for="f-currentPassword">Current password</label>
                <span class="material-symbols-rounded field-prefix">lock</span>
                <input id="f-currentPassword" pInputText class="password-input" name="currentPassword" [type]="showCurrentPassword() ? 'text' : 'password'" [(ngModel)]="currentPassword" autocomplete="current-password" required />
                <button pButton text rounded severity="secondary" data-icon class="field-suffix" type="button" (click)="showCurrentPassword.set(!showCurrentPassword())" [attr.aria-label]="showCurrentPassword() ? 'Hide password' : 'Show password'">
                  <span class="material-symbols-rounded">{{ showCurrentPassword() ? 'visibility_off' : 'visibility' }}</span>
                </button>
              </div>
              <div class="field">
                <label class="field-label" for="f-password">New password</label>
                <span class="material-symbols-rounded field-prefix">key</span>
                <input id="f-password" pInputText class="password-input" name="password" [type]="showPassword() ? 'text' : 'password'" [(ngModel)]="password" autocomplete="new-password" required minlength="12" />
                <button pButton text rounded severity="secondary" data-icon class="field-suffix" type="button" (click)="showPassword.set(!showPassword())" [attr.aria-label]="showPassword() ? 'Hide password' : 'Show password'">
                  <span class="material-symbols-rounded">{{ showPassword() ? 'visibility_off' : 'visibility' }}</span>
                </button>
                <small class="field-hint">Minimum 12 characters</small>
              </div>
              <div class="field">
                <label class="field-label" for="f-confirmPassword">Confirm new password</label>
                <span class="material-symbols-rounded field-prefix">password</span>
                <input id="f-confirmPassword" pInputText class="password-input" name="confirmPassword" [type]="showConfirm() ? 'text' : 'password'" [(ngModel)]="confirmPassword" autocomplete="new-password" required minlength="12" />
                <button pButton text rounded severity="secondary" data-icon class="field-suffix" type="button" (click)="showConfirm.set(!showConfirm())" [attr.aria-label]="showConfirm() ? 'Hide password' : 'Show password'">
                  <span class="material-symbols-rounded">{{ showConfirm() ? 'visibility_off' : 'visibility' }}</span>
                </button>
              </div>
              @if (message()) { <div class="alert" [class.success]="success()" [class.error]="!success()"><span class="material-symbols-rounded">{{ success() ? 'check_circle' : 'error' }}</span><span>{{ message() }}</span></div> }
              <button pButton type="submit" class="submit" [disabled]="busy()">Save new password</button>
            </form>
          }
        </div>
        <div class="card-actions actions">
          <a pButton text routerLink="/login">Back to sign in</a>
        </div>
      </section>
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
  readonly showCurrentPassword = signal(false);
  readonly showPassword = signal(false);
  readonly showConfirm = signal(false);
  readonly message = signal('');
  username = '';
  currentPassword = '';
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
      await this.auth.resetPassword(this.username, this.currentPassword, this.password);
      this.success.set(true);
      this.message.set('Password updated. Sign in with your new password.');
      this.currentPassword = '';
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
