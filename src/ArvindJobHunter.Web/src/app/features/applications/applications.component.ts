import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatMenuModule } from '@angular/material/menu';
import { ApiService } from '../../core/api.service';
import { ApplicationReview, Job, JobApplication, SubmissionPackage } from '../../core/models';
import { describeError } from '../../core/auth.interceptor';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { StatusChipComponent } from '../../shared/status-chip.component';
import { LoadingSkeletonComponent } from '../../shared/loading-skeleton.component';
import { NotifyService } from '../../shared/notify.service';

@Component({
  selector: 'app-applications',
  standalone: true,
  imports: [FormsModule, RouterLink, DatePipe, MatIconModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatProgressBarModule, MatExpansionModule, MatTooltipModule, MatMenuModule, PageHeaderComponent, StatusChipComponent, LoadingSkeletonComponent],
  template: `
    <div class="page">
      <app-page-header eyebrow="Tracking" title="Applications" icon="assignment_turned_in" subtitle="Move applications through your pipeline and schedule follow-up reminders. Status changes are local bookkeeping — they never trigger external actions.">
        <button mat-stroked-button (click)="load()" [disabled]="busy()"><mat-icon class="material-symbols-rounded">refresh</mat-icon>Refresh</button>
      </app-page-header>

      @if (busy()) { <mat-progress-bar mode="indeterminate" /> }
      @if (error()) { <div class="alert error"><mat-icon class="material-symbols-rounded">error</mat-icon><span>{{ error() }}</span></div> }

      @if (due().length) {
        <div class="alert warn"><mat-icon class="material-symbols-rounded">notifications_active</mat-icon><span>{{ due().length }} follow-up{{ due().length === 1 ? '' : 's' }} due now.</span></div>
      }

      <mat-accordion multi>
        @for (app of applications(); track app.id) {
          <mat-expansion-panel>
            <mat-expansion-panel-header>
              <mat-panel-title class="title">
                <span class="job">{{ jobTitle(app.jobId) }}</span>
                <app-status-chip [status]="app.status" />
              </mat-panel-title>
              <mat-panel-description class="desc">
                @if (app.followUpDueAt) {
                  <span class="row small" [class.overdue]="isDue(app)"><mat-icon inline class="material-symbols-rounded">alarm</mat-icon>{{ app.followUpDueAt | date:'MMM d, HH:mm' }}</span>
                }
                <span class="muted small">updated {{ app.updatedAt | date:'mediumDate' }}</span>
              </mat-panel-description>
            </mat-expansion-panel-header>

            <div class="body">
              <div class="grid-2">
                <div class="stack">
                  <h4>Status</h4>
                  <div class="row">
                    <mat-form-field appearance="outline" subscriptSizing="dynamic" class="grow">
                      <mat-label>Move to</mat-label>
                      <mat-select [ngModel]="nextStatus[app.id] ?? ''" (ngModelChange)="nextStatus[app.id] = $event">
                        @for (s of statuses(); track s) { @if (s !== app.status) { <mat-option [value]="s">{{ s.replace('_', ' ') }}</mat-option> } }
                      </mat-select>
                    </mat-form-field>
                    <button mat-flat-button color="primary" [disabled]="busy() || !nextStatus[app.id]" (click)="transition(app)">Update</button>
                  </div>
                  <div class="row wrap">
                    <a mat-button [routerLink]="['/jobs', app.jobId]">Open job<mat-icon iconPositionEnd class="material-symbols-rounded">arrow_forward</mat-icon></a>
                    @if (app.resumeVersionId) {
                      <button mat-stroked-button type="button" [disabled]="busy()" (click)="previewResume(app.resumeVersionId)"><mat-icon class="material-symbols-rounded">preview</mat-icon>Preview resume</button>
                    }
                  </div>
                </div>
                <div class="stack">
                  <h4>Follow-up reminder</h4>
                  <mat-form-field appearance="outline" subscriptSizing="dynamic">
                    <mat-label>Due</mat-label>
                    <input matInput type="datetime-local" [ngModel]="followUpAt[app.id] ?? ''" (ngModelChange)="followUpAt[app.id] = $event" />
                  </mat-form-field>
                  <mat-form-field appearance="outline" subscriptSizing="dynamic">
                    <mat-label>Note</mat-label>
                    <input matInput [ngModel]="followUpNote[app.id] ?? ''" (ngModelChange)="followUpNote[app.id] = $event" placeholder="e.g. Nudge recruiter about next steps" />
                  </mat-form-field>
                  <div class="row">
                    <button mat-stroked-button [disabled]="busy() || !followUpAt[app.id]" (click)="schedule(app)"><mat-icon class="material-symbols-rounded">alarm_add</mat-icon>Schedule</button>
                    <button mat-button [disabled]="busy()" (click)="quickFollowUp(app)">+7 days</button>
                    <button mat-button [disabled]="busy()" (click)="draftFollowUp(app)" matTooltip="Creates a draft on the Emails page; sending still needs approval">Draft email</button>
                    @if (app.followUpDueAt) { <button mat-button [disabled]="busy()" (click)="clearFollowUp(app)">Clear</button> }
                  </div>
                  @if (app.followUpNote) { <span class="muted small">Current note: {{ app.followUpNote }}</span> }
                </div>
              </div>
              <h4 class="mt">Review package</h4>
              <div class="stack">
                @if (reviews[app.id]; as r) {
                  @if (r.job.sponsorship; as s) {
                    <span class="small">Sponsorship: <strong>{{ s.status }}</strong>@if (s.matchedPhrase) { ("{{ s.matchedPhrase }}") } · source {{ s.source }} · checked {{ s.checkedAt | date:'mediumDate' }}</span>
                  } @else { <span class="small muted">Sponsorship: UNKNOWN (not checked)</span> }
                  @if (r.job.recruiter; as c) {
                    <span class="small">Recruiter: <strong>{{ c.status }}</strong>@if (c.email) { · {{ c.email }} } @if (c.source) { · from {{ c.source }} }</span>
                  } @else { <span class="small muted">Recruiter contact: not recorded</span> }
                  @for (issue of r.issues; track issue) { <span class="small overdue">⚠ {{ issue }}</span> }
                  @if (r.latestPackage; as p) {
                    <span class="small">Package v{{ p.version }} · {{ p.packageHash.slice(0, 12) }} · {{ r.packageIsCurrent ? 'current' : 'out of date — content changed' }}</span>
                  } @else { <span class="small muted">No package frozen yet.</span> }
                  <div class="row">
                    <button mat-flat-button color="primary" [disabled]="busy() || r.issues.length > 0 || r.packageIsCurrent" (click)="freeze(app)"><mat-icon class="material-symbols-rounded">inventory_2</mat-icon>Freeze package</button>
                  </div>
                } @else {
                  <button mat-stroked-button [disabled]="busy()" (click)="review(app)"><mat-icon class="material-symbols-rounded">fact_check</mat-icon>Review before submitting</button>
                }
              </div>
              <h4 class="mt">Browser apply</h4>
              <div class="stack">
                <span class="small muted">Opens a visible browser, fills the form and stops before Submit. Final submit only runs after you approve it on the Approvals page.</span>
                @for (s of submissionsFor(app); track s.id) {
                  <div class="card stack">
                    <div class="row wrap"><app-status-chip [status]="s.status" /><span class="small">{{ s.adapter }} · {{ s.updatedAt | date:'medium' }}</span>
                      @if (s.screenshotPath) { <button mat-button (click)="screenshot(s.id)"><mat-icon class="material-symbols-rounded">image</mat-icon>Screenshot</button> }
                    </div>
                    <span class="small">Resume: {{ s.resumeFilePath }}</span>
                    @for (f of s.fields; track f.label) { <span class="small">{{ f.label }}: <strong>{{ f.value || '—' }}</strong> <span class="muted">({{ f.source }})</span></span> }
                    @if (s.confirmationText) { <span class="small">{{ s.confirmationText }}</span> }
                    @if (s.unansweredFields.length && s.status !== 'SUBMITTED') {
                      @for (q of s.unansweredFields; track q) {
                        <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ q }}</mat-label>
                          <input matInput [ngModel]="answerDraft(s.id)[q] ?? ''" (ngModelChange)="answerDraft(s.id)[q] = $event" />
                        </mat-form-field>
                      }
                      <div class="row"><button mat-flat-button color="primary" [disabled]="busy()" (click)="answer(s.id)">Save answers</button></div>
                    }
                    @if (s.status === 'AWAITING_APPROVAL') { <a mat-button routerLink="/approvals">Review approval<mat-icon iconPositionEnd class="material-symbols-rounded">arrow_forward</mat-icon></a> }
                  </div>
                }
                <div class="row"><button mat-stroked-button [disabled]="busy()" (click)="prepare(app)"><mat-icon class="material-symbols-rounded">open_in_browser</mat-icon>Fill application in browser</button></div>
              </div>
              <h4 class="mt">History</h4>
              <div class="timeline">
                @for (h of app.history; track h.at) {
                  <div class="tl-item">
                    <span class="dot"></span>
                    <div><div class="row"><app-status-chip [status]="h.to" /><span class="muted small">{{ h.at | date:'medium' }}</span></div><span class="small">{{ h.reason }}</span></div>
                  </div>
                }
              </div>
            </div>
          </mat-expansion-panel>
        } @empty {
          @if (!loaded()) {
            <app-loading-skeleton variant="list" [rows]="4" label="Loading applications" />
          } @else {
          <div class="card empty"><mat-icon class="material-symbols-rounded">assignment</mat-icon>No applications yet. Prepare an application from a job to start tracking it.</div>
          }
        }
      </mat-accordion>
    </div>
  `,
  styles: [`
    .title { display: flex; gap: .75rem; align-items: center; min-width: 0; }
    .job { font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .desc { justify-content: flex-end; gap: 1rem; align-items: center; }
    .overdue { color: var(--ajh-danger); font-weight: 600; }
    .body { display: grid; gap: 1rem; padding-top: .5rem; }
    h4 { font-size: .8rem; text-transform: uppercase; letter-spacing: .05em; color: var(--ajh-muted); }
    .timeline { display: grid; gap: .75rem; padding-left: .25rem; }
    .tl-item { display: flex; gap: .75rem; align-items: flex-start; }
    .dot { width: 10px; height: 10px; border-radius: 50%; background: var(--ajh-primary); margin-top: .45rem; flex: none; }
    .tl-item > div { display: grid; gap: .2rem; }
  `]
})
export class ApplicationsComponent {
  private readonly api = inject(ApiService);
  private readonly notify = inject(NotifyService);
  readonly applications = signal<JobApplication[]>([]);
  readonly statuses = signal<string[]>([]);
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
