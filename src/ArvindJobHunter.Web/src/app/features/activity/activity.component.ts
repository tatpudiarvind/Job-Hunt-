import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ApiService } from '../../core/api.service';
import { AgentRun, AuditEvent } from '../../core/models';
import { describeError } from '../../core/auth.interceptor';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { StatusChipComponent } from '../../shared/status-chip.component';

@Component({
  selector: 'app-activity',
  standalone: true,
  imports: [DatePipe, RouterLink, MatIconModule, MatButtonModule, MatProgressBarModule, MatTabsModule, MatExpansionModule, MatTableModule, MatTooltipModule, PageHeaderComponent, StatusChipComponent],
  template: `
    <div class="page">
      <app-page-header eyebrow="Transparency" title="Activity" icon="history" subtitle="Every agent run and every audited action, with tool calls and outcomes.">
        <button mat-stroked-button (click)="load()" [disabled]="busy()"><mat-icon class="material-symbols-rounded">refresh</mat-icon>Refresh</button>
      </app-page-header>

      @if (busy()) { <mat-progress-bar mode="indeterminate" /> }
      @if (error()) { <div class="alert error"><mat-icon class="material-symbols-rounded">error</mat-icon><span>{{ error() }}</span></div> }

      <mat-tab-group animationDuration="150ms">
        <mat-tab label="Agent runs">
          <mat-accordion>
            @for (r of runs(); track r.id) {
              <mat-expansion-panel>
                <mat-expansion-panel-header>
                  <mat-panel-title class="title">
                    <app-status-chip [status]="r.status" />
                    <span class="name">{{ r.agentName }} <span class="muted">· {{ r.workflow }}</span></span>
                  </mat-panel-title>
                  <mat-panel-description class="desc">
                    <app-status-chip [status]="r.mode" /><span class="chip">{{ r.llmProvider }}</span>
                    <span class="muted small">{{ r.startedAt | date:'medium' }}</span>
                  </mat-panel-description>
                </mat-expansion-panel-header>
                <div class="stack">
                  <div class="row small muted">
                    <span>Started {{ r.startedAt | date:'medium' }}</span>
                    @if (r.completedAt) { <span>· finished {{ r.completedAt | date:'mediumTime' }}</span> }
                    @if (r.jobId) { <span>·</span><a [routerLink]="['/jobs', r.jobId]">view job</a> }
                  </div>
                  @if (r.error) { <div class="alert error"><mat-icon class="material-symbols-rounded">error</mat-icon><span>{{ r.error }}</span></div> }
                  @if (r.toolCalls.length) {
                    <table mat-table [dataSource]="r.toolCalls" class="calls">
                      <ng-container matColumnDef="ok"><th mat-header-cell *matHeaderCellDef></th><td mat-cell *matCellDef="let c"><mat-icon class="material-symbols-rounded" [class.ok]="c.succeeded" [class.bad]="!c.succeeded">{{ c.succeeded ? 'check_circle' : 'cancel' }}</mat-icon></td></ng-container>
                      <ng-container matColumnDef="tool"><th mat-header-cell *matHeaderCellDef>Tool</th><td mat-cell *matCellDef="let c"><b>{{ c.toolName }}</b></td></ng-container>
                      <ng-container matColumnDef="input"><th mat-header-cell *matHeaderCellDef>Input</th><td mat-cell *matCellDef="let c" class="small">{{ c.inputSummary }}</td></ng-container>
                      <ng-container matColumnDef="output"><th mat-header-cell *matHeaderCellDef>Output</th><td mat-cell *matCellDef="let c" class="small">{{ c.outputSummary }}</td></ng-container>
                      <ng-container matColumnDef="ms"><th mat-header-cell *matHeaderCellDef>Time</th><td mat-cell *matCellDef="let c" class="muted small">{{ c.durationMs }} ms</td></ng-container>
                      <tr mat-header-row *matHeaderRowDef="callCols"></tr>
                      <tr mat-row *matRowDef="let row; columns: callCols"></tr>
                    </table>
                  } @else { <span class="muted small">No tool calls recorded.</span> }
                </div>
              </mat-expansion-panel>
            } @empty { <div class="card empty"><mat-icon class="material-symbols-rounded">smart_toy</mat-icon>No agent runs yet. Analyze a job to start one.</div> }
          </mat-accordion>
        </mat-tab>

        <mat-tab label="Audit log">
          <section class="card">
            @for (e of audit(); track e.id) {
              <div class="item audit">
                <span class="muted small when">{{ e.at | date:'MMM d, HH:mm:ss' }}</span>
                <div class="what">
                  <div class="row"><b>{{ e.action }}</b><app-status-chip [status]="e.outcome" /><span class="chip">{{ e.targetType }}</span></div>
                  @if (e.details) { <span class="small">{{ e.details }}</span> }
                  <span class="muted small mono" [matTooltip]="e.targetId">{{ e.targetId }}</span>
                </div>
              </div>
            } @empty { <div class="empty"><mat-icon class="material-symbols-rounded">receipt_long</mat-icon>No audit events yet.</div> }
          </section>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
  styles: [`
    .title { display: flex; gap: .6rem; align-items: center; min-width: 0; }
    .name { font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .desc { justify-content: flex-end; gap: .4rem; align-items: center; }
    .calls { width: 100%; }
    .calls .ok { color: var(--ajh-success); } .calls .bad { color: var(--ajh-danger); }
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
  readonly callCols = ['ok', 'tool', 'input', 'output', 'ms'];

  constructor() { void this.load(); }

  async load(): Promise<void> {
    this.busy.set(true); this.error.set('');
    try {
      const [runs, audit] = await Promise.all([this.api.agentRuns(), this.api.auditLogs()]);
      this.runs.set(runs); this.audit.set(audit);
    } catch (e) { this.error.set(describeError(e)); } finally { this.busy.set(false); }
  }
}
