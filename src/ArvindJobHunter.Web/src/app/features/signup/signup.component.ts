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
  templateUrl: './signup.component.html',
  styleUrl: './signup.component.scss'
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
