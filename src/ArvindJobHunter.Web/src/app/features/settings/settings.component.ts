import { Component, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AccordionModule } from 'primeng/accordion';
import { SelectModule } from 'primeng/select';
import { MultiSelectModule } from 'primeng/multiselect';
import { RadioButtonModule } from 'primeng/radiobutton';
import { ApiService } from '../../core/api.service';
import { GoogleOAuthSettings, GoogleStatus, JobSearchPreferences, Settings, SiteSetting } from '../../core/models';
import { SettingsStateService } from '../../core/settings-state.service';
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

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [ButtonModule, InputTextModule, TextareaModule, TooltipModule, ProgressBarModule, DecimalPipe, FormsModule, AccordionModule, SelectModule, MultiSelectModule, RadioButtonModule, PageHeaderComponent, StatusChipComponent, LoadingSkeletonComponent],
  templateUrl: './settings.component.html',
  styleUrl: './settings.component.scss'
})
export class SettingsComponent {
  private readonly api = inject(ApiService);
  private readonly settingsState = inject(SettingsStateService);
  private readonly notify = inject(NotifyService);
  readonly settings = signal<Settings | null>(null);
  readonly preferences = signal<JobSearchPreferences | null>(null);
  readonly google = signal<GoogleStatus | null>(null);
  readonly llmHelp = signal(false);
  readonly googleHelp = signal(false);
  readonly showLlmApiKey = signal(false);
  readonly showGoogleSecret = signal(false);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly countryOptions = [
    'United Kingdom', 'Germany', 'Netherlands', 'Ireland', 'France', 'Belgium', 'Luxembourg', 'Switzerland',
    'Sweden', 'Norway', 'Denmark', 'Finland', 'Iceland', 'Austria', 'Spain', 'Portugal', 'Italy', 'Malta', 'Greece', 'Cyprus', 'Poland', 'Slovakia', 'Bulgaria', 'Croatia', 'Slovenia', 'Serbia', 'Estonia', 'Latvia', 'Lithuania',
    'Czech Republic', 'Hungary', 'Romania', 'Other European Countries', 'Singapore', 'United Arab Emirates'
  ];
  readonly workModeOptions = ['Remote', 'Hybrid', 'Onsite', 'Flexible'];
  readonly workModeSelectOptions = [{ label: 'No preference', value: '' }, ...this.workModeOptions.map(m => ({ label: m, value: m }))];
  readonly providerOptions = [
    { label: 'Demo (offline, deterministic)', value: 'Demo' },
    { label: 'OpenAI-compatible API', value: 'OpenAI' },
    { label: 'Google Gemini', value: 'Gemini' }
  ];
  form = { mode: 'DEMO', llmProvider: 'Demo', llmDisplayName: 'Demo', llmBaseUrl: 'https://api.openai.com/v1/', llmApiKey: '', llmModel: 'gpt-4o-mini', masterResumePath: '' };
  jobSearchForm = { preferredCountries: [] as string[], linkedInProfileUrl: '', preferredRolesText: '', workMode: '', salaryExpectation: '', workAuthorization: '' };
  googleForm = { clientId: '', clientSecret: '', redirectUri: 'http://localhost:5228/api/integrations/google/callback', clientSecretConfigured: false, configured: false };
  importFile: File | null = null;
  resumeFile: File | null = null;
  readonly dragging = signal(false);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly sites = signal<SiteSetting[]>([]);
  readonly downloads = signal<{ path: string; name: string }[]>([]);
  readonly library = signal<{ path: string; label: string; keywords: string[]; kind?: string }[]>([]);
  downloadKeywords: Record<string, string> = {};
  downloadKind: Record<string, 'RESUME' | 'COVER_LETTER'> = {};
  openResumeNow(): Promise<void> {
    return this.run(async () => { await this.api.openInBrowser('https://www.resume-now.com/'); this.notify.info('ResumeNow opened in the browser window.'); });
  }
  loadDownloads(): Promise<void> {
    return this.run(async () => { this.downloads.set(await this.api.browserDownloads()); this.library.set(await this.api.resumeLibrary()); });
  }
  importDownload(path: string): Promise<void> {
    const keywords = (this.downloadKeywords[path] ?? '').split(',').map(k => k.trim()).filter(k => k);
    const kind = this.downloadKind[path] ?? 'RESUME';
    return this.run(async () => { await this.api.importDownload(path, null, keywords, kind); this.library.set(await this.api.resumeLibrary()); this.notify.success(kind === 'COVER_LETTER' ? 'Cover letter added to the library.' : 'Resume added to the library.'); });
  }
  saveSites(): Promise<void> {
    return this.run(async () => { this.sites.set(await this.api.updateSites(this.sites().map(x => ({ adapter: x.adapter, enabled: x.enabled, dailyCap: Number(x.dailyCap) })))); this.notify.success('Site settings saved.'); });
  }

