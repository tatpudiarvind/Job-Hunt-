import { Component, computed, input } from '@angular/core';

const TONES: Record<string, string> = {
  // approvals / receipts
  PENDING: 'warn', APPROVED: 'primary', CONSUMED: 'success', REJECTED: 'danger', EXPIRED: 'danger', INVALIDATED: 'danger',
  SUCCESS: 'success', FAILED: 'danger', BLOCKED: 'danger', SIMULATED: 'primary', SKIPPED: '',
  // jobs
  DISCOVERED: '', ANALYZED: 'primary', QUALIFIED: 'success', DISMISSED: 'danger',
  // applications
  MATCHED: 'primary', SHORTLISTED: 'primary', RESUME_PREPARED: 'primary', AWAITING_APPROVAL: 'warn',
  APPLIED: 'success', RECRUITER_CONTACTED: 'success', RECRUITER_REPLIED: 'success',
  INTERVIEW: 'success', TECHNICAL_INTERVIEW: 'success', HR_INTERVIEW: 'success', OFFER: 'success',
  WITHDRAWN: '', CLOSED: '',
  // modes / misc
  DEMO: '', DRY_RUN: 'warn', LIVE: 'danger', RUNNING: 'warn', COMPLETED: 'success', DRAFT: '', SENT: 'success'
};

@Component({
  selector: 'app-status-chip',
  standalone: true,
  template: `<span class="chip {{ tone() }}">{{ label() }}</span>`
})
export class StatusChipComponent {
  readonly status = input.required<string>();
  readonly tone = computed(() => TONES[this.status()] ?? '');
  readonly label = computed(() => this.status().replace(/_/g, ' '));
}
