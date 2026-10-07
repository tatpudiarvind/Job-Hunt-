import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatExpansionModule } from '@angular/material/expansion';
import { ApiService } from '../../core/api.service';
import { Job } from '../../core/models';
import { describeError } from '../../core/auth.interceptor';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { StatusChipComponent } from '../../shared/status-chip.component';
import { NotifyService } from '../../shared/notify.service';

type Filter = 'ALL' | 'QUALIFIED' | 'DISCOVERED' | 'DISMISSED';

@Component({
  selector: 'app-jobs',
  standalone: true,
  imports: [FormsModule, RouterLink, DatePipe, MatIconModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatProgressBarModule, MatTooltipModule, MatButtonToggleModule, MatExpansionModule, PageHeaderComponent, StatusChipComponent],
  template: `
    <div class="page">
      <app-page-header eyebrow="Pipeline" title="Jobs" icon="work" subtitle="Paste a job description or import from a URL. Analysis is read-only; nothing is sent anywhere.">
        <button mat-flat-button color="primary" (click)="formOpen.set(!formOpen())"><mat-icon class="material-symbols-rounded">{{ formOpen() ? 'close' : 'add' }}</mat-icon>{{ formOpen() ? 'Close' : 'Add a job' }}</button>
      </app-page-header>

      @if (busy()) { <mat-progress-bar mode="indeterminate" /> }
      @if (error()) { <div class="alert error"><mat-icon class="material-symbols-rounded">error</mat-icon><span>{{ error() }}</span></div> }

      @if (formOpen()) {
        <section class="card">
          <div class="card-title"><mat-icon class="material-symbols-rounded">post_add</mat-icon>New job</div>
          <div class="import row">
            <mat-form-field appearance="outline" class="grow" subscriptSizing="dynamic">
              <mat-label>Import from URL</mat-label>
              <mat-icon matPrefix class="material-symbols-rounded">link</mat-icon>
              <input matInput name="importUrl" [(ngModel)]="importUrl" placeholder="https://company.com/careers/role" />
            </mat-form-field>
            <button mat-stroked-button type="button" [disabled]="busy() || !importUrl" (click)="importFromUrl()"><mat-icon class="material-symbols-rounded">download</mat-icon>Fetch &amp; prefill</button>
          </div>
          @if (warnings().length) {
            <div class="alert warn"><mat-icon class="material-symbols-rounded">info</mat-icon><div>@for (w of warnings(); track w) { <div>{{ w }}</div> }</div></div>
          }
          <form class="form" (ngSubmit)="create()">
            <mat-form-field appearance="outline"><mat-label>Title</mat-label><input matInput name="title" [(ngModel)]="form.title" required /></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Company</mat-label><input matInput name="company" [(ngModel)]="form.company" required /></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Location</mat-label><input matInput name="location" [(ngModel)]="form.location" /></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Source</mat-label><input matInput name="source" [(ngModel)]="form.source" placeholder="Manual, LinkedIn, referral…" /></mat-form-field>
            <mat-form-field appearance="outline" class="span"><mat-label>Posting URL</mat-label><input matInput name="url" [(ngModel)]="form.url" /></mat-form-field>
            <mat-form-field appearance="outline" class="span"><mat-label>Job description</mat-label><textarea matInput rows="10" name="description" [(ngModel)]="form.description" required></textarea></mat-form-field>
            <div class="row end span">
              <button mat-button type="button" (click)="reset()">Clear</button>
              <button mat-flat-button color="primary" type="submit" [disabled]="busy() || !form.title || !form.company || !form.description">Save job</button>
            </div>
          </form>
        </section>
      }

      <section class="card">
        <div class="row between toolbar">
          <mat-form-field appearance="outline" class="search" subscriptSizing="dynamic">
            <mat-icon matPrefix class="material-symbols-rounded">search</mat-icon>
            <input matInput placeholder="Search title or company" [ngModel]="query()" (ngModelChange)="query.set($event)" />
          </mat-form-field>
          <mat-button-toggle-group [value]="filter()" (change)="filter.set($event.value)" hideSingleSelectionIndicator>
            <mat-button-toggle value="ALL">All ({{ jobs().length }})</mat-button-toggle>
            <mat-button-toggle value="QUALIFIED">Qualified</mat-button-toggle>
            <mat-button-toggle value="DISCOVERED">New</mat-button-toggle>
            <mat-button-toggle value="DISMISSED">Dismissed</mat-button-toggle>
          </mat-button-toggle-group>
        </div>

        @for (j of visible(); track j.id) {
          <div class="item job">
            <div class="main">
              <div class="row">
                <a class="title" [routerLink]="['/jobs', j.id]">{{ j.title }}</a>
                <app-status-chip [status]="j.status" />
              </div>
              <span class="muted small">{{ j.company }}@if (j.location) { · {{ j.location }}} · {{ j.source }} · added {{ j.createdAt | date:'mediumDate' }}</span>
              @if (j.match) {
                <div class="chips mt-sm">
                  @for (s of j.match.matchedSkills.slice(0, 6); track s) { <span class="chip success">{{ s }}</span> }
                  @for (s of j.match.missingSkills.slice(0, 3); track s) { <span class="chip danger">{{ s }}</span> }
                </div>
              }
            </div>
            <div class="side">
              @if (j.match) { <span class="score big" [class.low]="j.match.score < 60">{{ j.match.score }}%</span> } @else { <span class="muted small">not analyzed</span> }
              <div class="row">
                @if (j.status !== 'DISMISSED') {
                  <button mat-stroked-button [disabled]="busy()" (click)="analyze(j)">{{ j.analysis ? 'Re-analyze' : 'Analyze' }}</button>
                  <button mat-icon-button matTooltip="Dismiss" [disabled]="busy()" (click)="dismiss(j)"><mat-icon class="material-symbols-rounded">visibility_off</mat-icon></button>
                }
                <a mat-icon-button matTooltip="Open" [routerLink]="['/jobs', j.id]"><mat-icon class="material-symbols-rounded">arrow_forward</mat-icon></a>
              </div>
            </div>
          </div>
        } @empty {
          <div class="empty"><mat-icon class="material-symbols-rounded">work_off</mat-icon>{{ jobs().length ? 'No jobs match this filter.' : 'No jobs yet — add your first one above.' }}</div>
        }
      </section>
    </div>
  `,
  styles: [`
    .import { align-items: center; margin-bottom: 1rem; }
    .form { display: grid; gap: .5rem 1rem; grid-template-columns: 1fr 1fr; }
    .form .span { grid-column: 1 / -1; }
    .toolbar { margin-bottom: .5rem; }
    .search { width: min(100%, 360px); }
    .job { display: flex; justify-content: space-between; gap: 1rem; align-items: flex-start; }
    .job .main { display: grid; gap: .3rem; min-width: 0; }
    .job .side { display: grid; gap: .5rem; justify-items: end; flex: none; }
    .score.big { font-size: 1.5rem; }
    .mt-sm { margin-top: .35rem; }
    @media (max-width: 720px) { .form { grid-template-columns: 1fr; } .job { flex-direction: column; } .job .side { justify-items: start; } }
  `]
})
export class JobsComponent {
  private readonly api = inject(ApiService);
  private readonly notify = inject(NotifyService);
  readonly jobs = signal<Job[]>([]);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly warnings = signal<string[]>([]);
  readonly formOpen = signal(false);
  readonly query = signal('');
  readonly filter = signal<Filter>('ALL');
  readonly visible = computed(() => {
    const q = this.query().trim().toLowerCase();
    const f = this.filter();
    return this.jobs().filter(j =>
      (f === 'ALL' ? j.status !== 'DISMISSED' : f === 'QUALIFIED' ? j.status === 'QUALIFIED' : j.status === f) &&
      (!q || j.title.toLowerCase().includes(q) || j.company.toLowerCase().includes(q)));
  });
  importUrl = '';
  form = { title: '', company: '', location: '', source: 'Manual', url: '', description: '' };

