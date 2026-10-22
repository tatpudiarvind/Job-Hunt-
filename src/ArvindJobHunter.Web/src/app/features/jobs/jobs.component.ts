import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { ApiService } from '../../core/api.service';
import { Job, SkillDemandReport } from '../../core/models';
import { describeError } from '../../core/auth.interceptor';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { StatusChipComponent } from '../../shared/status-chip.component';
import { LoadingSkeletonComponent } from '../../shared/loading-skeleton.component';
import { NotifyService } from '../../shared/notify.service';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';
import { TooltipModule } from 'primeng/tooltip';
import { ProgressBarModule } from 'primeng/progressbar';

type Filter = 'ALL' | 'QUALIFIED' | 'DISCOVERED' | 'DISMISSED';

@Component({
  selector: 'app-jobs',
  standalone: true,
  imports: [ButtonModule, InputTextModule, TextareaModule, TooltipModule, ProgressBarModule, FormsModule, RouterLink, DatePipe, MatButtonToggleModule, MatExpansionModule, MatPaginatorModule, PageHeaderComponent, StatusChipComponent, LoadingSkeletonComponent],
  template: `
    <div class="page">
      <app-page-header eyebrow="Pipeline" title="Jobs" icon="work" subtitle="Showing only roles in the UK, Europe, Singapore and the UAE that explicitly mention visa sponsorship (change countries in Settings). Discover roles from supported public career sites, then review or start the approval-gated apply workflow.">
        <div class="row actions-header">
          <button pButton outlined [disabled]="busy()" (click)="discover()"><span class="material-symbols-rounded">travel_explore</span>Discover sponsored jobs</button>
          <button pButton (click)="formOpen.set(!formOpen())"><span class="material-symbols-rounded">{{ formOpen() ? 'close' : 'add' }}</span>{{ formOpen() ? 'Close' : 'Add a job' }}</button>
        </div>
      </app-page-header>

      @if (busy()) { <p-progressbar mode="indeterminate" styleClass="busy-bar" /> }
      @if (error()) { <div class="alert error"><span class="material-symbols-rounded">error</span><span>{{ error() }}</span></div> }
      @if (discoverySummary()) { <div class="alert info"><span class="material-symbols-rounded">info</span><span>{{ discoverySummary() }}</span></div> }
      @if (discoveryNotes().length) {
        <div class="alert warn"><span class="material-symbols-rounded">policy</span><div>@for (note of discoveryNotes(); track note) { <div>{{ note }}</div> }</div></div>
      }

      @if (formOpen()) {
        <section class="card">
          <div class="card-title"><span class="material-symbols-rounded">post_add</span>New job</div>
          <div class="import row">
            <div class="field">
              <label class="field-label" for="f-importUrl">Import from URL</label>
              <span class="material-symbols-rounded field-prefix">link</span>
              <input id="f-importUrl" pInputText name="importUrl" [(ngModel)]="importUrl" placeholder="https://company.com/careers/role" />
            </div>
            <button pButton outlined type="button" [disabled]="busy() || !importUrl" (click)="importFromUrl()"><span class="material-symbols-rounded">download</span>Fetch &amp; prefill</button>
            <button pButton outlined type="button" [disabled]="busy() || !importUrl" (click)="openInBrowser()"><span class="material-symbols-rounded">open_in_browser</span>Open in browser</button>
            <button pButton outlined type="button" [disabled]="busy()" (click)="captureFromBrowser()"><span class="material-symbols-rounded">screen_share</span>Use page open in browser</button>
          </div>
          @if (warnings().length) {
            <div class="alert warn"><span class="material-symbols-rounded">info</span><div>@for (w of warnings(); track w) { <div>{{ w }}</div> }</div></div>
          }
          <form class="form" (ngSubmit)="create()">
            <div class="field"><label class="field-label" for="f-title">Title</label><input id="f-title" pInputText name="title" [(ngModel)]="form.title" required /></div>
            <div class="field"><label class="field-label" for="f-company">Company</label><input id="f-company" pInputText name="company" [(ngModel)]="form.company" required /></div>
            <div class="field"><label class="field-label" for="f-location">Location</label><input id="f-location" pInputText name="location" [(ngModel)]="form.location" /></div>
            <div class="field"><label class="field-label" for="f-source">Source</label><input id="f-source" pInputText name="source" [(ngModel)]="form.source" placeholder="Manual, LinkedIn, referral…" /></div>
            <div class="field"><label class="field-label" for="f-url">Posting URL</label><input id="f-url" pInputText name="url" [(ngModel)]="form.url" /></div>
            <div class="field"><label class="field-label" for="f-description">Job description</label><textarea id="f-description" pTextarea rows="10" name="description" [(ngModel)]="form.description" required></textarea></div>
            <div class="row end span">
              <button pButton text type="button" (click)="reset()">Clear</button>
              <button pButton type="submit" [disabled]="busy() || !form.title || !form.company || !form.description">Save job</button>
            </div>
          </form>
        </section>
      }

      @if (demand(); as d) {
        <section class="card">
          <mat-expansion-panel class="demand">
            <mat-expansion-panel-header>
              <mat-panel-title><span class="material-symbols-rounded">insights</span>&nbsp;Skills sponsored employers ask for</mat-panel-title>
              <mat-panel-description>{{ d.postingsMatchingResume }} of {{ d.sponsoredPostings }} sponsored posting(s) match at least 60% of your resume skills</mat-panel-description>
            </mat-expansion-panel-header>
            @if (!d.sponsoredPostings) {
              <p class="muted">No sponsored postings in your target countries yet. Run <b>Discover sponsored jobs</b> to collect some.</p>
            } @else {
              <p class="muted small">Counted from the {{ d.sponsoredPostings }} real posting(s) stored here, not market-wide data. Red skills are asked for but missing from your verified resume skills.</p>
              <div class="chips">
                @for (s of d.skills.slice(0, 25); track s.skill) {
                  <span class="chip" [class.have]="s.onResume" [class.gap]="!s.onResume" [pTooltip]="s.postings + ' posting(s)'">{{ s.skill }} · {{ s.percentOfPostings }}%</span>
                }
              </div>
              @for (c of d.countries; track c.country) {
                <div class="country"><b>{{ c.country }}</b> <span class="muted small">({{ c.postings }})</span>
                  @for (s of c.topSkills; track s.skill) { <span class="chip small" [class.have]="s.onResume" [class.gap]="!s.onResume">{{ s.skill }}</span> }
                </div>
              }
            }
          </mat-expansion-panel>
        </section>
      }

      <section class="card">
        <div class="row between toolbar">
          <div class="field">
            <span class="material-symbols-rounded field-prefix">search</span>
            <input pInputText placeholder="Search title or company" [ngModel]="query()" (ngModelChange)="query.set($event); pageIndex.set(0)" />
          </div>
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
						<button pButton [disabled]="busy()" (click)="apply(j)">Apply</button>
						<button pButton outlined [disabled]="busy()" (click)="analyze(j)">{{ j.analysis ? 'Re-analyze' : 'Analyze' }}</button>
						<button pButton text rounded severity="secondary" data-icon pTooltip="Dismiss" [disabled]="busy()" (click)="dismiss(j)"><span class="material-symbols-rounded">visibility_off</span></button>
					  }
					  <a pButton text rounded severity="secondary" data-icon pTooltip="Open" [routerLink]="['/jobs', j.id]"><span class="material-symbols-rounded">arrow_forward</span></a>
					</div>
				  </td>
				</tr>
			  } @empty {
				@if (!loaded()) {
				  <tr><td colspan="8"><app-loading-skeleton variant="table" [rows]="6" [columns]="6" label="Loading jobs" /></td></tr>
				} @else {
				<tr><td colspan="8"><div class="empty"><span class="material-symbols-rounded">work_off</span>{{ jobs().length ? 'No jobs match this filter.' : 'No jobs yet — discover sponsored roles or add one manually.' }}</div></td></tr>
				}
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
  readonly loaded = signal(false);
  readonly warnings = signal<string[]>([]);
  readonly discoveryNotes = signal<string[]>([]);
  readonly discoverySummary = signal('');
  readonly demand = signal<SkillDemandReport | null>(null);
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

  async load(): Promise<void> {
    try { this.jobs.set(await this.api.jobs()); } catch (e) { this.error.set(describeError(e)); } finally { this.loaded.set(true); }
    try { this.demand.set(await this.api.skillDemand()); } catch { this.demand.set(null); }
  }

  onPage(event: PageEvent): void { this.pageIndex.set(event.pageIndex); this.pageSize.set(event.pageSize); }

  reset(): void { this.form = { title: '', company: '', location: '', source: 'Manual', url: '', description: '' }; this.warnings.set([]); this.importUrl = ''; }

  discover(): Promise<void> {
    return this.run(async () => {
      const result = await this.api.discoverJobs(20);
      this.discoveryNotes.set(result.notes);
      this.discoverySummary.set(`Discovered ${result.jobs.length} visa-sponsorship role(s) in your target countries with at least 60% skill match from ${result.sources.join(', ')}. Imported ${result.imported}, updated ${result.updated}, skipped ${result.skipped}.`);
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

  openInBrowser(): Promise<void> {
    return this.run(async () => { await this.api.openInBrowser(this.importUrl); this.notify.info('Opened in the browser window. Sign in if needed, then use "Use page open in browser".'); }, false);
  }

  captureFromBrowser(): Promise<void> {
    return this.run(async () => {
      const draft = await this.api.captureJobFromBrowser();
      this.form = { title: draft.title ?? '', company: draft.company ?? '', location: draft.location ?? '', source: draft.source, url: draft.url, description: draft.description };
      this.warnings.set(draft.warnings);
      this.notify.info('Prefilled from the browser page. Review and save.');
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
