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
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
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
  imports: [FormsModule, RouterLink, DatePipe, MatIconModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatProgressBarModule, MatTooltipModule, MatButtonToggleModule, MatExpansionModule, MatPaginatorModule, PageHeaderComponent, StatusChipComponent],
  template: `
    <div class="page">
      <app-page-header eyebrow="Pipeline" title="Jobs" icon="work" subtitle="Discover UK and Europe roles with explicit visa sponsorship from supported public career sites, then review or start the approval-gated apply workflow.">
        <div class="row actions-header">
          <button mat-stroked-button [disabled]="busy()" (click)="discover()"><mat-icon class="material-symbols-rounded">travel_explore</mat-icon>Discover sponsored jobs</button>
          <button mat-flat-button color="primary" (click)="formOpen.set(!formOpen())"><mat-icon class="material-symbols-rounded">{{ formOpen() ? 'close' : 'add' }}</mat-icon>{{ formOpen() ? 'Close' : 'Add a job' }}</button>
        </div>
      </app-page-header>

      @if (busy()) { <mat-progress-bar mode="indeterminate" /> }
      @if (error()) { <div class="alert error"><mat-icon class="material-symbols-rounded">error</mat-icon><span>{{ error() }}</span></div> }
      @if (discoverySummary()) { <div class="alert info"><mat-icon class="material-symbols-rounded">info</mat-icon><span>{{ discoverySummary() }}</span></div> }
      @if (discoveryNotes().length) {
        <div class="alert warn"><mat-icon class="material-symbols-rounded">policy</mat-icon><div>@for (note of discoveryNotes(); track note) { <div>{{ note }}</div> }</div></div>
      }

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
            <input matInput placeholder="Search title or company" [ngModel]="query()" (ngModelChange)="query.set($event); pageIndex.set(0)" />
          </mat-form-field>
          <mat-button-toggle-group [value]="filter()" (change)="filter.set($event.value); pageIndex.set(0)" hideSingleSelectionIndicator>
            <mat-button-toggle value="ALL">All ({{ jobs().length }})</mat-button-toggle>
            <mat-button-toggle value="QUALIFIED">Qualified</mat-button-toggle>
            <mat-button-toggle value="DISCOVERED">New</mat-button-toggle>
            <mat-button-toggle value="DISMISSED">Dismissed</mat-button-toggle>
          </mat-button-toggle-group>
        </div>

		<div class="table-wrap">
		  <table class="jobs-table">
			<thead>
			  <tr>
				<th>Role</th>
				<th>Company</th>
				<th>Location</th>
				<th>Source</th>
				<th>Status</th>
				<th class="num">Match</th>
				<th>Added</th>
				<th class="actions">Actions</th>
			  </tr>
			</thead>
			<tbody>
			  @for (j of paged(); track j.id) {
				<tr>
				  <td>
					<a class="title" [routerLink]="['/jobs', j.id]">{{ j.title }}</a>
					@if (j.match; as m) {
					  <div class="chips mt-sm">
						@for (s of m.matchedSkills.slice(0, 4); track s) { <span class="chip success">{{ s }}</span> }
						@for (s of m.missingSkills.slice(0, 2); track s) { <span class="chip danger">{{ s }}</span> }
					  </div>
					}
				  </td>
				  <td>{{ j.company }}</td>
				  <td>{{ j.location || '—' }}</td>
				  <td>@if (j.url) { <a [href]="j.url" target="_blank" rel="noopener noreferrer">{{ j.source }}</a> } @else { {{ j.source }} }</td>
				  <td><app-status-chip [status]="j.status" /></td>
				  <td class="num">@if (j.match; as m) { <span class="score" [class.low]="m.score < 60">{{ m.score }}%</span> } @else { <span class="muted small">—</span> }</td>
				  <td class="nowrap">{{ j.createdAt | date:'mediumDate' }}</td>
				  <td class="actions">
					<div class="row nowrap">
					  @if (j.status !== 'DISMISSED') {
						<button mat-flat-button color="primary" [disabled]="busy()" (click)="apply(j)">Apply</button>
						<button mat-stroked-button [disabled]="busy()" (click)="analyze(j)">{{ j.analysis ? 'Re-analyze' : 'Analyze' }}</button>
						<button mat-icon-button matTooltip="Dismiss" [disabled]="busy()" (click)="dismiss(j)"><mat-icon class="material-symbols-rounded">visibility_off</mat-icon></button>
					  }
					  <a mat-icon-button matTooltip="Open" [routerLink]="['/jobs', j.id]"><mat-icon class="material-symbols-rounded">arrow_forward</mat-icon></a>
					</div>
				  </td>
				</tr>
			  } @empty {
				<tr><td colspan="8"><div class="empty"><mat-icon class="material-symbols-rounded">work_off</mat-icon>{{ jobs().length ? 'No jobs match this filter.' : 'No jobs yet — discover sponsored roles or add one manually.' }}</div></td></tr>
			  }
			</tbody>
		  </table>
		</div>
		<mat-paginator [length]="visible().length" [pageIndex]="pageIndex()" [pageSize]="pageSize()" [pageSizeOptions]="[10, 25, 50, 100]" (page)="onPage($event)" showFirstLastButtons aria-label="Select jobs page" />
      </section>
    </div>
  `,
  styles: [`
    .actions-header { gap: .5rem; flex-wrap: wrap; }
    .import { align-items: center; margin-bottom: 1rem; }
    .form { display: grid; gap: .5rem 1rem; grid-template-columns: 1fr 1fr; }
    .form .span { grid-column: 1 / -1; }
    .toolbar { margin-bottom: .5rem; }
    .search { width: min(100%, 360px); }
    .table-wrap { overflow-x: auto; }
    .jobs-table { width: 100%; border-collapse: collapse; }
    .jobs-table th, .jobs-table td { padding: .6rem .5rem; text-align: left; vertical-align: top; border-bottom: 1px solid var(--mat-sys-outline-variant, rgba(0,0,0,.12)); }
    .jobs-table th { font-weight: 600; font-size: .85rem; white-space: nowrap; }
    .jobs-table tbody tr:hover { background: var(--mat-sys-surface-container-low, rgba(0,0,0,.03)); }
    .jobs-table .num, .jobs-table .actions { text-align: right; }
    .jobs-table .actions .row { justify-content: flex-end; }
    .nowrap { white-space: nowrap; }
    .title { font-weight: 600; }
    .mt-sm { margin-top: .35rem; }
    @media (max-width: 720px) { .form { grid-template-columns: 1fr; } }
  `]
})
export class JobsComponent {
  private readonly api = inject(ApiService);
  private readonly notify = inject(NotifyService);
  readonly jobs = signal<Job[]>([]);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly warnings = signal<string[]>([]);
  readonly discoveryNotes = signal<string[]>([]);
  readonly discoverySummary = signal('');
  readonly formOpen = signal(false);
  readonly query = signal('');
  readonly filter = signal<Filter>('ALL');
  readonly pageIndex = signal(0);
  readonly pageSize = signal(25);
  readonly visible = computed(() => {
    const q = this.query().trim().toLowerCase();
    const f = this.filter();
    return this.jobs().filter(j =>
      (f === 'ALL' ? j.status !== 'DISMISSED' : f === 'QUALIFIED' ? j.status === 'QUALIFIED' : j.status === f) &&
      (!q || j.title.toLowerCase().includes(q) || j.company.toLowerCase().includes(q)));
  });
  readonly paged = computed(() => {
    const start = this.pageIndex() * this.pageSize();
    return this.visible().slice(start, start + this.pageSize());
  });
  importUrl = '';
  form = { title: '', company: '', location: '', source: 'Manual', url: '', description: '' };

