import { Component, Input, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { SelectModule } from 'primeng/select';
import { ApiService } from '../../core/api.service';
import { InterviewPrepPlan, InterviewResource, Job } from '../../core/models';
import { describeError } from '../../core/auth.interceptor';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { ButtonModule } from 'primeng/button';
import { TooltipModule } from 'primeng/tooltip';
import { ProgressBarModule } from 'primeng/progressbar';

@Component({
  selector: 'app-interview-prep',
  standalone: true,
  imports: [ButtonModule, TooltipModule, ProgressBarModule, FormsModule, RouterLink, DatePipe, SelectModule, PageHeaderComponent],
  templateUrl: './interview-prep.component.html',
  styleUrl: './interview-prep.component.scss'
})
export class InterviewPrepComponent {
  private readonly api = inject(ApiService);
  readonly jobs = signal<Job[]>([]);
  readonly plan = signal<InterviewPrepPlan | null>(null);
  readonly selectedId = signal<string | null>(null);
  readonly error = signal('');
  readonly busy = signal(false);
  private routeId: string | null = null;

  @Input() set id(value: string | undefined) { this.routeId = value ?? null; if (value) void this.select(value); }

  constructor() {
    this.api.jobs(true).then(jobs => {
      const usable = jobs.filter(j => j.status !== 'DISMISSED');
      this.jobs.set(usable);
      if (!this.routeId && usable.length === 1) void this.select(usable[0].id);
    }).catch(e => this.error.set(describeError(e)));
  }

  icon(r: InterviewResource): string { return r.kind === 'video' ? 'play_circle' : r.kind === 'search' ? 'search' : 'language'; }

  async select(jobId: string): Promise<void> {
    this.selectedId.set(jobId); this.busy.set(true); this.error.set('');
    try { this.plan.set(await this.api.interviewPrep(jobId)); } catch (e) { this.plan.set(null); this.error.set(describeError(e)); } finally { this.busy.set(false); }
  }
}
