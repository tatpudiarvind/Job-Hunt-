import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { SelectButtonModule } from 'primeng/selectbutton';
import { AccordionModule } from 'primeng/accordion';
import { PaginatorModule, PaginatorState } from 'primeng/paginator';
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
  imports: [ButtonModule, InputTextModule, TextareaModule, TooltipModule, ProgressBarModule, FormsModule, RouterLink, DatePipe, SelectButtonModule, AccordionModule, PaginatorModule, PageHeaderComponent, StatusChipComponent, LoadingSkeletonComponent],
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
          <p-accordion class="demand"><p-accordion-panel value="demand">
            <p-accordion-header>
              <span class="panel-title"><span class="material-symbols-rounded">insights</span>&nbsp;Skills sponsored employers ask for</span>
              <span class="panel-desc">{{ d.postingsMatchingResume }} of {{ d.sponsoredPostings }} sponsored posting(s) match at least 60% of your resume skills</span>
            </p-accordion-header><p-accordion-content>
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
          </p-accordion-content></p-accordion-panel></p-accordion>
        </section>
      }

      <section class="card">
        <div class="row between toolbar">
          <div class="search-box">
            <span class="material-symbols-rounded">search</span>
            <input pInputText aria-label="Search jobs" placeholder="Search title or company" [ngModel]="query()" (ngModelChange)="query.set($event); pageIndex.set(0)" />
          </div>
          <div class="row">
            <span class="muted small">{{ visible().length }} result{{ visible().length === 1 ? '' : 's' }}</span>
            <p-selectbutton [options]="filterOptions()" [ngModel]="filter()" (ngModelChange)="filter.set($event); pageIndex.set(0)" optionLabel="label" optionValue="value" [allowEmpty]="false" aria-label="Filter jobs" />
          </div>
        </div>

		<div class="list">
		  @for (j of paged(); track j.id) {
			<article class="list-card job" [class.dismissed]="j.status === 'DISMISSED'">
			  <div class="ring" [class.mid]="(j.match?.score ?? 0) >= 40 && (j.match?.score ?? 0) < 70" [class.low]="j.match && j.match.score < 40" [class.none]="!j.match" [style.--value]="j.match?.score ?? 0" [pTooltip]="j.match ? 'Resume match' : 'Not analyzed yet'">
				<span>{{ j.match ? j.match.score + '%' : '—' }}</span>
			  </div>
			  <div class="body">
				<div class="head">
				  <a class="title ellipsis" [routerLink]="['/jobs', j.id]" [title]="j.title">{{ j.title }}</a>
				  <app-status-chip [status]="j.status" />
				</div>
				<div class="meta">
				  <span><span class="material-symbols-rounded">apartment</span>{{ j.company }}</span>
				  @if (j.location) {
					<span [pTooltip]="locations(j.location).length > 1 ? j.location : ''"><span class="material-symbols-rounded">location_on</span>{{ locations(j.location)[0] }}@if (locations(j.location).length > 1) { <span class="chip more">+{{ locations(j.location).length - 1 }}</span> }</span>
				  }
				  <span><span class="material-symbols-rounded">public</span>@if (j.url) { <a [href]="j.url" target="_blank" rel="noopener noreferrer">{{ j.source }}</a> } @else { {{ j.source }} }</span>
				  <span><span class="material-symbols-rounded">calendar_today</span>{{ j.createdAt | date:'mediumDate' }}</span>
				</div>
				@if (j.match; as m) {
				  <div class="chips">
					@for (s of m.matchedSkills.slice(0, 4); track s) { <span class="chip success"><span class="material-symbols-rounded">check</span>{{ s }}</span> }
					@for (s of m.missingSkills.slice(0, 2); track s) { <span class="chip danger" pTooltip="Missing from your resume">{{ s }}</span> }
					@if (m.matchedSkills.length + m.missingSkills.length > 6) { <span class="chip more">+{{ m.matchedSkills.length + m.missingSkills.length - 6 }} more</span> }
				  </div>
				}
			  </div>
			  <div class="job-actions">
				@if (j.status !== 'DISMISSED') {
				  <button pButton size="small" [disabled]="busy()" (click)="apply(j)"><span class="material-symbols-rounded">send</span>Apply</button>
				  <button pButton size="small" outlined [disabled]="busy()" (click)="analyze(j)"><span class="material-symbols-rounded">auto_awesome</span>{{ j.analysis ? 'Re-analyze' : 'Analyze' }}</button>
				  <button pButton text rounded severity="secondary" data-icon pTooltip="Dismiss" aria-label="Dismiss" [disabled]="busy()" (click)="dismiss(j)"><span class="material-symbols-rounded">visibility_off</span></button>
				}
				<a pButton text rounded severity="secondary" data-icon pTooltip="Open details" aria-label="Open details" [routerLink]="['/jobs', j.id]"><span class="material-symbols-rounded">chevron_right</span></a>
			  </div>
			</article>
		  } @empty {
			@if (!loaded()) {
			  <app-loading-skeleton variant="table" [rows]="6" [columns]="6" label="Loading jobs" />
			} @else {
			  <div class="empty"><span class="material-symbols-rounded">work_off</span>{{ jobs().length ? 'No jobs match this filter.' : 'No jobs yet — discover sponsored roles or add one manually.' }}</div>
			}
		  }
		</div>
		<p-paginator [totalRecords]="visible().length" [first]="pageIndex() * pageSize()" [rows]="pageSize()" [rowsPerPageOptions]="[10, 25, 50, 100]" (onPageChange)="onPage($event)" [showFirstLastIcon]="true" />
	  </section>
    </div>
  `,
  styles: [`
    .actions-header { gap: .5rem; flex-wrap: wrap; }
    .import { align-items: center; margin-bottom: 1rem; }
    .form { display: grid; gap: .5rem 1rem; grid-template-columns: 1fr 1fr; }
    .form .span { grid-column: 1 / -1; }
    .toolbar { margin-bottom: 1rem; gap: 1rem; }
    .job { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; gap: 1rem 1.25rem; align-items: center; }
    .job.dismissed { opacity: .65; }
    .job .body { display: grid; gap: .45rem; min-width: 0; }
    .job .head { display: flex; align-items: center; gap: .75rem; min-width: 0; }
    .job .title { font-weight: 600; font-size: 1rem; color: var(--ajh-text); text-decoration: none; }
    .job .title:hover { color: var(--ajh-primary); text-decoration: underline; }
    .job .chip .material-symbols-rounded { font-size: .85rem; }
    .job-actions { display: flex; align-items: center; gap: .4rem; flex-wrap: nowrap; }
    p-paginator { display: block; margin-top: 1rem; }
    @media (max-width: 900px) {
      .job { grid-template-columns: auto minmax(0, 1fr); }
      .job-actions { grid-column: 1 / -1; justify-content: flex-end; flex-wrap: wrap; }
    }
    @media (max-width: 720px) { .form { grid-template-columns: 1fr; } .job .head { flex-wrap: wrap; } }
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
  readonly filterOptions = computed(() => [
    { label: `All (${this.jobs().length})`, value: 'ALL' },
    { label: 'Qualified', value: 'QUALIFIED' },
    { label: 'New', value: 'DISCOVERED' },
    { label: 'Dismissed', value: 'DISMISSED' }
  ]);
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

  onPage(event: PaginatorState): void { this.pageIndex.set(event.page ?? 0); this.pageSize.set(event.rows ?? this.pageSize()); }

  private readonly locationCache = new Map<string, string[]>();
  locations(value: string): string[] {
    let parts = this.locationCache.get(value);
    if (!parts) {
      parts = value.split(/\s*[;|]\s*/).map(p => p.trim()).filter(Boolean);
      if (!parts.length) parts = [value];
      this.locationCache.set(value, parts);
    }
    return parts;
  }

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
