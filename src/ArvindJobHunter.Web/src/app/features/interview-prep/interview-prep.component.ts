import { Component, Input, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { SelectModule } from 'primeng/select';
import { ApiService } from '../../core/api.service';
import { InterviewPrepPlan, InterviewResource, Job } from '../../core/models';
import { describeError } from '../../core/auth.interceptor';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { ButtonModule } from 'primeng/button';
import { TooltipModule } from 'primeng/tooltip';
import { ProgressBarModule } from 'primeng/progressbar';

@Component({
  selector: 'app-interview-prep',
  standalone: true,
  imports: [ButtonModule, TooltipModule, ProgressBarModule, FormsModule, RouterLink, DatePipe, SelectModule, PageHeaderComponent],
  template: `
    <div class="page">
      <app-page-header eyebrow="Get ready" title="Interview prep" icon="school" subtitle="Curated external resources for the skills this role requires. Links open in a new tab — nothing is downloaded or embedded here.">
        <div class="field">
          <label class="field-label" for="prep-job">Job</label>
          <p-select inputId="prep-job" [ngModel]="selectedId()" (ngModelChange)="select($event)" [options]="jobs()" optionValue="id" placeholder="Choose a job" appendTo="body">
            <ng-template #selectedItem let-j>{{ j.title }} · {{ j.company }}</ng-template><ng-template #item let-j>{{ j.title }} · {{ j.company }}</ng-template>
          </p-select>
        </div>
      </app-page-header>

      @if (busy()) { <p-progressbar mode="indeterminate" styleClass="busy-bar" /> }
      @if (error()) { <div class="alert error"><span class="material-symbols-rounded">error</span><span>{{ error() }}</span></div> }

      @if (plan(); as p) {
        <section class="card focus">
          <div class="row between">
            <div>
              <h3>{{ p.jobTitle }} <span class="muted">· {{ p.company }}</span></h3>
              <p class="muted small">Generated {{ p.generatedAt | date:'medium' }}</p>
            </div>
            <a pButton text [routerLink]="['/jobs', p.jobId]">Job details<span class="material-symbols-rounded">arrow_forward</span></a>
          </div>
          <div class="chips mt"><span class="muted small">Focus:</span>@for (s of p.focusSkills; track s) { <span class="chip primary">{{ s }}</span> }</div>
        </section>

        <div class="grid-2">
          @for (g of p.groups; track g.topic) {
            <section class="card">
              <div class="card-title"><span class="material-symbols-rounded">topic</span>{{ g.topic }}</div>
              <div class="resources">
                @for (r of g.resources; track r.url) {
                  <a class="resource" [href]="r.url" target="_blank" rel="noopener noreferrer external" [pTooltip]="r.url">
                    <span class="material-symbols-rounded kind {{ r.kind }}">{{ icon(r) }}</span>
                    <div class="text">
                      <span class="title">{{ r.title }}</span>
                      <span class="muted small">{{ r.provider }}@if (r.why) { · {{ r.why }}}</span>
                    </div>
                    <span class="material-symbols-rounded open">open_in_new</span>
                  </a>
                }
              </div>
            </section>
          }
        </div>
      } @else if (!busy() && !error()) {
        <div class="card empty"><span class="material-symbols-rounded">school</span>{{ jobs().length ? 'Pick a job to see a prep plan.' : 'Add and analyze a job first.' }}</div>
      }
    </div>
  `,
  styles: [`
    .picker { width: min(100%, 360px); }
    .focus h3 { font-size: 1.15rem; }
    .resources { display: grid; gap: .4rem; }
    .resource { display: flex; gap: .75rem; align-items: center; padding: .65rem .75rem; border-radius: 10px; border: 1px solid var(--ajh-border); color: inherit; text-decoration: none; transition: background .15s, border-color .15s; }
    .resource:hover { background: #f7f9fc; border-color: #c9d9f5; text-decoration: none; }
    .resource .text { display: grid; gap: .1rem; min-width: 0; flex: 1; }
    .resource .title { font-weight: 500; }
    .kind { flex: none; color: var(--ajh-muted); }
    .kind.video { color: #c0392b; } .kind.website { color: var(--ajh-primary); } .kind.search { color: var(--ajh-success); }
    .open { flex: none; color: var(--ajh-muted); font-size: 1.1rem; width: 1.1rem; height: 1.1rem; }
  `]
})
export class InterviewPrepComponent {
  private readonly api = inject(ApiService);
  readonly jobs = signal<Job[]>([]);
  readonly plan = signal<InterviewPrepPlan | null>(null);
  readonly selectedId = signal<string | null>(null);
  readonly error = signal('');
  readonly busy = signal(false);
  private routeId: string | null = null;

  @Input() set id(value: string | undefined) { this.routeId = value ?? null; if (value) void this.select(value); }

  constructor() {
    this.api.jobs(true).then(jobs => {
      const usable = jobs.filter(j => j.status !== 'DISMISSED');
      this.jobs.set(usable);
      if (!this.routeId && usable.length === 1) void this.select(usable[0].id);
    }).catch(e => this.error.set(describeError(e)));
  }

  icon(r: InterviewResource): string { return r.kind === 'video' ? 'play_circle' : r.kind === 'search' ? 'search' : 'language'; }

  async select(jobId: string): Promise<void> {
    this.selectedId.set(jobId); this.busy.set(true); this.error.set('');
    try { this.plan.set(await this.api.interviewPrep(jobId)); } catch (e) { this.plan.set(null); this.error.set(describeError(e)); } finally { this.busy.set(false); }
  }
}
