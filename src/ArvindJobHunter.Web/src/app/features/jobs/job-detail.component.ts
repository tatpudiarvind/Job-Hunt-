import { Component, Input, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TabsModule } from 'primeng/tabs';
import { AccordionModule } from 'primeng/accordion';
import { ApiService } from '../../core/api.service';
import { CountryProfile, EmailDraft, Job, JobApplication, ResumeVersion } from '../../core/models';
import { describeError } from '../../core/auth.interceptor';
import { StatusChipComponent } from '../../shared/status-chip.component';
import { LoadingSkeletonComponent } from '../../shared/loading-skeleton.component';
import { NotifyService } from '../../shared/notify.service';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TooltipModule } from 'primeng/tooltip';
import { ProgressBarModule } from 'primeng/progressbar';

@Component({
  selector: 'app-job-detail',
  standalone: true,
  imports: [ButtonModule, InputTextModule, TooltipModule, ProgressBarModule, FormsModule, RouterLink, DatePipe, TabsModule, AccordionModule, StatusChipComponent, LoadingSkeletonComponent],
  template: `
    <div class="page">
      <a pButton text routerLink="/jobs" class="back"><span class="material-symbols-rounded">arrow_back</span>Jobs</a>
      @if (busy()) { <p-progressbar mode="indeterminate" styleClass="busy-bar" /> }
      @if (error()) { <div class="alert error"><span class="material-symbols-rounded">error</span><span>{{ error() }}</span></div> }

      @if (job(); as j) {
        <section class="card hero">
          <div class="identity">
            <div class="row"><h1>{{ j.title }}</h1><app-status-chip [status]="j.status" /></div>
            <p class="muted">{{ j.company }}@if (j.location) { · {{ j.location }}} · {{ j.source }}@if (j.url) { · <a [href]="j.url" target="_blank" rel="noopener noreferrer external">posting <span class="material-symbols-rounded">open_in_new</span></a>}</p>
            @if (application(); as app) { <div class="row small"><span class="muted">Application:</span><app-status-chip [status]="app.status" /><a routerLink="/applications" class="small">manage</a></div> }
          </div>
          @if (j.match; as m) {
            <div class="score-card" [class.low]="m.score < 60">
              <b>{{ m.score }}%</b><span>match</span>
            </div>
          }
        </section>

        <section class="card">
          <div class="row actions">
            <button pButton [disabled]="busy()" (click)="analyze()"><span class="material-symbols-rounded">query_stats</span>{{ j.analysis ? 'Re-analyze' : 'Analyze & match' }}</button>
            <button pButton outlined [disabled]="busy() || !j.analysis" pTooltip="Proposes tailored resume changes and a cover letter. Nothing is written until approved." (click)="prepare()"><span class="material-symbols-rounded">description</span>Prepare application</button>
            <a pButton outlined [routerLink]="['/jobs', j.id, 'interview-prep']"><span class="material-symbols-rounded">school</span>Interview prep</a>
            <span class="grow"></span>
            <div class="field">
              <label class="field-label" for="f-recipient">Recruiter email</label>
              <input id="f-recipient" pInputText name="recipient" [(ngModel)]="recipient" placeholder="recruiter@company.com" type="email" />
            </div>
            <button pButton outlined [disabled]="busy() || !recipient" (click)="draftRecruiter()"><span class="material-symbols-rounded">edit_note</span>Draft email</button>
          </div>
          <div class="row actions">
            @if (j.recruiter; as c) {
              <span class="small">Recorded contact: <strong>{{ c.status }}</strong>@if (c.email) { · {{ c.email }} } @if (c.source) { · from {{ c.source }} }</span>
            } @else { <span class="small muted">No recruiter contact recorded. Contacts are never guessed — add one you found yourself.</span> }
            <span class="grow"></span>
            <div class="field">
              <label class="field-label" for="f-contactSource">Where you found it</label>
              <input id="f-contactSource" pInputText name="contactSource" [(ngModel)]="contactSource" placeholder="e.g. careers page, LinkedIn post" />
            </div>
            <button pButton outlined [disabled]="busy() || !recipient || !contactSource" (click)="saveContact('USER_PROVIDED')"><span class="material-symbols-rounded">contact_mail</span>Save contact</button>
            <button pButton text [disabled]="busy()" (click)="saveContact('NOT_FOUND')">Not found</button>
          </div>
        </section>

        @if (country(); as c) {
          <section class="card">
            <div class="row actions">
              <div class="card-title"><span class="material-symbols-rounded">public</span>{{ c.code === 'XX' ? 'Country not detected' : c.name + ' conventions' }}</div>
              <span class="grow"></span>
              <div class="field">
                <label class="field-label">Country</label>
                <select matNativeControl [ngModel]="j.countryCode ?? ''" (ngModelChange)="setCountry($event)" name="country">
                  <option value="">Auto-detect</option>
                  @for (o of countryOptions; track o.code) { <option [value]="o.code">{{ o.name }}</option> }
                </select>
              </div>
            </div>
            <div class="chips">
              <span class="chip primary">{{ c.documentName }}</span>
              <span class="chip">max {{ c.maxPages }} page(s)</span>
              <span class="chip">{{ c.photoExpected ? 'photo common' : 'no photo' }}</span>
              <span class="chip">{{ c.spelling }} spelling</span>
              @for (s of c.requiredSections; track s) { <span class="chip">{{ s }}</span> }
            </div>
            <ul class="list mt">@for (n of c.notes; track n) { <li class="small">{{ n }}</li> }</ul>
          </section>
        }

        <p-tabs value="analysis">
          <p-tablist>
            <p-tab value="analysis">Analysis &amp; match</p-tab>
            <p-tab value="resume">Resume &amp; emails</p-tab>
            <p-tab value="description">Description</p-tab>
            @if (application()) { <p-tab value="history">History</p-tab> }
          </p-tablist>
          <p-tabpanels>
          <p-tabpanel value="analysis">
            <div class="grid-2">
              <section class="card">
                <div class="card-title"><span class="material-symbols-rounded">analytics</span>Analysis</div>
                @if (j.analysis; as a) {
                  <p class="pre">{{ a.summary }}</p>
                  <p class="muted small mt">Seniority: {{ a.seniorityLevel }}</p>
                  <h4 class="mt">Required skills</h4>
                  <div class="chips">@for (s of a.requiredSkills; track s) { <span class="chip primary">{{ s }}</span> }</div>
                  <h4 class="mt">Nice to have</h4>
                  <div class="chips">@for (s of a.niceToHaveSkills; track s) { <span class="chip">{{ s }}</span> } @empty { <span class="muted small">none listed</span> }</div>
                  @if (a.responsibilities.length) {
                    <h4 class="mt">Responsibilities</h4>
                    <ul class="list">@for (r of a.responsibilities; track r) { <li>{{ r }}</li> }</ul>
                  }
                } @else { <div class="empty"><span class="material-symbols-rounded">query_stats</span>Not analyzed yet. Click “Analyze &amp; match”.</div> }
              </section>
              <section class="card">
                <div class="card-title"><span class="material-symbols-rounded">verified</span>Match against verified facts</div>
                @if (j.match; as m) {
                  <p class="pre">{{ m.reason }}</p>
                  <h4 class="mt">Matched</h4>
                  <div class="chips">@for (s of m.matchedSkills; track s) { <span class="chip success">{{ s }}</span> } @empty { <span class="muted small">none</span> }</div>
                  <h4 class="mt">Missing</h4>
                  <div class="chips">@for (s of m.missingSkills; track s) { <span class="chip danger">{{ s }}</span> } @empty { <span class="muted small">none</span> }</div>
                  <p class="muted small mt">Matched {{ m.matchedAt | date:'medium' }}</p>
                } @else { <div class="empty"><span class="material-symbols-rounded">rule</span>Match appears after analysis.</div> }
              </section>
            </div>
          </p-tabpanel>

          <p-tabpanel value="resume">
            <div class="stack">
              @for (r of resumes(); track r.id) {
                <section class="card">
                  <div class="card-title"><span class="material-symbols-rounded">description</span>Proposed resume changes<span class="grow"></span><button pButton outlined type="button" [disabled]="busy()" (click)="previewResume(r)"><span class="material-symbols-rounded">preview</span>Preview resume</button><app-status-chip [status]="r.status" /></div>
                  @if (r.status !== 'GENERATED') {
                    <div class="row actions mt">
                      <span class="small">Template:</span>
                      <button pButton text type="button" [class.primary]="r.template !== 'CountryTemplate'" [disabled]="busy() || r.template !== 'CountryTemplate'" (click)="setTemplate(r, 'KeepMasterTemplate')">Keep my template</button>
                      <button pButton text type="button" [disabled]="busy() || r.template === 'CountryTemplate' || !r.countryCode" (click)="setTemplate(r, 'CountryTemplate')">Use {{ r.countryCode ?? 'country' }} template</button>
                      <span class="small muted">Changing it cancels any pending approval.</span>
                    </div>
                  }
                  @if (r.approvalId) { <div class="alert info"><span class="material-symbols-rounded">fact_check</span><span>Approval pending — review and execute it under <a routerLink="/approvals">Approvals</a>. The .docx is written only after execution.</span></div> }
                  <p-accordion class="mt">
                    @for (c of r.changes; track $index) {
                      <p-accordion-panel [value]="$index">
                        <p-accordion-header><span class="panel-title">{{ c.section }}</span></p-accordion-header><p-accordion-content>
                        <div class="diff">
                          <div class="before"><span class="label">Before</span><p class="pre">{{ c.oldText || '—' }}</p></div>
                          <div class="after"><span class="label">After</span><p class="pre">{{ c.newText }}</p></div>
                        </div>
                        <div class="chips mt"><span class="muted small">Evidence:</span>@for (e of c.evidence; track e) { <span class="chip success">{{ e }}</span> }</div>
                      </p-accordion-content></p-accordion-panel>
                    }
                  </p-accordion>
                  @if (r.outputPath) { <p class="muted small mono mt">{{ r.outputPath }}</p> }
                </section>
              }
              @for (e of emails(); track e.id) {
                <section class="card">
                  <div class="card-title"><span class="material-symbols-rounded">mail</span>{{ e.kind.replace('_', ' ') }}<span class="grow"></span><app-status-chip [status]="e.status" /></div>
                  <p><b>{{ e.subject }}</b></p>
                  <p class="muted small">To: {{ e.to || '—' }}</p>
                  <p class="pre mt">{{ e.body }}</p>
                  <div class="row end mt"><a pButton text routerLink="/emails">Edit or request approval<span class="material-symbols-rounded">arrow_forward</span></a></div>
                </section>
              }
              @if (!resumes().length && !emails().length) {
                <div class="card empty"><span class="material-symbols-rounded">drafts</span>Nothing prepared yet. Use “Prepare application” or draft a recruiter email.</div>
              }
            </div>
          </p-tabpanel>

          <p-tabpanel value="description">
            <section class="card"><p class="pre">{{ j.description }}</p></section>
          </p-tabpanel>

          @if (application(); as app) {
            <p-tabpanel value="history">
              <section class="card">
                @for (h of app.history; track h.at) {
                  <div class="item"><div class="row"><span class="muted small">{{ h.at | date:'medium' }}</span><app-status-chip [status]="h.to" /></div><span class="small">{{ h.reason }}</span></div>
                } @empty { <div class="empty">No transitions yet.</div> }
              </section>
            </p-tabpanel>
          }
        </p-tabpanels>
        </p-tabs>
      } @else if (!error()) { <app-loading-skeleton variant="detail" [rows]="8" label="Loading job" /> }
    </div>
  `,
  styles: [`
    .back { justify-self: start; }
    .hero { display: flex; justify-content: space-between; gap: 1.5rem; align-items: center; }
    .identity { display: grid; gap: .5rem; }
    .identity h1 { font-size: 1.5rem; }
    .score-card { display: grid; justify-items: center; padding: .75rem 1.25rem; border-radius: 12px; background: var(--ajh-success-soft); color: var(--ajh-success); flex: none; }
    .score-card.low { background: var(--ajh-warn-soft); color: var(--ajh-warn); }
    .score-card b { font-size: 2rem; line-height: 1; }
    .score-card span { font-size: .75rem; text-transform: uppercase; letter-spacing: .05em; }
    .actions { align-items: center; }
    .recipient { width: min(100%, 280px); }
    .country { width: 220px; }
    h4 { font-size: .85rem; text-transform: uppercase; letter-spacing: .05em; color: var(--ajh-muted); margin-bottom: .4rem; }
    .list { margin: 0; padding-left: 1.2rem; display: grid; gap: .35rem; line-height: 1.5; }
    .diff { display: grid; gap: 1rem; grid-template-columns: 1fr 1fr; }
    .diff .label { font-size: .72rem; text-transform: uppercase; letter-spacing: .05em; font-weight: 600; color: var(--ajh-muted); }
    .diff .before p { color: var(--ajh-muted); text-decoration: line-through; text-decoration-color: rgba(192,57,43,.4); }
    .diff .after p { background: var(--ajh-success-soft); border-radius: 8px; padding: .5rem .75rem; }
    @media (max-width: 720px) { .hero { flex-direction: column; align-items: flex-start; } .diff { grid-template-columns: 1fr; } }
  `]
})
export class JobDetailComponent {
  private readonly api = inject(ApiService);
  private readonly notify = inject(NotifyService);
  readonly job = signal<Job | null>(null);
  readonly application = signal<JobApplication | null>(null);
  readonly resumes = signal<ResumeVersion[]>([]);
  readonly emails = signal<EmailDraft[]>([]);
  readonly country = signal<CountryProfile | null>(null);
  readonly countryOptions = [
    { code: 'US', name: 'United States' }, { code: 'CA', name: 'Canada' }, { code: 'GB', name: 'United Kingdom' }, { code: 'IE', name: 'Ireland' },
    { code: 'DE', name: 'Germany' }, { code: 'NL', name: 'Netherlands' }, { code: 'AU', name: 'Australia' }, { code: 'NZ', name: 'New Zealand' },
    { code: 'AE', name: 'United Arab Emirates' }, { code: 'SG', name: 'Singapore' }, { code: 'IN', name: 'India' }
  ];
  readonly error = signal('');
  readonly busy = signal(false);
  recipient = '';
  contactSource = '';
  private jobId = '';

