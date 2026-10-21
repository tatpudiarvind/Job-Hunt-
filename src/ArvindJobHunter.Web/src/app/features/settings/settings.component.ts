import { Component, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatRadioModule } from '@angular/material/radio';
import { ApiService } from '../../core/api.service';
import { GoogleOAuthSettings, GoogleStatus, JobSearchPreferences, Settings, SiteSetting } from '../../core/models';
import { SettingsStateService } from '../../core/settings-state.service';
import { describeError } from '../../core/auth.interceptor';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { StatusChipComponent } from '../../shared/status-chip.component';
import { LoadingSkeletonComponent } from '../../shared/loading-skeleton.component';
import { NotifyService } from '../../shared/notify.service';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [DecimalPipe, FormsModule, MatExpansionModule, MatIconModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatProgressBarModule, MatTooltipModule, MatRadioModule, PageHeaderComponent, StatusChipComponent, LoadingSkeletonComponent],
  template: `
    <div class="page">
      <app-page-header eyebrow="Configuration" title="Settings" icon="settings" subtitle="Control how far the assistant is allowed to go. Changes take effect immediately." />

      @if (busy()) { <mat-progress-bar mode="indeterminate" /> }
      @if (error()) { <div class="alert error"><mat-icon class="material-symbols-rounded">error</mat-icon><span>{{ error() }}</span></div> }

      @if (settings(); as s) {
        <section class="card stack">
          <div class="card-title"><span class="row gap-sm"><mat-icon class="material-symbols-rounded">open_in_browser</mat-icon>Browser auto-apply sites</span></div>
          <span class="muted small">Sites the visible browser may submit on (after your approval), with a daily cap. Log in, MFA and CAPTCHA are always done by you.</span>
          @for (site of sites(); track site.adapter) {
            <div class="row">
              <label class="row grow"><input type="checkbox" [(ngModel)]="site.enabled" [name]="'site-' + site.adapter" /> <b>{{ site.adapter }}</b> <span class="muted small">{{ site.submittedToday }} submitted today</span></label>
              <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>Daily cap</mat-label><input matInput type="number" min="0" max="100" [(ngModel)]="site.dailyCap" [name]="'cap-' + site.adapter" /></mat-form-field>
            </div>
          }
          <div class="row"><button mat-flat-button color="primary" type="button" [disabled]="busy()" (click)="saveSites()">Save sites</button></div>
        </section>
        <section class="card stack">
          <div class="card-title"><span class="row gap-sm"><mat-icon class="material-symbols-rounded">description</mat-icon>Resume library (ResumeNow)</span></div>
          <span class="muted small">Opens ResumeNow in the browser window. Edit and download your PDF there yourself, then import it here so auto-apply can pick it by keyword.</span>
          <div class="row wrap">
            <button mat-stroked-button type="button" [disabled]="busy()" (click)="openResumeNow()"><mat-icon class="material-symbols-rounded">open_in_browser</mat-icon>Open ResumeNow</button>
            <button mat-button type="button" [disabled]="busy()" (click)="loadDownloads()">Refresh downloads</button>
          </div>
          @for (d of downloads(); track d.path) {
            <div class="row wrap">
              <span class="grow small">{{ d.name }}</span>
              <input class="kw" placeholder="Keywords, comma separated" [(ngModel)]="downloadKeywords[d.path]" [name]="'kw-' + d.name" />
              <button mat-stroked-button type="button" [disabled]="busy()" (click)="importDownload(d.path)">Import</button>
            </div>
          }
          @for (r of library(); track r.path) { <span class="small"><b>{{ r.label }}</b> · {{ r.keywords.join(', ') || 'no keywords' }}</span> }
        </section>
        <div class="grid-2">
          <section class="card">
            <div class="card-title between">
              <span class="row gap-sm"><mat-icon class="material-symbols-rounded">tune</mat-icon>Execution mode & AI</span>
              <button mat-icon-button type="button" matTooltip="How to configure an LLM provider" (click)="llmHelp.set(!llmHelp())" aria-label="LLM setup help">
                <mat-icon class="material-symbols-rounded">help</mat-icon>
              </button>
            </div>
            <form class="stack" (ngSubmit)="save()">
              <mat-radio-group class="modes" name="mode" [(ngModel)]="form.mode">
                <label class="mode" [class.selected]="form.mode === 'DEMO'">
                  <mat-radio-button value="DEMO" />
                  <div><div class="row"><b>Demo</b><app-status-chip status="DEMO" /></div><span class="muted small">Nothing external, no files written. Safe for exploring.</span></div>
                </label>
                <label class="mode" [class.selected]="form.mode === 'DRY_RUN'">
                  <mat-radio-button value="DRY_RUN" />
                  <div><div class="row"><b>Dry run</b><app-status-chip status="DRY_RUN" /></div><span class="muted small">Full workflow with simulated external actions and receipts.</span></div>
                </label>
                <label class="mode" [class.selected]="form.mode === 'LIVE'">
                  <mat-radio-button value="LIVE" />
                  <div><div class="row"><b>Live</b><app-status-chip status="LIVE" /></div><span class="muted small">Real resume files and Gmail — only after you approve and execute each action.</span></div>
                </label>
              </mat-radio-group>

              @if (llmHelp()) {
                <div class="alert info">
                  <mat-icon class="material-symbols-rounded">lightbulb</mat-icon>
                  <div>
                    <b>LLM setup help</b>
                    <div class="muted small mt-xs">Use <b>Demo</b> for an offline deterministic experience. Use <b>OpenAI-compatible</b> for OpenAI, local gateways, or self-hosted endpoints that expose a <span class="mono">/v1/chat/completions</span> API.</div>
                    <div class="muted small mt-xs">Typical values: OpenAI base URL <span class="mono">https://api.openai.com/v1/</span>, Ollama/OpenWebUI-style proxies often expose a similar <span class="mono">/v1/</span> endpoint, and the model should match the provider model id you want to use.</div>
                    <div class="muted small mt-xs">These settings are saved locally for this application.</div>
                  </div>
                </div>
              }

              <mat-form-field appearance="outline" subscriptSizing="dynamic">
                <mat-label>LLM provider</mat-label>
                <mat-select name="llmProvider" [(ngModel)]="form.llmProvider" (selectionChange)="onProviderChange($event.value)">
                  <mat-option value="Demo">Demo (offline, deterministic)</mat-option>
                  <mat-option value="OpenAI">OpenAI-compatible API</mat-option>
                  <mat-option value="Gemini">Google Gemini</mat-option>
                </mat-select>
              </mat-form-field>

              <mat-form-field appearance="outline" subscriptSizing="dynamic">
                <mat-label>Provider name</mat-label>
                <input matInput name="llmDisplayName" [(ngModel)]="form.llmDisplayName" placeholder="OpenAI, Azure OpenAI, Ollama gateway..." required />
                <mat-hint>Friendly name shown in the header and activity views.</mat-hint>
              </mat-form-field>

              @if (form.llmProvider === 'OpenAI' || form.llmProvider === 'Gemini') {
                <mat-form-field appearance="outline" subscriptSizing="dynamic">
                  <mat-label>Base URL</mat-label>
                  <input matInput name="llmBaseUrl" [(ngModel)]="form.llmBaseUrl" placeholder="https://api.openai.com/v1/" required />
                  <mat-hint>{{ form.llmProvider === 'Gemini' ? 'Gemini OpenAI-compatible endpoint. Create a key at aistudio.google.com/apikey.' : 'Must point to an OpenAI-compatible API root.' }}</mat-hint>
                </mat-form-field>

                <mat-form-field appearance="outline" subscriptSizing="dynamic">
                  <mat-label>API key</mat-label>
                  <input matInput name="llmApiKey" [type]="showLlmApiKey() ? 'text' : 'password'" [(ngModel)]="form.llmApiKey" autocomplete="off" />
                  <button mat-icon-button matSuffix type="button" (click)="showLlmApiKey.set(!showLlmApiKey())" [attr.aria-label]="showLlmApiKey() ? 'Hide API key' : 'Show API key'">
                    <mat-icon class="material-symbols-rounded">{{ showLlmApiKey() ? 'visibility_off' : 'visibility' }}</mat-icon>
                  </button>
                  <mat-hint>{{ s.llmConfigured ? 'Leave blank to keep the saved key, or enter a new one to replace it.' : 'Required for OpenAI-compatible providers.' }}</mat-hint>
                </mat-form-field>

                <mat-form-field appearance="outline" subscriptSizing="dynamic">
                  <mat-label>Model</mat-label>
                  <input matInput name="llmModel" [(ngModel)]="form.llmModel" placeholder="gpt-4o-mini" required />
                  <mat-hint>{{ form.llmProvider === 'Gemini' ? 'Example: gemini-3.5-flash-lite, gemini-3.5-flash.' : 'Example: gpt-4o-mini, gpt-4.1-mini, llama3.1:8b, mistral-small.' }}</mat-hint>
                </mat-form-field>
              } @else {
                <div class="alert info compact">
                  <mat-icon class="material-symbols-rounded">memory</mat-icon>
                  <span>Demo provider is built in and does not need a base URL, API key, or model configuration.</span>
                </div>
              }

              <div class="row between">
                <span class="muted small mono" matTooltip="Data directory">{{ s.dataDirectory }}</span>
                <button mat-flat-button color="primary" type="submit" [disabled]="busy()">Save settings</button>
              </div>
            </form>
          </section>

          <section class="card">
            <div class="card-title"><mat-icon class="material-symbols-rounded">travel_explore</mat-icon>Job search preferences</div>
            <p class="muted small">Choose the European countries and profile signals used during job discovery. Resume facts drive the matching; your LinkedIn URL is stored for auto-fill and future enrichment.</p>
            <form class="stack" (ngSubmit)="saveJobSearchPreferences()">
              <mat-form-field appearance="outline" subscriptSizing="dynamic">
                <mat-label>Preferred countries</mat-label>
                <mat-select name="preferredCountries" [(ngModel)]="jobSearchForm.preferredCountries" multiple>
                  @for (country of countryOptions; track country) {
                    <mat-option [value]="country">{{ country }}</mat-option>
                  }
                </mat-select>
                <mat-hint>Select one or more target countries for job discovery.</mat-hint>
              </mat-form-field>

              <mat-form-field appearance="outline" subscriptSizing="dynamic">
                <mat-label>LinkedIn profile URL</mat-label>
                <input matInput name="linkedInProfileUrl" [(ngModel)]="jobSearchForm.linkedInProfileUrl" placeholder="https://www.linkedin.com/in/..." />
                <mat-hint>Manual URL takes priority; if your resume contains LinkedIn, this field is prefilled automatically.</mat-hint>
              </mat-form-field>

              @if (preferences(); as p) {
                <div class="alert info compact">
                  <mat-icon class="material-symbols-rounded">person_search</mat-icon>
                  <span>
                    Effective LinkedIn URL: <span class="mono">{{ p.effectiveLinkedInProfileUrl || 'not available yet' }}</span>
                    @if (p.resumeLinkedInProfileUrl) {
                      <span class="muted small"> · Resume-detected: <span class="mono">{{ p.resumeLinkedInProfileUrl }}</span></span>
                    }
                  </span>
                </div>
              }

              <mat-form-field appearance="outline" subscriptSizing="dynamic">
                <mat-label>Preferred roles</mat-label>
                <textarea matInput name="preferredRolesText" [(ngModel)]="jobSearchForm.preferredRolesText" rows="3" placeholder="Backend Engineer, Senior .NET Developer, Platform Engineer"></textarea>
                <mat-hint>Separate multiple roles with commas or new lines.</mat-hint>
              </mat-form-field>

              <mat-form-field appearance="outline" subscriptSizing="dynamic">
                <mat-label>Work mode</mat-label>
                <mat-select name="workMode" [(ngModel)]="jobSearchForm.workMode">
                  <mat-option value="">No preference</mat-option>
                  @for (mode of workModeOptions; track mode) {
                    <mat-option [value]="mode">{{ mode }}</mat-option>
                  }
                </mat-select>
              </mat-form-field>

              <mat-form-field appearance="outline" subscriptSizing="dynamic">
                <mat-label>Salary expectation</mat-label>
                <input matInput name="salaryExpectation" [(ngModel)]="jobSearchForm.salaryExpectation" placeholder="e.g. €85,000 or £75,000" />
              </mat-form-field>

              <mat-form-field appearance="outline" subscriptSizing="dynamic">
                <mat-label>Work authorization</mat-label>
                <textarea matInput name="workAuthorization" [(ngModel)]="jobSearchForm.workAuthorization" rows="2" placeholder="e.g. Requires visa sponsorship / Eligible to work across the EU"></textarea>
              </mat-form-field>

              <div class="row between">
                <span class="muted small">These values are saved locally in the app data directory.</span>
                <button mat-flat-button color="primary" type="submit" [disabled]="busy()">Save job preferences</button>
              </div>
            </form>
          </section>

          <section class="card">
            <div class="card-title"><mat-icon class="material-symbols-rounded">description</mat-icon>Master resume</div>
            <p class="muted small">Your base resume (.docx or .pdf). It is only ever opened read-only; tailored copies are generated per job as .docx and written only after you approve and execute.</p>

            <div class="resume-status mt" [class.ok]="s.masterResumeExists" [class.missing]="!s.masterResumeExists">
              <mat-icon class="material-symbols-rounded">{{ s.masterResumeExists ? 'check_circle' : 'warning' }}</mat-icon>
              <div>
                <b>{{ s.masterResumeExists ? 'Resume on file' : 'No resume found' }}</b>
                <span class="muted small mono">{{ s.masterResumePath || 'No path configured' }}</span>
              </div>
            </div>
            @if (s.masterResumeWarning) {
              <div class="alert warn mt"><mat-icon class="material-symbols-rounded">warning</mat-icon><span>{{ s.masterResumeWarning }}</span></div>
            }

            <div class="dropzone mt" [class.drag]="dragging()" (dragover)="onDragOver($event)" (dragleave)="dragging.set(false)" (drop)="onDrop($event)" (click)="resumeInput.click()">
              <input #resumeInput type="file" accept=".docx,.pdf,application/pdf,application/vnd.openxmlformats-officedocument.wordprocessingml.document" hidden (change)="onResumeFile($event)" />
              <mat-icon class="material-symbols-rounded">upload_file</mat-icon>
              @if (resumeFile) {
                <b>{{ resumeFile.name }}</b><span class="muted small">{{ (resumeFile.size / 1024) | number:'1.0-0' }} KB · click to choose a different file</span>
              } @else {
                <b>Drop your .docx or .pdf here or click to browse</b><span class="muted small">Word or PDF documents, up to 20 MB</span>
              }
            </div>
            <div class="row end mt">
              <mat-form-field appearance="outline" subscriptSizing="dynamic">
                <mat-label>Use for country</mat-label>
                <mat-select [(ngModel)]="resumeCountry">
                  <mat-option value="">All countries (master)</mat-option>
                  @for (c of resumeCountryOptions; track c.code) { <mat-option [value]="c.code">{{ c.name }}</mat-option> }
                </mat-select>
              </mat-form-field>
              @if (resumeFile) { <button mat-button type="button" [disabled]="busy()" (click)="resumeFile = null">Clear</button> }
              <button mat-flat-button color="primary" type="button" [disabled]="busy() || !resumeFile" (click)="uploadResume()"><mat-icon class="material-symbols-rounded">cloud_upload</mat-icon>Upload resume</button>
            </div>
            @if (countryResumes(s).length) {
              <h4 class="mt">Country-specific resumes</h4>
              @for (entry of countryResumes(s); track entry[0]) {
                <div class="row"><b>{{ entry[0] }}</b><span class="muted small">{{ entry[1] }}</span>
                  <button mat-button type="button" [disabled]="busy()" (click)="removeCountryResume(entry[0])">Remove</button></div>
              }
            }

            <mat-expansion-panel class="mt advanced">
              <mat-expansion-panel-header><mat-panel-title>Use an existing file path instead</mat-panel-title></mat-expansion-panel-header>
              <form class="stack" (ngSubmit)="save()">
                <mat-form-field appearance="outline" subscriptSizing="dynamic">
                  <mat-label>Path to .docx or .pdf</mat-label>
                  <input matInput name="resume" [(ngModel)]="form.masterResumePath" placeholder="D:\\Documents\\Arvind-Resume.docx" />
                </mat-form-field>
                <div class="row end"><button mat-stroked-button type="submit" [disabled]="busy()">Save path</button></div>
              </form>
            </mat-expansion-panel>
          </section>

          <div class="stack">
            <section class="card">
              <div class="card-title between">
                <span class="row"><mat-icon class="material-symbols-rounded">mail</mat-icon>Google / Gmail</span>
                <button mat-icon-button type="button" matTooltip="How Gmail integration works" (click)="googleHelp.set(!googleHelp())" aria-label="Gmail integration help">
                  <mat-icon class="material-symbols-rounded">help</mat-icon>
                </button>
              </div>
              @if (googleHelp()) {
                <div class="alert info help-card">
                  <mat-icon class="material-symbols-rounded">help</mat-icon>
                  <span>
                    First save the Google OAuth Client ID, Client Secret, and Redirect URI. The application stores the client secret in protected local storage and keeps only non-secret Google settings in the local data directory. After that, click <b>Connect Google</b> to authorize Gmail access through Google.
                  </span>
                </div>
              }
              <form class="stack mt" (ngSubmit)="saveGoogleOAuth()">
                <mat-form-field appearance="outline" subscriptSizing="dynamic">
                  <mat-label>Google Client ID</mat-label>
                  <input matInput name="googleClientId" [(ngModel)]="googleForm.clientId" placeholder="YOUR_GOOGLE_OAUTH_CLIENT_ID" required />
                </mat-form-field>
                <mat-form-field appearance="outline" subscriptSizing="dynamic">
                  <mat-label>Google Client Secret</mat-label>
                  <input matInput name="googleClientSecret" [type]="showGoogleSecret() ? 'text' : 'password'" [(ngModel)]="googleForm.clientSecret" placeholder="YOUR_GOOGLE_OAUTH_CLIENT_SECRET" />
                  <button mat-icon-button matSuffix type="button" (click)="showGoogleSecret.set(!showGoogleSecret())" [attr.aria-label]="showGoogleSecret() ? 'Hide client secret' : 'Show client secret'">
                    <mat-icon class="material-symbols-rounded">{{ showGoogleSecret() ? 'visibility_off' : 'visibility' }}</mat-icon>
                  </button>
                  <mat-hint>{{ googleForm.clientSecretConfigured ? 'Leave blank to keep the saved client secret, or enter a new one to replace it.' : 'Required the first time you configure Google OAuth.' }}</mat-hint>
                </mat-form-field>
                <mat-form-field appearance="outline" subscriptSizing="dynamic">
                  <mat-label>Redirect URI</mat-label>
                  <input matInput name="googleRedirectUri" [(ngModel)]="googleForm.redirectUri" placeholder="http://localhost:5228/api/integrations/google/callback" required />
                </mat-form-field>
                <div class="row between">
                  <span class="muted small">Client secrets are stored in protected local storage and are never sent back to the browser after saving.</span>
                  <button mat-flat-button color="primary" type="submit" [disabled]="busy()">Save Google settings</button>
                </div>
              </form>
              @if (google(); as g) {
                @if (!g.configured) {
                  <div class="alert warn mt"><mat-icon class="material-symbols-rounded">info</mat-icon><span>Gmail actions are unavailable until valid Google OAuth settings are saved.</span></div>
                }
                @if (g.connected) {
                  <div class="row between mt">
                    <div class="row"><mat-icon class="material-symbols-rounded ok">check_circle</mat-icon><span>Connected as <b>{{ g.accountEmail || 'Google Account' }}</b></span></div>
                    <button mat-stroked-button [disabled]="busy()" (click)="revoke()">Disconnect Google</button>
                  </div>
                } @else {
                  <p class="muted small mt">After saving the OAuth settings, click below to connect your Google account and enable Gmail features.</p>
                  <div class="row end mt"><button mat-flat-button color="primary" [disabled]="busy() || !googleConfigured()" (click)="connect()"><mat-icon class="material-symbols-rounded">link</mat-icon>Connect Google</button></div>
                }
              }
            </section>

            <section class="card">
              <div class="card-title"><mat-icon class="material-symbols-rounded">backup</mat-icon>Backup &amp; restore</div>
              <p class="muted small">Exports jobs, applications, approvals, resumes, emails, receipts, audit log, profile and settings as a .zip. Secrets (login hash, Google tokens, encryption keys) are never exported.</p>
              <div class="row mt">
                <button mat-stroked-button [disabled]="busy()" (click)="exportData()"><mat-icon class="material-symbols-rounded">download</mat-icon>Download export</button>
              </div>
              <div class="restore mt">
                <input #file type="file" accept=".zip,application/zip" hidden (change)="onFile($event)" />
                <button mat-button (click)="file.click()"><mat-icon class="material-symbols-rounded">upload_file</mat-icon>{{ importFile ? importFile.name : 'Choose export file…' }}</button>
                <button mat-flat-button color="warn" [disabled]="busy() || !importFile" (click)="importData()">Import &amp; replace</button>
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
    .dropzone mat-icon { font-size: 2rem; width: 2rem; height: 2rem; color: var(--ajh-primary); }
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
      .split(/[\n,]/)
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

