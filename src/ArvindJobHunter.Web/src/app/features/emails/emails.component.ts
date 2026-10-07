import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ApiService } from '../../core/api.service';
import { EmailDraft } from '../../core/models';
import { describeError } from '../../core/auth.interceptor';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { StatusChipComponent } from '../../shared/status-chip.component';
import { NotifyService } from '../../shared/notify.service';

@Component({
  selector: 'app-emails',
  standalone: true,
  imports: [FormsModule, RouterLink, DatePipe, MatIconModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatProgressBarModule, MatExpansionModule, MatTooltipModule, PageHeaderComponent, StatusChipComponent],
  template: `
    <div class="page">
      <app-page-header eyebrow="Outreach" title="Email drafts" icon="mail" subtitle="Edit freely — editing resets any prior approval. Sending requires approval and explicit execution, and only happens in LIVE mode with Gmail connected." />

      @if (busy()) { <mat-progress-bar mode="indeterminate" /> }
      @if (error()) { <div class="alert error"><mat-icon class="material-symbols-rounded">error</mat-icon><span>{{ error() }}</span></div> }

      <mat-accordion>
        @for (e of drafts(); track e.id) {
          <mat-expansion-panel [expanded]="drafts().length === 1">
            <mat-expansion-panel-header>
              <mat-panel-title class="title">
                <mat-icon class="material-symbols-rounded muted">{{ e.kind === 'COVER_LETTER' ? 'article' : 'outgoing_mail' }}</mat-icon>
                <span class="subject">{{ e.subject || '(no subject)' }}</span>
              </mat-panel-title>
              <mat-panel-description class="desc">
                <app-status-chip [status]="e.kind" />
                <app-status-chip [status]="e.status" />
                @if (e.approvalId) { <span class="chip warn">approval requested</span> }
              </mat-panel-description>
            </mat-expansion-panel-header>

            <form class="stack" (ngSubmit)="save(e)">
              <div class="grid-2">
                <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>To</mat-label><input matInput [(ngModel)]="e.to" name="to-{{ e.id }}" type="email" /></mat-form-field>
                <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>Subject</mat-label><input matInput [(ngModel)]="e.subject" name="subject-{{ e.id }}" /></mat-form-field>
              </div>
              <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>Body</mat-label><textarea matInput rows="12" [(ngModel)]="e.body" name="body-{{ e.id }}"></textarea></mat-form-field>
              <div class="row between">
                <span class="muted small">Updated {{ e.updatedAt | date:'medium' }}@if (e.jobId) { · <a [routerLink]="['/jobs', e.jobId]">view job</a>}</span>
                <div class="row">
                  <button mat-button type="button" class="danger" [disabled]="busy()" (click)="remove(e)">Delete</button>
                  <button mat-stroked-button type="submit" [disabled]="busy()"><mat-icon class="material-symbols-rounded">save</mat-icon>Save</button>
                  <button mat-flat-button color="primary" type="button" [disabled]="busy()" matTooltip="Saves, then creates an approval request. Nothing is sent until you execute it under Approvals." (click)="requestApproval(e)"><mat-icon class="material-symbols-rounded">fact_check</mat-icon>Request approval to send</button>
                </div>
              </div>
            </form>
          </mat-expansion-panel>
        } @empty {
          <div class="card empty"><mat-icon class="material-symbols-rounded">drafts</mat-icon>No drafts yet. Prepare an application or draft a recruiter email from a <a routerLink="/jobs">job</a>.</div>
        }
      </mat-accordion>
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

  constructor() { void this.load(); }

  async load(): Promise<void> { try { this.drafts.set(await this.api.emails()); } catch (e) { this.error.set(describeError(e)); } }

  save(e: EmailDraft) { return this.run(async () => { await this.api.updateEmail(e.id, { to: e.to, subject: e.subject, body: e.body }); this.notify.success('Saved.'); }); }
  requestApproval(e: EmailDraft) { return this.run(async () => { await this.api.updateEmail(e.id, { to: e.to, subject: e.subject, body: e.body }); await this.api.requestEmailApproval(e.id); this.notify.info('Approval requested. Review it under Approvals.'); }); }
  async remove(e: EmailDraft) {
    if (!await this.notify.confirm({ title: 'Delete this draft?', message: e.subject || '(no subject)', confirmLabel: 'Delete', danger: true })) return;
    await this.run(() => this.api.deleteEmail(e.id));
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true); this.error.set('');
    try { await action(); await this.load(); } catch (err) { this.error.set(describeError(err)); } finally { this.busy.set(false); }
  }
}
