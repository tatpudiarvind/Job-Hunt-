import { Component, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { BreakpointObserver } from '@angular/cdk/layout';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';
import { MenuItem } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { DrawerModule } from 'primeng/drawer';
import { MenuModule } from 'primeng/menu';
import { TooltipModule } from 'primeng/tooltip';
import { DividerModule } from 'primeng/divider';
import { AuthService } from '../core/auth.service';
import { SettingsStateService } from '../core/settings-state.service';
import { StatusChipComponent } from '../shared/status-chip.component';

interface NavItem { path: string; label: string; icon: string; }

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, NgTemplateOutlet, ButtonModule, DrawerModule, MenuModule, TooltipModule, DividerModule, StatusChipComponent],
  template: `
    <ng-template #navContent>
        <div class="brand">
          <div class="logo"><span class="material-symbols-rounded">work</span></div>
          <div>
            <b>Job Hunter</b>
            <span class="muted small">Approval-first · local</span>
          </div>
        </div>
        <nav class="nav-list">
          @for (item of primary; track item.path) {
            <a class="nav-item" [routerLink]="item.path" routerLinkActive="active" (click)="navOpen.set(false)">
              <span class="material-symbols-rounded">{{ item.icon }}</span>
              <span>{{ item.label }}</span>
            </a>
          }
          <p-divider styleClass="divider" />
          @for (item of secondary; track item.path) {
            <a class="nav-item" [routerLink]="item.path" routerLinkActive="active" (click)="navOpen.set(false)">
              <span class="material-symbols-rounded">{{ item.icon }}</span>
              <span>{{ item.label }}</span>
            </a>
          }
        </nav>
        <div class="sidenav-footer muted small">
          <span class="material-symbols-rounded">verified_user</span>
          Nothing leaves this machine without your approval.
        </div>
    </ng-template>

    <div class="container" [class.handset]="isHandset()">
      @if (isHandset()) {
        <p-drawer [(visible)]="navOpen" [showCloseIcon]="false" styleClass="nav-drawer" ariaLabel="Navigation">
          <div class="sidenav"><ng-container [ngTemplateOutlet]="navContent" /></div>
        </p-drawer>
      } @else {
        <aside class="sidenav"><ng-container [ngTemplateOutlet]="navContent" /></aside>
      }

      <div class="content">
        <header class="toolbar">
          @if (isHandset()) {
            <p-button [text]="true" [rounded]="true" severity="secondary" ariaLabel="Toggle navigation" (onClick)="navOpen.set(!navOpen())">
              <span class="material-symbols-rounded">menu</span>
            </p-button>
          }
          <span class="grow"></span>
          @if (settings(); as s) {
            <a routerLink="/settings" class="mode" pTooltip="Execution mode and AI provider. Change in Settings." tooltipPosition="bottom">
              <app-status-chip [status]="s.mode" />
              <span class="chip">AI · {{ s.llmDisplayName }}</span>
            </a>
          }
          <p-button [text]="true" [rounded]="true" severity="secondary" ariaLabel="Account" (onClick)="menu.toggle($event)">
            <span class="material-symbols-rounded">account_circle</span>
          </p-button>
          <p-menu #menu [model]="accountMenu" [popup]="true" appendTo="body">
            <ng-template #item let-item>
              <a class="p-menu-item-link menu-link" [routerLink]="item.routerLink">
                <span class="material-symbols-rounded">{{ item.icon }}</span>{{ item.label }}
              </a>
            </ng-template>
          </p-menu>
        </header>
        <main class="main"><router-outlet /></main>
      </div>
    </div>
  `,
  styles: [`
    .container { height: 100vh; display: grid; grid-template-columns: 248px 1fr; }
    .container.handset { grid-template-columns: 1fr; }
    .sidenav { width: 248px; height: 100%; border-right: 1px solid var(--ajh-border); background: var(--ajh-surface); display: flex; flex-direction: column; overflow-y: auto; }
    .content { overflow-y: auto; height: 100vh; }
    .brand { display: flex; align-items: center; gap: .75rem; padding: 1.25rem 1.25rem 1rem; }
    .brand b { display: block; font-size: 1rem; }
    .brand span { display: block; }
    .logo { width: 40px; height: 40px; border-radius: 12px; background: linear-gradient(135deg, #1f5fbf, #3b82f6); display: grid; place-items: center; color: #fff; }
    .nav-list { padding: 0 .75rem; flex: 1; display: grid; align-content: start; gap: 2px; }
    :host ::ng-deep .divider { margin: .75rem 0; }
    .nav-item { display: flex; align-items: center; gap: 1rem; height: 48px; padding: 0 1rem; border-radius: 10px; color: var(--ajh-text); text-decoration: none; font-weight: 500; }
    .nav-item:hover { background: #f1f4f8; text-decoration: none; }
    .nav-item .material-symbols-rounded { color: var(--ajh-muted); }
    .nav-item.active { background: var(--ajh-primary-soft); color: var(--ajh-primary); }
    .nav-item.active .material-symbols-rounded { color: var(--ajh-primary); font-variation-settings: 'FILL' 1; }
    .sidenav-footer { display: flex; gap: .5rem; align-items: flex-start; padding: 1rem 1.25rem 1.25rem; border-top: 1px solid var(--ajh-border); line-height: 1.4; }
    .sidenav-footer .material-symbols-rounded { font-size: 1.1rem; color: var(--ajh-success); flex: none; }
    .toolbar { height: 64px; padding: 0 16px; display: flex; align-items: center; background: rgba(244,246,249,.85); backdrop-filter: blur(8px); border-bottom: 1px solid var(--ajh-border); position: sticky; top: 0; z-index: 2; gap: .5rem; }
    .mode { display: inline-flex; gap: .35rem; align-items: center; text-decoration: none; }
    .menu-link { display: flex; align-items: center; gap: .75rem; color: inherit; text-decoration: none; }
    .main { padding: 1.75rem 2rem 3rem; }
    :host ::ng-deep .nav-drawer { width: 248px !important; }
    :host ::ng-deep .nav-drawer .p-drawer-content { padding: 0; }
    :host ::ng-deep .nav-drawer .p-drawer-header { display: none; }
    @media (max-width: 720px) { .main { padding: 1.25rem 1rem 2rem; } }
  `]
})
export class ShellComponent {
  private readonly auth = inject(AuthService);
  private readonly settingsState = inject(SettingsStateService);
  private readonly breakpoints = inject(BreakpointObserver);
  readonly settings = this.settingsState.settings;
  readonly isHandset = toSignal(this.breakpoints.observe('(max-width: 960px)').pipe(map(r => r.matches)), { initialValue: false });
  readonly navOpen = signal(false);

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
    { path: '/help', label: 'Help', icon: 'help' },
    { path: '/settings', label: 'Settings', icon: 'settings' }
  ];

  readonly accountMenu: MenuItem[] = [
    { label: 'Profile', icon: 'person', routerLink: '/profile' },
    { label: 'Settings', icon: 'settings', routerLink: '/settings' },
    { separator: true },
    { label: 'Sign out', icon: 'logout', command: () => this.logout() }
  ];

  constructor() { void this.settingsState.load().catch(() => undefined); }

  logout(): void { void this.auth.logout(); }
}
