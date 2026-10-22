import { Component, inject, signal } from '@angular/core';
import { DatePipe, SlicePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TabsModule } from 'primeng/tabs';
import { AccordionModule } from 'primeng/accordion';
import { TableModule } from 'primeng/table';
import { ApiService } from '../../core/api.service';
import { AgentRun, AuditEvent } from '../../core/models';
import { describeError } from '../../core/auth.interceptor';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { StatusChipComponent } from '../../shared/status-chip.component';
import { LoadingSkeletonComponent } from '../../shared/loading-skeleton.component';
import { ButtonModule } from 'primeng/button';
import { TooltipModule } from 'primeng/tooltip';
import { ProgressBarModule } from 'primeng/progressbar';

@Component({
  selector: 'app-activity',
  standalone: true,
  imports: [ButtonModule, TooltipModule, ProgressBarModule, DatePipe, SlicePipe, RouterLink, TabsModule, AccordionModule, TableModule, PageHeaderComponent, StatusChipComponent, LoadingSkeletonComponent],
  template: `
    <div class="page">
      <app-page-header eyebrow="Transparency" title="Activity" icon="history" subtitle="Every agent run and every audited action, with tool calls and outcomes.">
        <button pButton outlined (click)="load()" [disabled]="busy()"><span class="material-symbols-rounded">refresh</span>Refresh</button>
      </app-page-header>

      @if (busy()) { <p-progressbar mode="indeterminate" styleClass="busy-bar" /> }
      @if (error()) { <div class="alert error"><span class="material-symbols-rounded">error</span><span>{{ error() }}</span></div> }

      <p-tabs value="runs">
        <p-tablist>
          <p-tab value="runs">Agent runs</p-tab>
          <p-tab value="audit">Audit log</p-tab>
        </p-tablist>
        <p-tabpanels>
        <p-tabpanel value="runs">
          <p-accordion>
            @for (r of runs(); track r.id) {
              <p-accordion-panel [value]="r.id">
                <p-accordion-header>
                  <span class="panel-title title">
                    <app-status-chip [status]="r.status" />
                    <span class="name">{{ r.agentName }} <span class="muted">· {{ r.workflow }}</span></span>
                  </span>
                  <span class="panel-desc desc">
                    <app-status-chip [status]="r.mode" /><span class="chip">{{ r.llmProvider }}</span>
                    <span class="muted small">{{ r.startedAt | date:'medium' }}</span>
                  </span>
                </p-accordion-header><p-accordion-content>
                <div class="stack">
                  <div class="row small muted">
                    <span>Started {{ r.startedAt | date:'medium' }}</span>
                    @if (r.completedAt) { <span>· finished {{ r.completedAt | date:'mediumTime' }}</span> }
                    @if (r.jobId) { <span>·</span><a [routerLink]="['/jobs', r.jobId]">view job</a> }
                  </div>
                  @if (r.error) { <div class="alert error"><span class="material-symbols-rounded">error</span><span>{{ r.error }}</span></div> }
                  @if (r.toolCalls.length) {
                    <p-table [value]="r.toolCalls" styleClass="calls" size="small">
                      <ng-template #header><tr><th></th><th>Tool</th><th>Input</th><th>Output</th><th>Time</th></tr></ng-template>
                      <ng-template #body let-c>
                        <tr>
                          <td><span class="material-symbols-rounded" [class.ok]="c.succeeded" [class.bad]="!c.succeeded">{{ c.succeeded ? 'check_circle' : 'cancel' }}</span></td>
                          <td><b>{{ c.toolName }}</b></td>
                          <td class="small">{{ c.inputSummary }}</td>
                          <td class="small">{{ c.outputSummary }}</td>
                          <td class="muted small">{{ c.durationMs }} ms</td>
                        </tr>
                      </ng-template>
                    </p-table>
                  } @else { <span class="muted small">No tool calls recorded.</span> }
                </div>
              </p-accordion-content></p-accordion-panel>
            } @empty {
              @if (!loaded()) { <app-loading-skeleton variant="list" [rows]="5" label="Loading agent runs" /> }
              @else { <div class="card empty"><span class="material-symbols-rounded">smart_toy</span>No agent runs yet. Analyze a job to start one.</div> }
            }
          </p-accordion>
        </p-tabpanel>

        <p-tabpanel value="audit">
          <section class="card">
            @for (e of audit(); track e.id) {
              <div class="item audit">
                <span class="muted small when">{{ e.at | date:'MMM d, HH:mm:ss' }}</span>
                <div class="what">
                  <div class="row"><b>{{ e.action }}</b><app-status-chip [status]="e.outcome" /><span class="chip">{{ e.targetType }}</span></div>
                  @if (e.details) { <span class="small">{{ e.details }}</span> }
                  <span class="muted small mono" [pTooltip]="e.targetId">{{ e.targetId }}</span>
                  @if (e.correlationId) {
                    <span class="muted small mono" [pTooltip]="'Correlation id ' + e.correlationId + ' — search for it in the Markdown log (data/logs) to see everything that happened in this request.'">log #{{ e.correlationId | slice:0:12 }}</span>
                  }
                </div>
              </div>
            } @empty {
              @if (!loaded()) { <app-loading-skeleton variant="table" [rows]="6" [columns]="3" label="Loading audit log" /> }
              @else { <div class="empty"><span class="material-symbols-rounded">receipt_long</span>No audit events yet.</div> }
            }
          </section>
        </p-tabpanel>
      </p-tabpanels>
      </p-tabs>
    </div>
  `,
  styles: [`
    .title { display: flex; gap: .6rem; align-items: center; min-width: 0; }
    .name { font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .desc { justify-content: flex-end; gap: .4rem; align-items: center; }
    :host ::ng-deep .calls { width: 100%; }
    .ok { color: var(--ajh-success); } .bad { color: var(--ajh-danger); }
    .audit { display: grid; grid-template-columns: 150px 1fr; gap: 1rem; }
    .what { display: grid; gap: .25rem; min-width: 0; }
    .mono { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    @media (max-width: 640px) { .audit { grid-template-columns: 1fr; gap: .25rem; } }
  `]
})
export class ActivityComponent {
  private readonly api = inject(ApiService);
  readonly runs = signal<AgentRun[]>([]);
  readonly audit = signal<AuditEvent[]>([]);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly loaded = signal(false);

  constructor() { void this.load(); }

  async load(): Promise<void> {
    this.busy.set(true); this.error.set('');
    try {
      const [runs, audit] = await Promise.all([this.api.agentRuns(), this.api.auditLogs()]);
      this.runs.set(runs); this.audit.set(audit);
    } catch (e) { this.error.set(describeError(e)); } finally { this.busy.set(false); this.loaded.set(true); }
  }
}
