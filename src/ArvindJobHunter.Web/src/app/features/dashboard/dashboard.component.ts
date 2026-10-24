import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { ApprovalRequest, Dashboard, Job, JobApplication, Settings } from '../../core/models';
import { describeError } from '../../core/auth.interceptor';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { StatusChipComponent } from '../../shared/status-chip.component';
import { LoadingSkeletonComponent } from '../../shared/loading-skeleton.component';
import { ButtonModule } from 'primeng/button';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [ButtonModule, RouterLink, DatePipe, PageHeaderComponent, StatusChipComponent, LoadingSkeletonComponent],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent {
  private readonly api = inject(ApiService);
  readonly dashboard = signal<Dashboard | null>(null);
  readonly pending = signal<ApprovalRequest[]>([]);
  readonly topJobs = signal<Job[]>([]);
  readonly dueFollowUps = signal<JobApplication[]>([]);
  readonly settings = signal<Settings | null>(null);
  readonly error = signal('');
  readonly drafting = signal<string | null>(null);
  readonly drafted = signal<string | null>(null);
  private jobs: Job[] = [];

  async draftFollowUp(app: JobApplication): Promise<void> {
    this.drafting.set(app.id);
    try { await this.api.draftFollowUpEmail(app.id); this.drafted.set(app.id); }
    catch (e) { this.error.set(describeError(e)); }
    finally { this.drafting.set(null); }
  }

  jobTitle(jobId: string): string { const j = this.jobs.find(x => x.id === jobId); return j ? `${j.title} · ${j.company}` : jobId; }

  constructor() {
    Promise.all([this.api.dashboard(), this.api.approvals(), this.api.jobs(true), this.api.dueFollowUps()])
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
