import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { BreakpointObserver } from '@angular/cdk/layout';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDividerModule } from '@angular/material/divider';
import { AuthService } from '../core/auth.service';
import { SettingsStateService } from '../core/settings-state.service';
import { StatusChipComponent } from '../shared/status-chip.component';

interface NavItem { path: string; label: string; icon: string; }

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatSidenavModule, MatToolbarModule, MatListModule, MatIconModule, MatButtonModule, MatMenuModule, MatTooltipModule, MatDividerModule, StatusChipComponent],
  template: `
    <mat-sidenav-container class="container">
      <mat-sidenav #nav class="sidenav" [mode]="isHandset() ? 'over' : 'side'" [opened]="!isHandset()" [fixedInViewport]="isHandset()">
        <div class="brand">
          <div class="logo"><mat-icon class="material-symbols-rounded">work</mat-icon></div>
          <div>
            <b>Job Hunter</b>
            <span class="muted small">Approval-first · local</span>
          </div>
        </div>
        <mat-nav-list>
          @for (item of primary; track item.path) {
            <a mat-list-item [routerLink]="item.path" routerLinkActive="active" (click)="isHandset() && nav.close()">
              <mat-icon matListItemIcon class="material-symbols-rounded">{{ item.icon }}</mat-icon>
              <span matListItemTitle>{{ item.label }}</span>
            </a>
          }
          <mat-divider class="divider" />
          @for (item of secondary; track item.path) {
            <a mat-list-item [routerLink]="item.path" routerLinkActive="active" (click)="isHandset() && nav.close()">
              <mat-icon matListItemIcon class="material-symbols-rounded">{{ item.icon }}</mat-icon>
              <span matListItemTitle>{{ item.label }}</span>
            </a>
          }
        </mat-nav-list>
        <div class="sidenav-footer muted small">
          <mat-icon class="material-symbols-rounded">verified_user</mat-icon>
          Nothing leaves this machine without your approval.
        </div>
      </mat-sidenav>

      <mat-sidenav-content>
        <mat-toolbar class="toolbar">
          @if (isHandset()) {
            <button mat-icon-button (click)="nav.toggle()" aria-label="Toggle navigation"><mat-icon class="material-symbols-rounded">menu</mat-icon></button>
          }
          <span class="grow"></span>
          @if (settings(); as s) {
            <a routerLink="/settings" class="mode" matTooltip="Execution mode and AI provider. Change in Settings.">
              <app-status-chip [status]="s.mode" />
              <span class="chip">AI · {{ s.llmDisplayName }}</span>
            </a>
          }
          <button mat-icon-button [matMenuTriggerFor]="menu" aria-label="Account"><mat-icon class="material-symbols-rounded">account_circle</mat-icon></button>
          <mat-menu #menu="matMenu">
            <a mat-menu-item routerLink="/profile"><mat-icon class="material-symbols-rounded">person</mat-icon>Profile</a>
            <a mat-menu-item routerLink="/settings"><mat-icon class="material-symbols-rounded">settings</mat-icon>Settings</a>
            <mat-divider />
            <button mat-menu-item (click)="logout()"><mat-icon class="material-symbols-rounded">logout</mat-icon>Sign out</button>
          </mat-menu>
        </mat-toolbar>
        <main class="main"><router-outlet /></main>
      </mat-sidenav-content>
    </mat-sidenav-container>
  `,
  styles: [`
    .container { height: 100vh; }
    .sidenav { width: 248px; border-right: 1px solid var(--ajh-border); background: var(--ajh-surface); display: flex; flex-direction: column; }
    .brand { display: flex; align-items: center; gap: .75rem; padding: 1.25rem 1.25rem 1rem; }
    .brand b { display: block; font-size: 1rem; }
    .brand span { display: block; }
    .logo { width: 40px; height: 40px; border-radius: 12px; background: linear-gradient(135deg, #1f5fbf, #3b82f6); display: grid; place-items: center; color: #fff; }
    mat-nav-list { padding: 0 .75rem; flex: 1; }
    .divider { margin: .75rem 0; }
    a[mat-list-item] { border-radius: 10px; margin-bottom: 2px; }
    a.active { background: var(--ajh-primary-soft); color: var(--ajh-primary); }
    a.active mat-icon { color: var(--ajh-primary); font-variation-settings: 'FILL' 1; }
    .sidenav-footer { display: flex; gap: .5rem; align-items: flex-start; padding: 1rem 1.25rem 1.25rem; border-top: 1px solid var(--ajh-border); line-height: 1.4; }
    .sidenav-footer mat-icon { font-size: 1.1rem; width: 1.1rem; height: 1.1rem; color: var(--ajh-success); flex: none; }
    .toolbar { background: rgba(244,246,249,.85); backdrop-filter: blur(8px); border-bottom: 1px solid var(--ajh-border); position: sticky; top: 0; z-index: 2; gap: .5rem; }
    .mode { display: inline-flex; gap: .35rem; align-items: center; text-decoration: none; }
    .main { padding: 1.75rem 2rem 3rem; }
    @media (max-width: 720px) { .main { padding: 1.25rem 1rem 2rem; } }
  `]
})
export class ShellComponent {
  private readonly auth = inject(AuthService);
  private readonly settingsState = inject(SettingsStateService);
  private readonly breakpoints = inject(BreakpointObserver);
  readonly settings = this.settingsState.settings;
  readonly isHandset = toSignal(this.breakpoints.observe('(max-width: 960px)').pipe(map(r => r.matches)), { initialValue: false });

  readonly primary: NavItem[] = [
    { path: '/dashboard', label: 'Dashboard', icon: 'space_dashboard' },
    { path: '/jobs', label: 'Jobs', icon: 'work' },
    { path: '/approvals', label: 'Approvals', icon: 'fact_check' },
    { path: '/applications', label: 'Applications', icon: 'assignment_turned_in' },
    { path: '/emails', label: 'Emails', icon: 'mail' },
    { path: '/interview-prep', label: 'Interview prep', icon: 'school' }
  ];
  readonly secondary: NavItem[] = [
    { path: '/profile', label: 'Profile', icon: 'person' },
    { path: '/activity', label: 'Activity', icon: 'history' },
    { path: '/settings', label: 'Settings', icon: 'settings' }
  ];

  constructor() { void this.settingsState.load().catch(() => undefined); }

  logout(): void { void this.auth.logout(); }
}
