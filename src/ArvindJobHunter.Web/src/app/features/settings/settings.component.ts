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
  template: `
    <div class="page">
      <app-page-header eyebrow="Configuration" title="Settings" icon="settings" subtitle="Control how far the assistant is allowed to go. Changes take effect immediately." />

      @if (busy()) { <p-progressbar mode="indeterminate" styleClass="busy-bar" /> }
      @if (error()) { <div class="alert error"><span class="material-symbols-rounded">error</span><span>{{ error() }}</span></div> }

      @if (settings(); as s) {
        <section class="card stack">
          <div class="card-title"><span class="row gap-sm"><span class="material-symbols-rounded">open_in_browser</span>Browser auto-apply sites</span></div>
          <span class="muted small">Sites the visible browser may submit on (after your approval), with a daily cap. Log in, MFA and CAPTCHA are always done by you.</span>
          @for (site of sites(); track site.adapter) {
            <div class="row">
              <label class="row grow"><input type="checkbox" [(ngModel)]="site.enabled" [name]="'site-' + site.adapter" /> <b>{{ site.adapter }}</b> <span class="muted small">{{ site.submittedToday }} submitted today</span></label>
              <div class="field"><label class="field-label" for="f-site-dailyCap">Daily cap</label><input id="f-site-dailyCap" pInputText type="number" min="0" max="100" [(ngModel)]="site.dailyCap" [name]="'cap-' + site.adapter" /></div>
            </div>
          }
          <div class="row"><button pButton type="button" [disabled]="busy()" (click)="saveSites()">Save sites</button></div>
        </section>
        <section class="card stack">
          <div class="card-title"><span class="row gap-sm"><span class="material-symbols-rounded">description</span>Resume library (ResumeNow)</span></div>
          <span class="muted small">Opens ResumeNow in the browser window. Edit and download your PDF there yourself, then import it here so auto-apply can pick it by keyword.</span>
          <div class="row wrap">
            <button pButton outlined type="button" [disabled]="busy()" (click)="openResumeNow()"><span class="material-symbols-rounded">open_in_browser</span>Open ResumeNow</button>
            <button pButton text type="button" [disabled]="busy()" (click)="loadDownloads()">Refresh downloads</button>
          </div>
          @for (d of downloads(); track d.path) {
            <div class="row wrap">
              <span class="grow small">{{ d.name }}</span>
              <input class="kw" placeholder="Keywords, comma separated" [(ngModel)]="downloadKeywords[d.path]" [name]="'kw-' + d.name" />
              <button pButton outlined type="button" [disabled]="busy()" (click)="importDownload(d.path)">Import</button>
            </div>
          }
          @for (r of library(); track r.path) { <span class="small"><b>{{ r.label }}</b> · {{ r.keywords.join(', ') || 'no keywords' }}</span> }
        </section>
        <div class="grid-2">
          <section class="card">
            <div class="card-title between">
              <span class="row gap-sm"><span class="material-symbols-rounded">tune</span>Execution mode & AI</span>
              <button pButton text rounded severity="secondary" data-icon type="button" pTooltip="How to configure an LLM provider" (click)="llmHelp.set(!llmHelp())" aria-label="LLM setup help">
                <span class="material-symbols-rounded">help</span>
              </button>
            </div>
            <form class="stack" (ngSubmit)="save()">
              <div class="modes" role="radiogroup" aria-label="Execution mode">
                <label class="mode" [class.selected]="form.mode === 'DEMO'">
                  <p-radiobutton name="mode" value="DEMO" [(ngModel)]="form.mode" />
                  <div><div class="row"><b>Demo</b><app-status-chip status="DEMO" /></div><span class="muted small">Nothing external, no files written. Safe for exploring.</span></div>
                </label>
                <label class="mode" [class.selected]="form.mode === 'DRY_RUN'">
                  <p-radiobutton name="mode" value="DRY_RUN" [(ngModel)]="form.mode" />
                  <div><div class="row"><b>Dry run</b><app-status-chip status="DRY_RUN" /></div><span class="muted small">Full workflow with simulated external actions and receipts.</span></div>
                </label>
                <label class="mode" [class.selected]="form.mode === 'LIVE'">
                  <p-radiobutton name="mode" value="LIVE" [(ngModel)]="form.mode" />
                  <div><div class="row"><b>Live</b><app-status-chip status="LIVE" /></div><span class="muted small">Real resume files and Gmail — only after you approve and execute each action.</span></div>
                </label>
              </div>

              @if (llmHelp()) {
                <div class="alert info">
                  <span class="material-symbols-rounded">lightbulb</span>
                  <div>
                    <b>LLM setup help</b>
                    <div class="muted small mt-xs">Use <b>Demo</b> for an offline deterministic experience. Use <b>OpenAI-compatible</b> for OpenAI, local gateways, or self-hosted endpoints that expose a <span class="mono">/v1/chat/completions</span> API.</div>
                    <div class="muted small mt-xs">Typical values: OpenAI base URL <span class="mono">https://api.openai.com/v1/</span>, Ollama/OpenWebUI-style proxies often expose a similar <span class="mono">/v1/</span> endpoint, and the model should match the provider model id you want to use.</div>
                    <div class="muted small mt-xs">These settings are saved locally for this application.</div>
                  </div>
                </div>
              }

              <div class="field">
                <label class="field-label" for="llm-provider">LLM provider</label>
                <p-select inputId="llm-provider" name="llmProvider" [(ngModel)]="form.llmProvider" (onChange)="onProviderChange($event.value)" [options]="providerOptions" optionLabel="label" optionValue="value" appendTo="body" />
              </div>

              <div class="field">
                <label class="field-label" for="f-llmDisplayName">Provider name</label>
                <input id="f-llmDisplayName" pInputText name="llmDisplayName" [(ngModel)]="form.llmDisplayName" placeholder="OpenAI, Azure OpenAI, Ollama gateway..." required />
                <small class="field-hint">Friendly name shown in the header and activity views.</small>
              </div>

              @if (form.llmProvider === 'OpenAI' || form.llmProvider === 'Gemini') {
                <div class="field">
                  <label class="field-label" for="f-llmBaseUrl">Base URL</label>
                  <input id="f-llmBaseUrl" pInputText name="llmBaseUrl" [(ngModel)]="form.llmBaseUrl" placeholder="https://api.openai.com/v1/" required />
                  <small class="field-hint">{{ form.llmProvider === 'Gemini' ? 'Gemini OpenAI-compatible endpoint. Create a key at aistudio.google.com/apikey.' : 'Must point to an OpenAI-compatible API root.' }}</small>
                </div>

                <div class="field">
                  <label class="field-label" for="f-llmApiKey">API key</label>
                  <input id="f-llmApiKey" pInputText name="llmApiKey" [type]="showLlmApiKey() ? 'text' : 'password'" [(ngModel)]="form.llmApiKey" autocomplete="off" />
                  <button pButton text rounded severity="secondary" data-icon class="field-suffix" type="button" (click)="showLlmApiKey.set(!showLlmApiKey())" [attr.aria-label]="showLlmApiKey() ? 'Hide API key' : 'Show API key'">
                    <span class="material-symbols-rounded">{{ showLlmApiKey() ? 'visibility_off' : 'visibility' }}</span>
                  </button>
                  <small class="field-hint">{{ s.llmConfigured ? 'Leave blank to keep the saved key, or enter a new one to replace it.' : 'Required for OpenAI-compatible providers.' }}</small>
                </div>

                <div class="field">
                  <label class="field-label" for="f-llmModel">Model</label>
                  <input id="f-llmModel" pInputText name="llmModel" [(ngModel)]="form.llmModel" placeholder="gpt-4o-mini" required />
                  <small class="field-hint">{{ form.llmProvider === 'Gemini' ? 'Example: gemini-3.5-flash-lite, gemini-3.5-flash.' : 'Example: gpt-4o-mini, gpt-4.1-mini, llama3.1:8b, mistral-small.' }}</small>
                </div>
              } @else {
                <div class="alert info compact">
                  <span class="material-symbols-rounded">memory</span>
                  <span>Demo provider is built in and does not need a base URL, API key, or model configuration.</span>
                </div>
              }

              <div class="row between">
                <span class="muted small mono" pTooltip="Data directory">{{ s.dataDirectory }}</span>
                <button pButton type="submit" [disabled]="busy()">Save settings</button>
              </div>
            </form>
          </section>

          <section class="card">
            <div class="card-title"><span class="material-symbols-rounded">travel_explore</span>Job search preferences</div>
            <p class="muted small">Choose the European countries and profile signals used during job discovery. Resume facts drive the matching; your LinkedIn URL is stored for auto-fill and future enrichment.</p>
            <form class="stack" (ngSubmit)="saveJobSearchPreferences()">
              <div class="field">
                <label class="field-label" for="preferred-countries">Preferred countries</label>
                <p-multiselect inputId="preferred-countries" name="preferredCountries" [(ngModel)]="jobSearchForm.preferredCountries" [options]="countryOptions" display="chip" placeholder="Choose countries" appendTo="body" />
                <small class="field-hint">Select one or more target countries for job discovery.</small>
              </div>

              <div class="field">
                <label class="field-label" for="f-linkedInProfileUrl">LinkedIn profile URL</label>
                <input id="f-linkedInProfileUrl" pInputText name="linkedInProfileUrl" [(ngModel)]="jobSearchForm.linkedInProfileUrl" placeholder="https://www.linkedin.com/in/..." />
                <small class="field-hint">Manual URL takes priority; if your resume contains LinkedIn, this field is prefilled automatically.</small>
              </div>

              @if (preferences(); as p) {
                <div class="alert info compact">
                  <span class="material-symbols-rounded">person_search</span>
                  <span>
                    Effective LinkedIn URL: <span class="mono">{{ p.effectiveLinkedInProfileUrl || 'not available yet' }}</span>
                    @if (p.resumeLinkedInProfileUrl) {
                      <span class="muted small"> · Resume-detected: <span class="mono">{{ p.resumeLinkedInProfileUrl }}</span></span>
                    }
                  </span>
                </div>
              }

              <div class="field">
                <label class="field-label" for="f-preferredRolesText">Preferred roles</label>
                <textarea id="f-preferredRolesText" pTextarea name="preferredRolesText" [(ngModel)]="jobSearchForm.preferredRolesText" rows="3" placeholder="Backend Engineer, Senior .NET Developer, Platform Engineer"></textarea>
                <small class="field-hint">Separate multiple roles with commas or new lines.</small>
              </div>

              <div class="field">
                <label class="field-label" for="work-mode">Work mode</label>
                <p-select inputId="work-mode" name="workMode" [(ngModel)]="jobSearchForm.workMode" [options]="workModeSelectOptions" optionLabel="label" optionValue="value" appendTo="body" />
              </div>

              <div class="field">
                <label class="field-label" for="f-salaryExpectation">Salary expectation</label>
                <input id="f-salaryExpectation" pInputText name="salaryExpectation" [(ngModel)]="jobSearchForm.salaryExpectation" placeholder="e.g. €85,000 or £75,000" />
              </div>

              <div class="field">
                <label class="field-label" for="f-workAuthorization">Work authorization</label>
                <textarea id="f-workAuthorization" pTextarea name="workAuthorization" [(ngModel)]="jobSearchForm.workAuthorization" rows="2" placeholder="e.g. Requires visa sponsorship / Eligible to work across the EU"></textarea>
              </div>

              <div class="row between">
                <span class="muted small">These values are saved locally in the app data directory.</span>
                <button pButton type="submit" [disabled]="busy()">Save job preferences</button>
              </div>
            </form>
          </section>

          <section class="card">
            <div class="card-title"><span class="material-symbols-rounded">description</span>Master resume</div>
            <p class="muted small">Your base resume (.docx or .pdf). It is only ever opened read-only; tailored copies are generated per job as .docx and written only after you approve and execute.</p>

            <div class="resume-status mt" [class.ok]="s.masterResumeExists" [class.missing]="!s.masterResumeExists">
              <span class="material-symbols-rounded">{{ s.masterResumeExists ? 'check_circle' : 'warning' }}</span>
              <div>
                <b>{{ s.masterResumeExists ? 'Resume on file' : 'No resume found' }}</b>
                <span class="muted small mono">{{ s.masterResumePath || 'No path configured' }}</span>
              </div>
            </div>
            @if (s.masterResumeWarning) {
              <div class="alert warn mt"><span class="material-symbols-rounded">warning</span><span>{{ s.masterResumeWarning }}</span></div>
            }

            <div class="dropzone mt" [class.drag]="dragging()" (dragover)="onDragOver($event)" (dragleave)="dragging.set(false)" (drop)="onDrop($event)" (click)="resumeInput.click()">
              <input #resumeInput type="file" accept=".docx,.pdf,application/pdf,application/vnd.openxmlformats-officedocument.wordprocessingml.document" hidden (change)="onResumeFile($event)" />
              <span class="material-symbols-rounded">upload_file</span>
              @if (resumeFile) {
                <b>{{ resumeFile.name }}</b><span class="muted small">{{ (resumeFile.size / 1024) | number:'1.0-0' }} KB · click to choose a different file</span>
              } @else {
                <b>Drop your .docx or .pdf here or click to browse</b><span class="muted small">Word or PDF documents, up to 20 MB</span>
              }
            </div>
            <div class="row end mt">
              <div class="field">
                <label class="field-label" for="resume-country">Use for country</label>
                <p-select inputId="resume-country" [(ngModel)]="resumeCountry" [options]="resumeCountrySelectOptions" optionLabel="name" optionValue="code" appendTo="body" />
              </div>
              @if (resumeFile) { <button pButton text type="button" [disabled]="busy()" (click)="resumeFile = null">Clear</button> }
              <button pButton type="button" [disabled]="busy() || !resumeFile" (click)="uploadResume()"><span class="material-symbols-rounded">cloud_upload</span>Upload resume</button>
            </div>
            @if (countryResumes(s).length) {
              <h4 class="mt">Country-specific resumes</h4>
              @for (entry of countryResumes(s); track entry[0]) {
                <div class="row"><b>{{ entry[0] }}</b><span class="muted small">{{ entry[1] }}</span>
                  <button pButton text type="button" [disabled]="busy()" (click)="removeCountryResume(entry[0])">Remove</button></div>
              }
            }

            <p-accordion class="mt advanced"><p-accordion-panel value="advanced">
              <p-accordion-header><span class="panel-title">Use an existing file path instead</span></p-accordion-header><p-accordion-content>
              <form class="stack" (ngSubmit)="save()">
                <div class="field">
                  <label class="field-label" for="f-resume">Path to .docx or .pdf</label>
                  <input id="f-resume" pInputText name="resume" [(ngModel)]="form.masterResumePath" placeholder="D:\\Documents\\Arvind-Resume.docx" />
                </div>
                <div class="row end"><button pButton outlined type="submit" [disabled]="busy()">Save path</button></div>
              </form>
            </p-accordion-content></p-accordion-panel></p-accordion>
          </section>

          <div class="stack">
            <section class="card">
              <div class="card-title between">
                <span class="row"><span class="material-symbols-rounded">mail</span>Google / Gmail</span>
                <button pButton text rounded severity="secondary" data-icon type="button" pTooltip="How Gmail integration works" (click)="googleHelp.set(!googleHelp())" aria-label="Gmail integration help">
                  <span class="material-symbols-rounded">help</span>
                </button>
              </div>
              @if (googleHelp()) {
                <div class="alert info help-card">
                  <span class="material-symbols-rounded">help</span>
                  <span>
                    First save the Google OAuth Client ID, Client Secret, and Redirect URI. The application stores the client secret in protected local storage and keeps only non-secret Google settings in the local data directory. After that, click <b>Connect Google</b> to authorize Gmail access through Google.
                  </span>
                </div>
              }
              <form class="stack mt" (ngSubmit)="saveGoogleOAuth()">
                <div class="field">
                  <label class="field-label" for="f-googleClientId">Google Client ID</label>
                  <input id="f-googleClientId" pInputText name="googleClientId" [(ngModel)]="googleForm.clientId" placeholder="YOUR_GOOGLE_OAUTH_CLIENT_ID" required />
                </div>
                <div class="field">
                  <label class="field-label" for="f-googleClientSecret">Google Client Secret</label>
                  <input id="f-googleClientSecret" pInputText name="googleClientSecret" [type]="showGoogleSecret() ? 'text' : 'password'" [(ngModel)]="googleForm.clientSecret" placeholder="YOUR_GOOGLE_OAUTH_CLIENT_SECRET" />
                  <button pButton text rounded severity="secondary" data-icon class="field-suffix" type="button" (click)="showGoogleSecret.set(!showGoogleSecret())" [attr.aria-label]="showGoogleSecret() ? 'Hide client secret' : 'Show client secret'">
                    <span class="material-symbols-rounded">{{ showGoogleSecret() ? 'visibility_off' : 'visibility' }}</span>
                  </button>
                  <small class="field-hint">{{ googleForm.clientSecretConfigured ? 'Leave blank to keep the saved client secret, or enter a new one to replace it.' : 'Required the first time you configure Google OAuth.' }}</small>
                </div>
                <div class="field">
                  <label class="field-label" for="f-googleRedirectUri">Redirect URI</label>
                  <input id="f-googleRedirectUri" pInputText name="googleRedirectUri" [(ngModel)]="googleForm.redirectUri" placeholder="http://localhost:5228/api/integrations/google/callback" required />
                </div>
                <div class="row between">
                  <span class="muted small">Client secrets are stored in protected local storage and are never sent back to the browser after saving.</span>
                  <button pButton type="submit" [disabled]="busy()">Save Google settings</button>
                </div>
              </form>
              @if (google(); as g) {
                @if (!g.configured) {
                  <div class="alert warn mt"><span class="material-symbols-rounded">info</span><span>Gmail actions are unavailable until valid Google OAuth settings are saved.</span></div>
                }
                @if (g.connected) {
                  <div class="row between mt">
                    <div class="row"><span class="material-symbols-rounded ok">check_circle</span><span>Connected as <b>{{ g.accountEmail || 'Google Account' }}</b></span></div>
                    <button pButton outlined [disabled]="busy()" (click)="revoke()">Disconnect Google</button>
                  </div>
                } @else {
                  <p class="muted small mt">After saving the OAuth settings, click below to connect your Google account and enable Gmail features.</p>
                  <div class="row end mt"><button pButton [disabled]="busy() || !googleConfigured()" (click)="connect()"><span class="material-symbols-rounded">link</span>Connect Google</button></div>
                }
              }
            </section>

            <section class="card">
              <div class="card-title"><span class="material-symbols-rounded">backup</span>Backup &amp; restore</div>
              <p class="muted small">Exports jobs, applications, approvals, resumes, emails, receipts, audit log, profile and settings as a .zip. Secrets (login hash, Google tokens, encryption keys) are never exported.</p>
              <div class="row mt">
                <button pButton outlined [disabled]="busy()" (click)="exportData()"><span class="material-symbols-rounded">download</span>Download export</button>
              </div>
              <div class="restore mt">
                <input #file type="file" accept=".zip,application/zip" hidden (change)="onFile($event)" />
                <button pButton text (click)="file.click()"><span class="material-symbols-rounded">upload_file</span>{{ importFile ? importFile.name : 'Choose export file…' }}</button>
                <button pButton severity="danger" [disabled]="busy() || !importFile" (click)="importData()">Import &amp; replace</button>
              </div>
            </section>
          </div>
        </div>
      } @else if (!error()) { <app-loading-skeleton variant="form" [rows]="5" label="Loading settings" /> }
    </div>
  `,
  styles: [`
    .modes { display: grid; gap: .5rem; }
    .mode { display: flex; gap: .5rem; align-items: flex-start; padding: .75rem .9rem .75rem .5rem; border: 1px solid var(--ajh-border); border-radius: 10px; cursor: pointer; transition: border-color .15s, background .15s; }
    .mode:hover { background: #f7f9fc; }
    .mode.selected { border-color: var(--ajh-primary); background: var(--ajh-primary-soft); }
    .mode > div { display: grid; gap: .2rem; padding-top: .55rem; }
    .card-title.between { justify-content: space-between; }
    .gap-sm { gap: .35rem; }
    .mt-xs { margin-top: .35rem; }
    .ok { color: var(--ajh-success); }
    .restore { display: flex; gap: .5rem; flex-wrap: wrap; align-items: center; }
    .help-card { margin-top: .5rem; }
    .alert.compact { padding: .7rem .85rem; }
    .resume-status { display: flex; gap: .75rem; align-items: center; padding: .75rem 1rem; border-radius: 10px; }
    .resume-status.ok { background: var(--ajh-success-soft); color: var(--ajh-success); }
    .resume-status.missing { background: var(--ajh-warn-soft); color: var(--ajh-warn); }
    .resume-status > div { display: grid; gap: .15rem; min-width: 0; }
    .resume-status b { color: var(--ajh-text); }
    .resume-status .mono { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .dropzone { display: grid; justify-items: center; gap: .3rem; padding: 1.75rem 1rem; border: 2px dashed var(--ajh-border); border-radius: 12px; cursor: pointer; text-align: center; transition: border-color .15s, background .15s; }
    .dropzone:hover, .dropzone.drag { border-color: var(--ajh-primary); background: var(--ajh-primary-soft); }
    .dropzone .material-symbols-rounded { font-size: 2rem; width: 2rem; height: 2rem; color: var(--ajh-primary); }
    .advanced { box-shadow: none; border: 1px solid var(--ajh-border); border-radius: 10px !important; }
    .kw { padding: .4rem .6rem; border: 1px solid var(--ajh-border); border-radius: 8px; min-width: 14rem; background: transparent; color: inherit; }
  `]
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
  readonly library = signal<{ path: string; label: string; keywords: string[] }[]>([]);
  downloadKeywords: Record<string, string> = {};
  openResumeNow(): Promise<void> {
    return this.run(async () => { await this.api.openInBrowser('https://www.resume-now.com/'); this.notify.info('ResumeNow opened in the browser window.'); });
  }
  loadDownloads(): Promise<void> {
    return this.run(async () => { this.downloads.set(await this.api.browserDownloads()); this.library.set(await this.api.resumeLibrary()); });
  }
  importDownload(path: string): Promise<void> {
    const keywords = (this.downloadKeywords[path] ?? '').split(',').map(k => k.trim()).filter(k => k);
    return this.run(async () => { await this.api.importDownload(path, null, keywords); this.library.set(await this.api.resumeLibrary()); this.notify.success('Resume added to the library.'); });
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

