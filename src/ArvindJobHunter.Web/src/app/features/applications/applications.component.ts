import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { SelectModule } from 'primeng/select';
import { AccordionModule } from 'primeng/accordion';
import { ApiService } from '../../core/api.service';
import { ApplicationReview, Job, JobApplication, SubmissionPackage } from '../../core/models';
import { describeError } from '../../core/auth.interceptor';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { StatusChipComponent } from '../../shared/status-chip.component';
import { LoadingSkeletonComponent } from '../../shared/loading-skeleton.component';
import { NotifyService } from '../../shared/notify.service';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TooltipModule } from 'primeng/tooltip';
import { ProgressBarModule } from 'primeng/progressbar';

@Component({
  selector: 'app-applications',
  standalone: true,
  imports: [ButtonModule, InputTextModule, TooltipModule, ProgressBarModule, FormsModule, RouterLink, DatePipe, SelectModule, AccordionModule, PageHeaderComponent, StatusChipComponent, LoadingSkeletonComponent],
  templateUrl: './applications.component.html',
  styleUrl: './applications.component.scss'
})
export class ApplicationsComponent {
  private readonly api = inject(ApiService);
  private readonly notify = inject(NotifyService);
  readonly applications = signal<JobApplication[]>([]);
  readonly statuses = signal<string[]>([]);
  private readonly statusOptionsByCurrent = computed(() => {
    const all = this.statuses().map(s => ({ label: s.replace('_', ' '), value: s }));
    return Object.fromEntries(this.statuses().map(c => [c, all.filter(o => o.value !== c)])) as Record<string, { label: string; value: string }[]>;
  });

  statusOptions(current: string): { label: string; value: string }[] {
    return this.statusOptionsByCurrent()[current] ?? [];
  }
  readonly error = signal('');
  readonly busy = signal(false);
  readonly loaded = signal(false);
  readonly due = computed(() => this.applications().filter(a => this.isDue(a)));
  nextStatus: Record<string, string> = {};
  followUpAt: Record<string, string> = {};
  followUpNote: Record<string, string> = {};
  reviews: Record<string, ApplicationReview> = {};
  private jobs: Job[] = [];
  readonly submissions = signal<SubmissionPackage[]>([]);
  private answers: Record<string, Record<string, string>> = {};

  submissionsFor(app: JobApplication): SubmissionPackage[] { return this.submissions().filter(s => s.applicationId === app.id); }
  answerDraft(id: string): Record<string, string> { return this.answers[id] ??= {}; }
  prepare(app: JobApplication): Promise<void> {
    return this.run(async () => { const s = await this.api.prepareSubmission(app.jobId); this.notify.info(s.status === 'AWAITING_APPROVAL' ? 'Form filled — approve it to submit.' : 'Form filled — some questions need answers.'); });
  }
  answer(id: string): Promise<void> {
    return this.run(async () => { await this.api.answerSubmission(id, this.answerDraft(id)); delete this.answers[id]; this.notify.success('Answers saved.'); });
  }
  async screenshot(id: string): Promise<void> {
    try { window.open(URL.createObjectURL(await this.api.submissionScreenshot(id)), '_blank'); } catch (e) { this.error.set(describeError(e)); }
  }

  constructor() { void this.load(); }

  jobTitle(jobId: string): string { const j = this.jobs.find(x => x.id === jobId); return j ? `${j.title} · ${j.company}` : jobId; }
  isDue(app: JobApplication): boolean { return !!app.followUpDueAt && new Date(app.followUpDueAt).getTime() <= Date.now(); }

  async load(): Promise<void> {
    try {
      const [apps, statuses, jobs] = await Promise.all([this.api.applications(), this.api.applicationStatuses(), this.api.jobs(true)]);
      this.applications.set(apps); this.statuses.set(statuses); this.jobs = jobs;
      this.submissions.set(await this.api.submissions().catch(() => []));
      for (const a of apps) {
        if (a.followUpDueAt && !this.followUpAt[a.id]) this.followUpAt[a.id] = this.toLocalInput(a.followUpDueAt);
        if (a.followUpNote && !this.followUpNote[a.id]) this.followUpNote[a.id] = a.followUpNote;
      }
    } catch (e) { this.error.set(describeError(e)); } finally { this.loaded.set(true); }
  }

  transition(app: JobApplication): Promise<void> {
    const status = this.nextStatus[app.id];
    return this.run(async () => { await this.api.transitionApplication(app.id, status, null); delete this.nextStatus[app.id]; this.notify.success(`Moved to ${status.replace('_', ' ')}.`); });
  }
  schedule(app: JobApplication): Promise<void> {
    return this.run(async () => { await this.api.scheduleFollowUp(app.id, new Date(this.followUpAt[app.id]).toISOString(), this.followUpNote[app.id] || null); this.notify.success('Follow-up scheduled.'); });
  }
  quickFollowUp(app: JobApplication): Promise<void> {
    const due = new Date(Date.now() + 7 * 24 * 60 * 60 * 1000);
    this.followUpAt[app.id] = this.toLocalInput(due.toISOString());
    return this.run(async () => { await this.api.scheduleFollowUp(app.id, due.toISOString(), this.followUpNote[app.id] || 'Follow up on application'); this.notify.success('Follow-up set for one week from now.'); });
  }
  draftFollowUp(app: JobApplication): Promise<void> {
    return this.run(async () => { await this.api.draftFollowUpEmail(app.id); this.notify.success('Follow-up draft created on the Emails page.'); }, false);
  }
  clearFollowUp(app: JobApplication): Promise<void> {
    return this.run(async () => { await this.api.clearFollowUp(app.id); delete this.followUpAt[app.id]; delete this.followUpNote[app.id]; this.notify.info('Reminder cleared.'); });
  }
  review(app: JobApplication): Promise<void> {
    return this.run(async () => { this.reviews[app.id] = await this.api.applicationReview(app.id); }, false);
  }
  freeze(app: JobApplication): Promise<void> {
    return this.run(async () => {
      const p = await this.api.createApplicationPackage(app.id);
      this.reviews[app.id] = await this.api.applicationReview(app.id);
      this.notify.success(`Package v${p.version} frozen.`);
    }, false);
  }
  previewResume(resumeVersionId: string): Promise<void> {
    return this.run(async () => { this.notify.previewResume(await this.api.resumePreview(resumeVersionId)); }, false);
  }

  private toLocalInput(iso: string): string {
    const d = new Date(iso); const pad = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
  }

  private async run(action: () => Promise<void>, reload = true): Promise<void> {
    this.busy.set(true); this.error.set('');
    try { await action(); if (reload) await this.load(); } catch (e) { this.error.set(describeError(e)); } finally { this.busy.set(false); }
  }
}
