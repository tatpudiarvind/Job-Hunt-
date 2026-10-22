import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { describeError } from '../../core/auth.interceptor';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressBarModule } from 'primeng/progressbar';

@Component({
  selector: 'app-signup',
  standalone: true,
  imports: [ButtonModule, InputTextModule, ProgressBarModule, FormsModule, RouterLink],
  template: `
    <div class="wrap">
      <section class="card panel">
        @if (busy()) { <p-progressbar mode="indeterminate" styleClass="busy-bar" /> }
        <header class="card-header">
          <h2 class="card-heading">Create local account</h2>
          <p class="muted">This app supports one local account per machine. Pick a password with at least 12 characters.</p>
        </header>
        <div class="card-body">
          @if (configured()) {
            <div class="stack">
              <div class="alert info"><span class="material-symbols-rounded">info</span><span>A local account already exists on this machine. Sign in or use Forgot password to change it.</span></div>
              <a pButton routerLink="/login" class="submit">Back to sign in</a>
            </div>
          } @else {
            <form class="stack" (ngSubmit)="submit()">
              <div class="field">
                <label class="field-label" for="f-username">Username</label>
                <span class="material-symbols-rounded field-prefix">person</span>
                <input id="f-username" pInputText name="username" [(ngModel)]="username" autocomplete="username" required />
              </div>
              <div class="field">
                <label class="field-label" for="f-password">Password</label>
                <span class="material-symbols-rounded field-prefix">key</span>
                <input id="f-password" pInputText class="password-input" name="password" [type]="showPassword() ? 'text' : 'password'" [(ngModel)]="password" autocomplete="new-password" required minlength="12" />
                <button pButton text rounded severity="secondary" data-icon class="field-suffix" type="button" (click)="showPassword.set(!showPassword())" [attr.aria-label]="showPassword() ? 'Hide password' : 'Show password'">
                  <span class="material-symbols-rounded">{{ showPassword() ? 'visibility_off' : 'visibility' }}</span>
                </button>
                <small class="field-hint">Minimum 12 characters</small>
              </div>
              <div class="field">
                <label class="field-label" for="f-confirmPassword">Confirm password</label>
                <span class="material-symbols-rounded field-prefix">password</span>
                <input id="f-confirmPassword" pInputText class="password-input" name="confirmPassword" [type]="showConfirm() ? 'text' : 'password'" [(ngModel)]="confirmPassword" autocomplete="new-password" required minlength="12" />
                <button pButton text rounded severity="secondary" data-icon class="field-suffix" type="button" (click)="showConfirm.set(!showConfirm())" [attr.aria-label]="showConfirm() ? 'Hide password' : 'Show password'">
                  <span class="material-symbols-rounded">{{ showConfirm() ? 'visibility_off' : 'visibility' }}</span>
                </button>
              </div>
              @if (message()) { <div class="alert error"><span class="material-symbols-rounded">error</span><span>{{ message() }}</span></div> }
              <button pButton type="submit" class="submit" [disabled]="busy()">Create account & sign in</button>
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
    .alert.error { background: #fff1f2; color: #9f1239; }
    .submit { height: 46px; }
    :host ::ng-deep .password-input::-ms-reveal,
    :host ::ng-deep .password-input::-ms-clear { display: none; }
    :host ::ng-deep .password-input::-webkit-credentials-auto-fill-button,
    :host ::ng-deep .password-input::-webkit-textfield-decoration-container { margin-right: 0; }
  `]
})
export class SignupComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly configured = signal(true);
  readonly busy = signal(false);
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
      this.message.set('Passwords do not match.');
      return;
    }

    this.busy.set(true);
    this.message.set('');
    try {
      await this.auth.signup(this.username, this.password);
      await this.auth.login(this.username, this.password);
      await this.router.navigateByUrl('/dashboard');
    } catch (e) {
      this.message.set(describeError(e) || 'Sign up failed.');
      const status = await this.auth.status().catch(() => null);
      if (status) this.configured.set(status.configured);
    } finally {
      this.busy.set(false);
    }
  }
}