  constructor() { void this.load(); this.handleOAuthRedirect(); }

  private handleOAuthRedirect(): void {
    const params = new URLSearchParams(window.location.search);
    if (params.get('google') === 'connected') {
      this.notify.success('Google account connected successfully!');
      // Remove the query parameter from URL
      window.history.replaceState({}, document.title, window.location.pathname);
      // Reload data to show updated status
      void this.load();
    }
  }

  async load(): Promise<void> {
    try {
      const [s, p, g, go] = await Promise.all([
        this.api.settings(),
        this.api.jobSearchPreferences(),
        this.api.googleStatus().catch(() => ({ configured: false, connected: false }) as GoogleStatus),
        this.api.googleOAuthSettings().catch(() => ({ clientId: '', redirectUri: 'http://localhost:5228/api/integrations/google/callback', clientSecretConfigured: false, configured: false }) as GoogleOAuthSettings)
      ]);
      this.settings.set(s); this.settingsState.update(s); this.preferences.set(p); this.google.set(g);
      this.sites.set(await this.api.sites().catch(() => []));
      this.googleForm = { clientId: go.clientId, clientSecret: '', redirectUri: go.redirectUri, clientSecretConfigured: go.clientSecretConfigured, configured: go.configured };
      this.form = { mode: s.mode, llmProvider: s.llmProvider, llmDisplayName: s.llmDisplayName, llmBaseUrl: s.llmBaseUrl, llmApiKey: '', llmModel: s.llmModel, masterResumePath: s.masterResumePath };
      this.jobSearchForm = {
        preferredCountries: [...p.preferredCountries],
        linkedInProfileUrl: p.linkedInProfileUrl ?? p.resumeLinkedInProfileUrl ?? '',
        preferredRolesText: p.preferredRoles.join(', '),
        workMode: p.workMode ?? '',
        salaryExpectation: p.salaryExpectation ?? '',
        workAuthorization: p.workAuthorization ?? ''
      };
    } catch (e) { this.error.set(describeError(e)); }
  }

  async save(): Promise<void> {
    if (this.form.mode === 'LIVE' && !await this.notify.confirm({ title: 'Switch to LIVE mode?', message: 'LIVE mode allows approved actions to write resume files and use Gmail. Every action still requires your approval and explicit execution.', confirmLabel: 'Enable LIVE', danger: true })) return;
    await this.run(async () => {
      await this.api.updateSettings(this.form);
      this.notify.success('Settings saved locally.');
    });
  }

  onProviderChange(provider: string): void {
    const presets: Record<string, { name: string; baseUrl: string; model: string }> = {
      Gemini: { name: 'Gemini', baseUrl: 'https://generativelanguage.googleapis.com/v1beta/openai/', model: 'gemini-3.5-flash-lite' },
      OpenAI: { name: 'OpenAI', baseUrl: 'https://api.openai.com/v1/', model: 'gpt-4o-mini' },
      Demo: { name: 'Demo', baseUrl: this.form.llmBaseUrl, model: this.form.llmModel }
    };
    const preset = presets[provider];
    if (!preset) return;
    this.form = { ...this.form, llmDisplayName: preset.name, llmBaseUrl: preset.baseUrl, llmModel: preset.model, llmApiKey: '' };
  }

