import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ApiService } from '../../core/api.service';
import { ApprovalRequest, Dashboard, Job, JobApplication, Settings } from '../../core/models';
import { describeError } from '../../core/auth.interceptor';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { StatusChipComponent } from '../../shared/status-chip.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [RouterLink, DatePipe, MatIconModule, MatButtonModule, MatProgressBarModule, PageHeaderComponent, StatusChipComponent],
  template: `
    <div class="page">
      <app-page-header eyebrow="Overview" title="Where things stand" icon="space_dashboard" subtitle="A snapshot of your pipeline and what needs your attention.">
        <a mat-flat-button color="primary" routerLink="/jobs"><mat-icon class="material-symbols-rounded">add</mat-icon>Add a job</a>
      </app-page-header>

      @if (error()) { <div class="alert error"><mat-icon class="material-symbols-rounded">error</mat-icon><span>{{ error() }}</span></div> }
      @if (settings() && !settings()!.masterResumeExists) {
        No master resume on file yet. Upload your .docx or .pdf so the assistant can tailor it per job.
      }

      @if (dashboard(); as d) {
        <div class="grid-3 metrics">
          <div class="metric"><b>{{ d.jobs }}</b><span>Jobs tracked</span></div>
          <div class="metric"><b>{{ d.qualifiedJobs }}</b><span>Qualified matches</span></div>
          <div class="metric"><b>{{ d.applications }}</b><span>Applications</span></div>
          <div class="metric accent"><b>{{ d.pendingApprovals }}</b><span>Awaiting your approval</span></div>
          <div class="metric"><b>{{ d.agentRuns }}</b><span>Agent runs</span></div>
          <div class="metric"><b>{{ d.externalActions }}</b><span>External actions executed</span></div>
        </div>

        <div class="grid-2">
          <section class="card">
            <div class="card-title"><mat-icon class="material-symbols-rounded">fact_check</mat-icon>Pending approvals</div>
            @for (a of pending(); track a.id) {
              <div class="item">
                <div class="row between"><app-status-chip [status]="a.actionType" /><span class="muted small">expires {{ a.expiresAt | date:'medium' }}</span></div>
                <span>{{ a.summary }}</span>
              </div>
            } @empty { <div class="empty"><mat-icon class="material-symbols-rounded">task_alt</mat-icon>Nothing waiting on you.</div> }
            <div class="row end mt"><a mat-button routerLink="/approvals">Open approvals<mat-icon iconPositionEnd class="material-symbols-rounded">arrow_forward</mat-icon></a></div>
          </section>

          <section class="card">
            <div class="card-title"><mat-icon class="material-symbols-rounded">star</mat-icon>Top matches</div>
            @for (j of topJobs(); track j.id) {
              <div class="item">
                <div class="row between">
                  <a class="title" [routerLink]="['/jobs', j.id]">{{ j.title }}</a>
                  <span class="score" [class.low]="(j.match?.score ?? 0) < 60">{{ j.match?.score }}%</span>
                </div>
                <span class="muted small">{{ j.company }}@if (j.location) { · {{ j.location }}}</span>
              </div>
            } @empty { <div class="empty"><mat-icon class="material-symbols-rounded">query_stats</mat-icon>No analyzed jobs yet. <a routerLink="/jobs">Add one</a>.</div> }
          </section>

          <section class="card">
            <div class="card-title"><mat-icon class="material-symbols-rounded">notifications_active</mat-icon>Follow-ups due</div>
            @for (f of dueFollowUps(); track f.id) {
              <div class="item">
                <div class="row between"><a class="title" [routerLink]="['/jobs', f.jobId]">{{ jobTitle(f.jobId) }}</a><app-status-chip [status]="f.status" /></div>
                <span class="muted small">due {{ f.followUpDueAt | date:'short' }}@if (f.followUpNote) { · {{ f.followUpNote }}}</span>
              </div>
            } @empty { <div class="empty"><mat-icon class="material-symbols-rounded">notifications_off</mat-icon>No follow-ups due.</div> }
            <div class="row end mt"><a mat-button routerLink="/applications">Manage applications<mat-icon iconPositionEnd class="material-symbols-rounded">arrow_forward</mat-icon></a></div>
          </section>

          <section class="card">
            <div class="card-title"><mat-icon class="material-symbols-rounded">route</mat-icon>How it works</div>
            <ol class="steps">
              <li><b>Add a job</b> by pasting a description or importing from a URL.</li>
              <li><b>Analyze &amp; match</b> against your verified skills.</li>
              <li><b>Prepare</b> a tailored resume and cover letter — proposed, not applied.</li>
              <li><b>Approve &amp; execute</b> each external action yourself.</li>
            </ol>
          </section>
        </div>
      } @else if (!error()) { <mat-progress-bar mode="indeterminate" /> }
    </div>
  `,
  styles: [`
    .metrics { grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); }
    .steps { margin: 0; padding-left: 1.2rem; display: grid; gap: .6rem; line-height: 1.5; color: var(--ajh-muted); }
    .steps b { color: var(--ajh-text); }
  `]
})
export class DashboardComponent {
  private readonly api = inject(ApiService);
  readonly dashboard = signal<Dashboard | null>(null);
  readonly pending = signal<ApprovalRequest[]>([]);
  readonly topJobs = signal<Job[]>([]);
  readonly dueFollowUps = signal<JobApplication[]>([]);
  readonly settings = signal<Settings | null>(null);
  readonly error = signal('');
  private jobs: Job[] = [];

  jobTitle(jobId: string): string { const j = this.jobs.find(x => x.id === jobId); return j ? `${j.title} · ${j.company}` : jobId; }

  constructor() {
    Promise.all([this.api.dashboard(), this.api.approvals(), this.api.jobs(), this.api.dueFollowUps()])
      .then(([d, approvals, jobs, due]) => {
        this.dashboard.set(d);
        this.jobs = jobs;
        this.dueFollowUps.set(due);
        this.pending.set(approvals.filter(a => a.status === 'PENDING'));
        this.topJobs.set(jobs.filter(j => j.match).sort((a, b) => (b.match!.score - a.match!.score)).slice(0, 5));
      })
      .catch(e => this.error.set(describeError(e)));
    this.api.settings().then(s => this.settings.set(s)).catch(() => undefined);
  }
}
