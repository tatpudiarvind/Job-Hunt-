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
  templateUrl: './forgot-password.component.html',
  styleUrl: './forgot-password.component.scss'
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
