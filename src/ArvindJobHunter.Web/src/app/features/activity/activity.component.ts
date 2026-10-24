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
  templateUrl: './activity.component.html',
  styleUrl: './activity.component.scss'
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
