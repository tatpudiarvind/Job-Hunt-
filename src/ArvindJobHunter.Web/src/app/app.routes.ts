import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./features/login/login.component').then(m => m.LoginComponent) },
  { path: 'signup', loadComponent: () => import('./features/signup/signup.component').then(m => m.SignupComponent) },
  { path: 'forgot-password', loadComponent: () => import('./features/forgot-password/forgot-password.component').then(m => m.ForgotPasswordComponent) },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./shell/shell.component').then(m => m.ShellComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      { path: 'dashboard', loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent) },
      { path: 'jobs', loadComponent: () => import('./features/jobs/jobs.component').then(m => m.JobsComponent) },
      { path: 'jobs/:id', loadComponent: () => import('./features/jobs/job-detail.component').then(m => m.JobDetailComponent) },
      { path: 'jobs/:id/interview-prep', loadComponent: () => import('./features/interview-prep/interview-prep.component').then(m => m.InterviewPrepComponent) },
      { path: 'interview-prep', loadComponent: () => import('./features/interview-prep/interview-prep.component').then(m => m.InterviewPrepComponent) },
      { path: 'approvals', loadComponent: () => import('./features/approvals/approvals.component').then(m => m.ApprovalsComponent) },
      { path: 'applications', loadComponent: () => import('./features/applications/applications.component').then(m => m.ApplicationsComponent) },
      { path: 'emails', loadComponent: () => import('./features/emails/emails.component').then(m => m.EmailsComponent) },
      { path: 'profile', loadComponent: () => import('./features/profile/profile.component').then(m => m.ProfileComponent) },
      { path: 'activity', loadComponent: () => import('./features/activity/activity.component').then(m => m.ActivityComponent) },
      { path: 'settings', loadComponent: () => import('./features/settings/settings.component').then(m => m.SettingsComponent) }
    ]
  },
  { path: '**', redirectTo: 'dashboard' }
];