  constructor() { void this.load(); }

  async load(): Promise<void> { try { this.jobs.set(await this.api.jobs()); } catch (e) { this.error.set(describeError(e)); } }

  reset(): void { this.form = { title: '', company: '', location: '', source: 'Manual', url: '', description: '' }; this.warnings.set([]); this.importUrl = ''; }

  importFromUrl(): Promise<void> {
    return this.run(async () => {
      const draft = await this.api.previewJobImport(this.importUrl);
      this.form = { title: draft.title ?? '', company: draft.company ?? '', location: draft.location ?? '', source: draft.source, url: draft.url, description: draft.description };
      this.warnings.set(draft.warnings);
      this.notify.info('Prefilled from the page. Review and save.');
    }, false);
  }

  create(): Promise<void> {
    return this.run(async () => {
      await this.api.createJob({ title: this.form.title, company: this.form.company, location: this.form.location || null, source: this.form.source || null, url: this.form.url || null, description: this.form.description });
      this.reset(); this.formOpen.set(false);
      this.notify.success('Job saved.');
    });
  }

  analyze(j: Job): Promise<void> { return this.run(async () => { await this.api.analyzeJob(j.id); this.notify.success(`Analyzed ${j.title}.`); }); }
  async dismiss(j: Job): Promise<void> {
    if (!await this.notify.confirm({ title: 'Dismiss job?', message: `${j.title} at ${j.company} will be hidden from the active list.`, confirmLabel: 'Dismiss' })) return;
    await this.run(() => this.api.dismissJob(j.id).then(() => undefined));
  }

  private async run(action: () => Promise<void>, reload = true): Promise<void> {
    this.busy.set(true); this.error.set('');
    try { await action(); if (reload) await this.load(); } catch (e) { this.error.set(describeError(e)); } finally { this.busy.set(false); }
  }
}
