import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe, SlicePipe } from '@angular/common';
import { MatTabsModule } from '@angular/material/tabs';
import { MatBadgeModule } from '@angular/material/badge';
import { ApiService } from '../../core/api.service';
import { ApprovalRequest, ExecutionReceipt } from '../../core/models';
import { describeError } from '../../core/auth.interceptor';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { StatusChipComponent } from '../../shared/status-chip.component';
import { LoadingSkeletonComponent } from '../../shared/loading-skeleton.component';
import { NotifyService } from '../../shared/notify.service';
import { ButtonModule } from 'primeng/button';
import { TooltipModule } from 'primeng/tooltip';
import { ProgressBarModule } from 'primeng/progressbar';

@Component({
  selector: 'app-approvals',
  standalone: true,
  imports: [ButtonModule, TooltipModule, ProgressBarModule, DatePipe, SlicePipe, MatTabsModule, MatBadgeModule, PageHeaderComponent, StatusChipComponent, LoadingSkeletonComponent],
  template: `
    <div class="page">
      <app-page-header eyebrow="Human in the loop" title="Approvals" icon="fact_check" subtitle="Nothing leaves this machine until you approve it here and then explicitly execute it. Each approval is bound to the exact content you reviewed.">
        <button pButton outlined (click)="load()" [disabled]="busy()"><span class="material-symbols-rounded">refresh</span>Refresh</button>
      </app-page-header>

      @if (busy()) { <p-progressbar mode="indeterminate" styleClass="busy-bar" /> }
      @if (error()) { <div class="alert error"><span class="material-symbols-rounded">error</span><span>{{ error() }}</span></div> }

      <mat-tab-group animationDuration="150ms">
        <mat-tab>
          <ng-template mat-tab-label><span [matBadge]="pending().length" [matBadgeHidden]="!pending().length" matBadgeOverlap="false" matBadgeColor="warn">Pending</span></ng-template>
          <div class="stack">
            @for (a of pending(); track a.id) {
              <section class="card approval">
                <div class="row between">
                  <div class="row"><app-status-chip [status]="a.actionType" /><app-status-chip [status]="a.mode" /><span class="chip">{{ a.targetType }}</span></div>
                  <span class="muted small">expires {{ a.expiresAt | date:'medium' }}</span>
                </div>
                <p class="summary pre">{{ a.summary }}</p>
                <div class="row between">
                  <span class="muted small mono" pTooltip="Content hash — approval is invalidated if the content changes">#{{ a.payloadHash | slice:0:12 }} · requested {{ a.requestedAt | date:'short' }}</span>
                  <div class="row">
                    <button pButton text [disabled]="busy()" (click)="reject(a)">Reject</button>
                    <button pButton [disabled]="busy()" (click)="approve(a)"><span class="material-symbols-rounded">check</span>Approve</button>
                  </div>
                </div>
              </section>
            } @empty {
              @if (!loaded()) { <app-loading-skeleton variant="cards" [rows]="2" label="Loading approvals" /> }
              @else { <div class="card empty"><span class="material-symbols-rounded">task_alt</span>No pending approvals.</div> }
            }
          </div>
        </mat-tab>

        <mat-tab>
          <ng-template mat-tab-label><span [matBadge]="approved().length" [matBadgeHidden]="!approved().length" matBadgeOverlap="false" matBadgeColor="primary">Ready to execute</span></ng-template>
          <div class="stack">
            @if (approved().length) { <div class="alert info"><span class="material-symbols-rounded">info</span><span>Approved actions do nothing until you click Execute. In DEMO and DRY_RUN modes execution is simulated; in LIVE mode it performs the real external action.</span></div> }
            @for (a of approved(); track a.id) {
              <section class="card approval">
                <div class="row between">
                  <div class="row"><app-status-chip [status]="a.actionType" /><app-status-chip [status]="a.mode" /></div>
                  <span class="muted small">approved {{ a.decidedAt | date:'short' }} · expires {{ a.expiresAt | date:'short' }}</span>
                </div>
                <p class="summary pre">{{ a.summary }}</p>
                <div class="row end">
                  <button pButton [severity]="a.mode === 'LIVE' ? 'danger' : 'primary'" [disabled]="busy()" (click)="execute(a)"><span class="material-symbols-rounded">{{ a.mode === 'LIVE' ? 'bolt' : 'play_arrow' }}</span>Execute now ({{ a.mode }})</button>
                </div>
              </section>
            } @empty { <div class="card empty"><span class="material-symbols-rounded">hourglass_empty</span>Nothing approved and waiting.</div> }
          </div>
        </mat-tab>

        <mat-tab label="Receipts">
          <section class="card">
            @for (r of receipts(); track r.id) {
              <div class="item">
                <div class="row between">
                  <div class="row"><app-status-chip [status]="r.result" /><app-status-chip [status]="r.actionType" /><app-status-chip [status]="r.mode" /></div>
                  <span class="muted small">{{ r.executedAt | date:'medium' }}</span>
                </div>
                @if (r.message) { <span class="small">{{ r.message }}</span> }
                <div class="row wrap small">
                  @if (r.websiteUrl) { <a [href]="r.websiteUrl" target="_blank" rel="noopener noreferrer external">Open application website<span class="material-symbols-rounded">open_in_new</span></a> }
                  @if (r.externalReference) { <span class="muted small mono">ref {{ r.externalReference }}</span> }
                </div>
              </div>
            } @empty { <div class="empty"><span class="material-symbols-rounded">receipt_long</span>No executions yet.</div> }
          </section>
        </mat-tab>

        <mat-tab label="History">
          <section class="card">
            @for (a of history(); track a.id) {
              <div class="item">
                <div class="row between">
                  <div class="row"><app-status-chip [status]="a.status" /><app-status-chip [status]="a.actionType" /></div>
                  <span class="muted small">{{ (a.decidedAt ?? a.requestedAt) | date:'medium' }}</span>
                </div>
                <span class="small">{{ a.summary }}</span>
                @if (a.decisionNote) { <span class="muted small">Note: {{ a.decisionNote }}</span> }
              </div>
            } @empty { <div class="empty"><span class="material-symbols-rounded">history</span>No decided approvals yet.</div> }
          </section>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
  styles: [`
    .approval { display: grid; gap: .85rem; }
    .summary { font-size: 1rem; }
  `]
})
export class ApprovalsComponent {
  private readonly api = inject(ApiService);
  private readonly notify = inject(NotifyService);
  readonly approvals = signal<ApprovalRequest[]>([]);
  readonly receipts = signal<ExecutionReceipt[]>([]);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly loaded = signal(false);
  readonly pending = computed(() => this.approvals().filter(a => a.status === 'PENDING'));
  readonly approved = computed(() => this.approvals().filter(a => a.status === 'APPROVED'));
  readonly history = computed(() => this.approvals().filter(a => a.status !== 'PENDING' && a.status !== 'APPROVED'));

  constructor() { void this.load(); }

  async load(): Promise<void> {
    try {
      const [approvals, receipts] = await Promise.all([this.api.approvals(), this.api.receipts()]);
      this.approvals.set(approvals); this.receipts.set(receipts);
    } catch (e) { this.error.set(describeError(e)); } finally { this.loaded.set(true); }
  }

  approve(a: ApprovalRequest): Promise<void> { return this.run(async () => { await this.api.approve(a.id, null); this.notify.success('Approved. It will not run until you click Execute.'); }); }
  async reject(a: ApprovalRequest): Promise<void> {
    const note = await this.notify.prompt({ title: 'Reject this action?', message: a.summary, confirmLabel: 'Reject', danger: true, promptLabel: 'Reason (optional)' });
    if (note === null) return;
    await this.run(async () => { await this.api.reject(a.id, note || null); this.notify.info('Rejected.'); });
  }
  async execute(a: ApprovalRequest): Promise<void> {
    if (a.mode === 'LIVE' && !await this.notify.confirm({ title: 'Perform a real external action?', message: a.summary, confirmLabel: 'Execute in LIVE', danger: true })) return;
    await this.run(async () => {
      const receipt = await this.api.execute(a.id, crypto.randomUUID());
      const text = `${receipt.result}: ${receipt.message ?? ''}`;
      if (receipt.result === 'SUCCESS') this.notify.success(text); else this.notify.error(text);
    });
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true); this.error.set('');
    try { await action(); await this.load(); } catch (e) { this.error.set(describeError(e)); } finally { this.busy.set(false); }
  }
}