  saveJobSearchPreferences() {
    const preferredRoles = this.jobSearchForm.preferredRolesText
      .split(/[\n]/)
      .map(value => value.trim())
      .filter(Boolean);
    return this.run(async () => {
      await this.api.updateJobSearchPreferences({
        preferredCountries: this.jobSearchForm.preferredCountries,
        linkedInProfileUrl: this.jobSearchForm.linkedInProfileUrl.trim() || null,
        preferredRoles,
        workMode: this.jobSearchForm.workMode || null,
        salaryExpectation: this.jobSearchForm.salaryExpectation.trim() || null,
        workAuthorization: this.jobSearchForm.workAuthorization.trim() || null
      });
      this.notify.success('Job search preferences saved.');
    });
  }
  googleConfigured() { return !!this.googleForm.clientId.trim() && !!this.googleForm.redirectUri.trim() && (this.googleForm.clientSecretConfigured || !!this.googleForm.clientSecret.trim()); }
  saveGoogleOAuth() {
    return this.run(async () => {
      await this.api.updateGoogleOAuthSettings({
        clientId: this.googleForm.clientId,
        clientSecret: this.googleForm.clientSecret.trim() || null,
        redirectUri: this.googleForm.redirectUri
      });
      this.notify.success('Google OAuth settings saved to protected local storage.');
    });
  }
  connect() { return this.run(async () => { const { url } = await this.api.googleAuthorizeUrl(); window.location.href = url; }); }
  revoke() { return this.run(async () => { await this.api.googleRevoke(); this.notify.info('Google disconnected.'); }); }

  onFile(event: Event) { this.importFile = (event.target as HTMLInputElement).files?.[0] ?? null; }

  onResumeFile(event: Event) { this.setResumeFile((event.target as HTMLInputElement).files?.[0] ?? null); (event.target as HTMLInputElement).value = ''; }
  onDragOver(event: DragEvent) { event.preventDefault(); this.dragging.set(true); }
  onDrop(event: DragEvent) { event.preventDefault(); this.dragging.set(false); this.setResumeFile(event.dataTransfer?.files?.[0] ?? null); }
  private setResumeFile(file: File | null) {
    const name = file?.name.toLowerCase() ?? '';
    if (file && !name.endsWith('.docx') && !name.endsWith('.pdf')) { this.notify.error('Please choose a .docx Word document or a .pdf file.'); return; }
    this.resumeFile = file;
  }
  resumeCountry = '';
  readonly resumeCountryOptions = [
    { code: 'US', name: 'United States' }, { code: 'CA', name: 'Canada' }, { code: 'GB', name: 'United Kingdom' }, { code: 'IE', name: 'Ireland' },
    { code: 'DE', name: 'Germany' },
    { code: 'AE', name: 'United Arab Emirates' }, { code: 'SG', name: 'Singapore' }, { code: 'IN', name: 'India' }];
  readonly resumeCountrySelectOptions = [{ code: '', name: 'All countries (master)' }, ...this.resumeCountryOptions];
  countryResumes(s: Settings) { return Object.entries(s.countryResumePaths ?? {}); }
  removeCountryResume(country: string) {
    return this.run(async () => {
      await this.api.removeCountryResume(country);
      this.settings.set(await this.api.settings());
      this.notify.success(`Removed the ${country} resume.`);
    });
  }
  uploadResume() {
    if (!this.resumeFile) return;
    return this.run(async () => {
      const result = await this.api.uploadMasterResume(this.resumeFile!, this.resumeCountry || undefined);
      this.resumeFile = null;
      this.settings.set(await this.api.settings());
      this.notify.success(`${this.resumeCountry ? this.resumeCountry + ' resume' : 'Master resume'} uploaded: ${result.fileName}.`);
    });
  }

  exportData() {
    return this.run(async () => {
      const blob = await this.api.exportData();
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a'); a.href = url; a.download = `arvind-job-hunter-${new Date().toISOString().slice(0, 10)}.zip`; a.click();
      URL.revokeObjectURL(url);
      this.notify.success('Export downloaded.');
    });
  }

  async importData(): Promise<void> {
    if (!this.importFile) return;
    if (!await this.notify.confirm({ title: 'Import and replace data?', message: 'Importing replaces current data files. A backup copy is kept in the data directory.', confirmLabel: 'Import', danger: true })) return;
    await this.run(async () => {
      const result = await this.api.importData(this.importFile!);
      this.importFile = null;
      this.notify.success(`Imported ${result.importedFiles.length} file(s). Previous files backed up to ${result.backupDirectory}.`);
    });
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true); this.error.set('');
    try { await action(); await this.load(); } catch (e) { this.error.set(describeError(e)); } finally { this.busy.set(false); }
  }
}

