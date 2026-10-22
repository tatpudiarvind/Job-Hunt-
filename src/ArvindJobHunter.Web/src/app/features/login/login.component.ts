import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { describeError } from '../../core/auth.interceptor';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressBarModule } from 'primeng/progressbar';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ButtonModule, InputTextModule, ProgressBarModule, FormsModule, RouterLink],
  template: `
    <div class="wrap">
      <aside class="hero">
        <div class="logo"><span class="material-symbols-rounded">work</span></div>
        <h1>Arvind AI Job Hunter</h1>
        <p>An approval-first assistant that analyzes jobs, tailors your resume, and drafts outreach — and never acts externally without your explicit go-ahead.</p>
        <ul>
          <li><span class="material-symbols-rounded">lock</span> Runs locally. Single user. Your data stays on this machine.</li>
          <li><span class="material-symbols-rounded">fact_check</span> Every side effect is proposed, approved, then executed by you.</li>
          <li><span class="material-symbols-rounded">history</span> Complete audit trail of what the agent did and why.</li>
        </ul>
      </aside>
      <section class="card panel">
        @if (busy()) { <p-progressbar mode="indeterminate" styleClass="busy-bar" /> }
        <header class="card-header">
          <h2 class="card-heading">{{ configured() ? 'Welcome back' : 'No local account yet' }}</h2>
          <p class="muted">{{ configured() ? 'Sign in to continue.' : 'Create your single local account first, then sign in.' }}</p>
        </header>
        <div class="card-body">
          @if (!configured()) {
            <div class="empty stack">
              <div class="alert info"><span class="material-symbols-rounded">info</span><span>No local account is configured on this machine yet.</span></div>
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
                <label class="field-label" for="f-password">Password</label>
                <span class="material-symbols-rounded field-prefix">key</span>
                <input id="f-password" pInputText class="password-input" name="password" [type]="show() ? 'text' : 'password'" [(ngModel)]="password" autocomplete="current-password" required minlength="12" />
                <button pButton text rounded severity="secondary" data-icon class="field-suffix" type="button" (click)="show.set(!show())" [attr.aria-label]="show() ? 'Hide password' : 'Show password'">
                  <span class="material-symbols-rounded">{{ show() ? 'visibility_off' : 'visibility' }}</span>
                </button>
              </div>
              @if (message()) { <div class="alert error"><span class="material-symbols-rounded">error</span><span>{{ message() }}</span></div> }
              <button pButton type="submit" class="submit" [disabled]="busy()">Sign in</button>
            </form>
          }
        </div>
        <div class="card-actions actions">
          <a pButton text routerLink="/signup">Sign up</a>
          <a pButton text routerLink="/forgot-password">Forgot password?</a>
        </div>
      </section>
    </div>
  `,
  styles: [`
    .wrap { min-height: 100vh; display: grid; grid-template-columns: 1.1fr 1fr; }
    .hero { background: linear-gradient(160deg, #0f2a5a 0%, #1f5fbf 60%, #3b82f6 100%); color: #fff; padding: 4rem 3.5rem; display: flex; flex-direction: column; justify-content: center; gap: 1.25rem; }
    .hero h1 { font-size: 2rem; }
    .hero p { opacity: .9; line-height: 1.6; max-width: 36rem; }
    .hero ul { list-style: none; padding: 0; margin: 1rem 0 0; display: grid; gap: .9rem; }
    .hero li { display: flex; gap: .7rem; align-items: flex-start; opacity: .95; line-height: 1.5; }
    .hero li .material-symbols-rounded { flex: none; }
    .logo { width: 52px; height: 52px; border-radius: 14px; background: rgba(255,255,255,.15); display: grid; place-items: center; }
    .panel { align-self: center; justify-self: center; width: min(100%, 440px); margin: 2rem; overflow: hidden; }
    .stack { display: grid; gap: 1rem; }
    .empty { padding-top: .25rem; }
    .actions { padding: 0 1rem 1rem; gap: .25rem; }
    .alert { display: flex; gap: .5rem; align-items: flex-start; padding: .85rem 1rem; border-radius: 12px; }
    .alert.info { background: #eef4ff; color: #1f3f84; }
    :host ::ng-deep .password-input::-ms-reveal,
    :host ::ng-deep .password-input::-ms-clear { display: none; }
    :host ::ng-deep .password-input::-webkit-credentials-auto-fill-button,
    :host ::ng-deep .password-input::-webkit-textfield-decoration-container { margin-right: 0; }
    .card-header { padding-bottom: 1rem; }
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
      await this.auth.login(this.username, this.password);
      await this.router.navigateByUrl('/dashboard');
    } catch (e) {
      this.message.set(describeError(e) || 'Sign in failed.');
    } finally {
      this.busy.set(false);
    }
  }
}
