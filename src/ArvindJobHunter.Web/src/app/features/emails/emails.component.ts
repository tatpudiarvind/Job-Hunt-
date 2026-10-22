import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AccordionModule } from 'primeng/accordion';
import { ApiService } from '../../core/api.service';
import { EmailDraft } from '../../core/models';
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
  selector: 'app-emails',
  standalone: true,
  imports: [ButtonModule, InputTextModule, TextareaModule, TooltipModule, ProgressBarModule, FormsModule, RouterLink, DatePipe, AccordionModule, PageHeaderComponent, StatusChipComponent, LoadingSkeletonComponent],
  template: `
    <div class="page">
      <app-page-header eyebrow="Outreach" title="Email drafts" icon="mail" subtitle="Edit freely — editing resets any prior approval. Sending requires approval and explicit execution, and only happens in LIVE mode with Gmail connected." />

      @if (busy()) { <p-progressbar mode="indeterminate" styleClass="busy-bar" /> }
      @if (error()) { <div class="alert error"><span class="material-symbols-rounded">error</span><span>{{ error() }}</span></div> }

      <p-accordion [value]="drafts().length === 1 ? drafts()[0].id : undefined">
        @for (e of drafts(); track e.id) {
          <p-accordion-panel [value]="e.id">
            <p-accordion-header>
              <span class="panel-title title">
                <span class="material-symbols-rounded muted">{{ e.kind === 'COVER_LETTER' ? 'article' : 'outgoing_mail' }}</span>
                <span class="subject">{{ e.subject || '(no subject)' }}</span>
              </span>
              <span class="panel-desc desc">
                <app-status-chip [status]="e.kind" />
                <app-status-chip [status]="e.status" />
                @if (e.approvalId) { <span class="chip warn">approval requested</span> }
              </span>
            </p-accordion-header><p-accordion-content>

            <form class="stack" (ngSubmit)="save(e)">
              <div class="grid-2">
                <div class="field"><label class="field-label" for="f-to-e-id">To</label><input id="f-to-e-id" pInputText [(ngModel)]="e.to" name="to-{{ e.id }}" type="email" /></div>
                <div class="field"><label class="field-label" for="f-subject-e-id">Subject</label><input id="f-subject-e-id" pInputText [(ngModel)]="e.subject" name="subject-{{ e.id }}" /></div>
              </div>
              <div class="field"><label class="field-label" for="f-body-e-id">Body</label><textarea id="f-body-e-id" pTextarea rows="12" [(ngModel)]="e.body" name="body-{{ e.id }}"></textarea></div>
              <div class="row between">
                <span class="muted small">Updated {{ e.updatedAt | date:'medium' }}@if (e.jobId) { · <a [routerLink]="['/jobs', e.jobId]">view job</a>}</span>
                <div class="row">
<button pButton text type="button" class="danger" [disabled]="busy()" (click)="remove(e)">Delete</button>
@if (e.kind === 'COVER_LETTER') {
  <button pButton outlined type="button" [disabled]="busy()" pTooltip="Creates an approval to write the letter as Word + PDF in your resume's style." (click)="requestDocument(e)"><span class="material-symbols-rounded">description</span>Generate document</button>
}
<button pButton outlined type="submit" [disabled]="busy()"><span class="material-symbols-rounded">save</span>Save</button>
<button pButton type="button" [disabled]="busy()" pTooltip="Saves, then creates an approval request. Nothing is sent until you execute it under Approvals." (click)="requestApproval(e)"><span class="material-symbols-rounded">fact_check</span>Request approval to send</button>
                </div>
              </div>
            </form>
          </p-accordion-content></p-accordion-panel>
        } @empty {
          @if (!loaded()) {
            <app-loading-skeleton variant="list" [rows]="3" label="Loading email drafts" />
          } @else {
          <div class="card empty"><span class="material-symbols-rounded">drafts</span>No drafts yet. Prepare an application or draft a recruiter email from a <a routerLink="/jobs">job</a>.</div>
          }
        }
      </p-accordion>
    </div>
  `,
  styles: [`
    .title { display: flex; gap: .6rem; align-items: center; min-width: 0; }
    .subject { font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .desc { justify-content: flex-end; gap: .4rem; align-items: center; }
    .danger { color: var(--ajh-danger); }
    form { padding-top: .5rem; }
  `]
})
export class EmailsComponent {
  private readonly api = inject(ApiService);
  private readonly notify = inject(NotifyService);
  readonly drafts = signal<EmailDraft[]>([]);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly loaded = signal(false);

  constructor() { void this.load(); }

  async load(): Promise<void> { try { this.drafts.set(await this.api.emails()); } catch (e) { this.error.set(describeError(e)); } finally { this.loaded.set(true); } }

  save(e: EmailDraft) { return this.run(async () => { await this.api.updateEmail(e.id, { to: e.to, subject: e.subject, body: e.body }); this.notify.success('Saved.'); }); }
  requestApproval(e: EmailDraft) { return this.run(async () => { await this.api.updateEmail(e.id, { to: e.to, subject: e.subject, body: e.body }); await this.api.requestEmailApproval(e.id); this.notify.info('Approval requested. Review it under Approvals.'); }); }
  requestDocument(e: EmailDraft) { return this.run(async () => { await this.api.updateEmail(e.id, { to: e.to, subject: e.subject, body: e.body }); await this.api.requestCoverLetterDocument(e.id); this.notify.info('Approval requested to generate the cover letter document.'); }); }
  async remove(e: EmailDraft) {
    if (!await this.notify.confirm({ title: 'Delete this draft?', message: e.subject || '(no subject)', confirmLabel: 'Delete', danger: true })) return;
    await this.run(() => this.api.deleteEmail(e.id));
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true); this.error.set('');
    try { await action(); await this.load(); } catch (err) { this.error.set(describeError(err)); } finally { this.busy.set(false); }
  }
}