  constructor() { void this.load(); }

  async load(): Promise<void> { try { this.jobs.set(await this.api.jobs()); } catch (e) { this.error.set(describeError(e)); } }

  onPage(event: PageEvent): void { this.pageIndex.set(event.pageIndex); this.pageSize.set(event.pageSize); }

  reset(): void { this.form = { title: '', company: '', location: '', source: 'Manual', url: '', description: '' }; this.warnings.set([]); this.importUrl = ''; }

  discover(): Promise<void> {
    return this.run(async () => {
      const result = await this.api.discoverJobs(20);
      this.discoveryNotes.set(result.notes);
      this.discoverySummary.set(`Discovered ${result.jobs.length} European visa-sponsorship role(s) with at least 60% skill match from ${result.sources.join(', ')}. Imported ${result.imported}, updated ${result.updated}, skipped ${result.skipped}.`);
      this.notify.success(result.imported > 0 ? `Imported ${result.imported} sponsored role(s).` : 'Discovery finished.');
    });
  }

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
  apply(j: Job): Promise<void> {
    return this.run(async () => {
      if (!j.analysis) await this.api.analyzeJob(j.id);
      const result = await this.api.prepareJob(j.id);
      this.notify.info(result.resumeApproval ? 'Application package prepared. Approve the resume changes in Approvals before execution.' : 'Application package prepared.');
    });
  }
  async dismiss(j: Job): Promise<void> {
    if (!await this.notify.confirm({ title: 'Dismiss job?', message: `${j.title} at ${j.company} will be hidden from the active list.`, confirmLabel: 'Dismiss' })) return;
    await this.run(() => this.api.dismissJob(j.id).then(() => undefined));
  }

  private async run(action: () => Promise<void>, reload = true): Promise<void> {
    this.busy.set(true); this.error.set('');
    try { await action(); if (reload) await this.load(); } catch (e) { this.error.set(describeError(e)); } finally { this.busy.set(false); }
  }
}
