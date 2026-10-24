import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe, SlicePipe } from '@angular/common';
import { TabsModule } from 'primeng/tabs';
import { BadgeModule } from 'primeng/badge';
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
  imports: [ButtonModule, TooltipModule, ProgressBarModule, DatePipe, SlicePipe, TabsModule, BadgeModule, PageHeaderComponent, StatusChipComponent, LoadingSkeletonComponent],
  templateUrl: './approvals.component.html',
  styleUrl: './approvals.component.scss'
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