  @Input() set id(value: string) { this.jobId = value; void this.load(); }

  async load(): Promise<void> {
    try {
      const [job, apps, resumes, emails] = await Promise.all([this.api.job(this.jobId), this.api.applications(), this.api.resumes(), this.api.emails()]);
      this.job.set(job);
      this.application.set(apps.find(a => a.jobId === this.jobId) ?? null);
      this.resumes.set(resumes.filter(r => r.jobId === this.jobId));
      this.emails.set(emails.filter(e => e.jobId === this.jobId));
      this.country.set(await this.api.countryProfile(this.jobId));
    } catch (e) { this.error.set(describeError(e)); }
  }

  analyze(): Promise<void> { return this.run(async () => { await this.api.analyzeJob(this.jobId); this.notify.success('Analysis and match complete.'); }); }
  prepare(): Promise<void> {
    return this.run(async () => {
      const result = await this.api.prepareJob(this.jobId);
      this.notify.info(result.resumeApproval ? 'Resume changes proposed. Approve them in Approvals before anything is written.' : 'Application prepared.');
    });
  }
  setCountry(code: string): Promise<void> {
    return this.run(async () => { await this.api.setJobCountry(this.jobId, code || null); });
  }
  setTemplate(resume: ResumeVersion, template: string): Promise<void> {
    return this.run(async () => {
      await this.api.setResumeTemplate(resume.id, template);
      this.notify.info('Template changed. Request approval again before generating.');
    });
  }
  previewResume(resume: ResumeVersion): Promise<void> {
    return this.run(async () => {
      this.notify.previewResume(await this.api.resumePreview(resume.id));
    }, false);
  }
  draftRecruiter(): Promise<void> { return this.run(async () => { await this.api.recruiterEmail(this.jobId, this.recipient); this.notify.success('Recruiter email drafted. Nothing has been sent.'); }); }
  saveContact(status: 'USER_PROVIDED' | 'NOT_FOUND'): Promise<void> {
    return this.run(async () => {
      await this.api.setRecruiterContact(this.jobId, status, null, status === 'NOT_FOUND' ? null : this.recipient, status === 'NOT_FOUND' ? null : this.contactSource);
      this.notify.success(status === 'NOT_FOUND' ? 'Marked as no recruiter contact found.' : 'Recruiter contact saved.');
    });
  }

  private async run(action: () => Promise<void>, reload = true): Promise<void> {
    this.busy.set(true); this.error.set('');
    try { await action(); if (reload) await this.load(); } catch (e) { this.error.set(describeError(e)); } finally { this.busy.set(false); }
  }
}
