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
  templateUrl: './emails.component.html',
  styleUrl: './emails.component.scss'
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
