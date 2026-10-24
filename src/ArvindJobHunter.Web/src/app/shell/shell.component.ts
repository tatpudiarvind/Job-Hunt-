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
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss'
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
