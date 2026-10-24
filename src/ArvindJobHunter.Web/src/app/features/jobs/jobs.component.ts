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
  templateUrl: './jobs.component.html',
  styleUrl: './jobs.component.scss'
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

  readonly searches = signal<{ name: string; url: string }[]>([]);
  readonly links = signal<{ text: string; url: string }[]>([]);
  newSearch = { name: '', url: '' };
  private searchesLoaded = false;

  loadSearches(): void {
    if (this.searchesLoaded) return;
    this.searchesLoaded = true;
    this.api.savedSearches().then(s => this.searches.set(s)).catch(e => this.error.set(describeError(e)));
  }

  addSearch(): Promise<void> {
    return this.run(async () => {
      this.searches.set(await this.api.saveSearches([...this.searches(), { name: this.newSearch.name.trim(), url: this.newSearch.url.trim() }]));
      this.newSearch = { name: '', url: '' };
      this.notify.success('Search saved.');
    }, false);
  }

  removeSearch(index: number): Promise<void> {
    return this.run(async () => { this.searches.set(await this.api.saveSearches(this.searches().filter((_, i) => i !== index))); }, false);
  }

  runSearch(url: string): Promise<void> {
    return this.run(async () => { await this.api.openInBrowser(url); this.links.set([]); this.notify.info('Search opened in the browser window. When results show, click "List jobs on page".'); }, false);
  }

  listLinks(): Promise<void> {
    return this.run(async () => {
      const page = await this.api.browserLinks();
      this.links.set(page.links);
      this.notify.info(page.links.length ? `Found ${page.links.length} job link(s) on the page.` : 'No job links found on this page.');
    }, false);
  }

  pickLink(url: string): Promise<void> { this.importUrl = url; return this.importFromUrl(); }

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
