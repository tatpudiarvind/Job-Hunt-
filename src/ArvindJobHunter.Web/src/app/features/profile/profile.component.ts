import { Component, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { SelectModule } from 'primeng/select';
import { SelectButtonModule } from 'primeng/selectbutton';
import { ApiService } from '../../core/api.service';
import { CandidateFact, CandidateProfile } from '../../core/models';
import { describeError } from '../../core/auth.interceptor';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { LoadingSkeletonComponent } from '../../shared/loading-skeleton.component';
import { NotifyService } from '../../shared/notify.service';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TooltipModule } from 'primeng/tooltip';
import { ProgressBarModule } from 'primeng/progressbar';

const FACT_TYPES = ['SKILL', 'EXPERIENCE', 'EDUCATION', 'CERTIFICATION', 'LANGUAGE', 'ACHIEVEMENT'];

function blankProfile(): CandidateProfile {
  return { id: '', name: '', currentRole: '', currentCompany: null, updatedAt: '', version: '' };
}

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [ButtonModule, InputTextModule, TooltipModule, ProgressBarModule, FormsModule, SelectModule, SelectButtonModule, PageHeaderComponent, LoadingSkeletonComponent],
  template: `
    <div class="page">
      <app-page-header eyebrow="Truth layer" title="Profile & verified facts" icon="person" subtitle="Only verified facts are used for matching and resume tailoring. The agent cannot invent experience you have not recorded here." />

      @if (busy()) { <p-progressbar mode="indeterminate" styleClass="busy-bar" /> }
      @if (error()) { <div class="alert error"><span class="material-symbols-rounded">error</span><span>{{ error() }}</span></div> }

      <div class="layout">
        <div class="stack">
          @if (profile(); as p) {
            <section class="card">
              <div class="card-title"><span class="material-symbols-rounded">badge</span>Profile</div>
              <form class="stack" (ngSubmit)="saveProfile()">
                <div class="field"><label class="field-label" for="f-name">Name</label><input id="f-name" pInputText name="name" [(ngModel)]="p.name" required /></div>
                <div class="field"><label class="field-label" for="f-role">Current role</label><input id="f-role" pInputText name="role" [(ngModel)]="p.currentRole" required /></div>
                <div class="field"><label class="field-label" for="f-company">Current company</label><input id="f-company" pInputText name="company" [(ngModel)]="p.currentCompany" /></div>
                <div class="row end"><button pButton type="submit" [disabled]="busy()">Save profile</button></div>
              </form>
            </section>
          } @else if (!loaded()) {
            <app-loading-skeleton variant="form" [rows]="3" label="Loading profile" />
          }
          <section class="card">
            <div class="card-title"><span class="material-symbols-rounded">add_circle</span>Add a fact</div>
            <form class="stack" (ngSubmit)="addFact()">
              <div class="field">
                <label class="field-label" for="fact-type">Type</label>
                <p-select inputId="fact-type" name="type" [(ngModel)]="fact.factType" [options]="types" appendTo="body" />
              </div>
              <div class="field"><label class="field-label" for="f-fname">Name</label><input id="f-fname" pInputText name="fname" [(ngModel)]="fact.name" required placeholder="e.g. Kubernetes" /></div>
              <div class="field"><label class="field-label" for="f-fvalue">Value</label><input id="f-fvalue" pInputText name="fvalue" [(ngModel)]="fact.value" required placeholder="e.g. 3 years, production clusters at Acme" /></div>
              <div class="field"><label class="field-label" for="f-fsrc">Evidence / source</label><input id="f-fsrc" pInputText name="fsrc" [(ngModel)]="fact.sourceReference" placeholder="e.g. Resume 2025 §Experience" /><small class="field-hint">Where this can be verified</small></div>
              <div class="row end"><button pButton outlined type="submit" [disabled]="busy() || !fact.name || !fact.value">Add as unverified</button></div>
            </form>
          </section>
        </div>

        <section class="card">
          <div class="row between toolbar">
            <div class="card-title no-mb"><span class="material-symbols-rounded">verified</span>Facts <span class="muted">({{ facts().length }})</span></div>
            <p-selectbutton [options]="filterOptions()" [ngModel]="filter()" (ngModelChange)="filter.set($event)" optionLabel="label" optionValue="value" [allowEmpty]="false" aria-label="Filter facts" />
          </div>
          @for (f of visible(); track f.id) {
            <div class="item fact">
              <div class="main">
                <div class="row"><span class="chip">{{ f.factType }}</span><b>{{ f.name }}</b>
                  @if (f.isVerified) { <span class="chip success">verified</span> } @else { <span class="chip warn">unverified</span> }
                </div>
                <span>{{ f.value }}</span>
                @if (f.sourceReference) { <span class="muted small">Source: {{ f.sourceReference }}</span> }
              </div>
              <div class="row side">
                @if (!f.isVerified) { <button pButton outlined [disabled]="busy()" (click)="verify(f)">Mark verified</button> }
                @else { <button pButton text [disabled]="busy()" (click)="invalidate(f)">Mark unverified</button> }
                <button pButton text rounded severity="secondary" data-icon pTooltip="Delete" [disabled]="busy()" (click)="remove(f)"><span class="material-symbols-rounded">delete</span></button>
              </div>
            </div>
          } @empty {
            @if (!loaded()) { <app-loading-skeleton variant="list" [rows]="4" label="Loading facts" /> }
            @else { <div class="empty"><span class="material-symbols-rounded">fact_check</span>{{ facts().length ? 'No facts match this filter.' : 'No facts yet — add your skills and experience to the left.' }}</div> }
          }
        </section>
      </div>
    </div>
  `,
  styles: [`
    .layout { display: grid; gap: 1.5rem; grid-template-columns: minmax(300px, 380px) 1fr; align-items: start; }
    .toolbar { margin-bottom: .75rem; }
    .no-mb { margin-bottom: 0; }
    .fact { display: flex; justify-content: space-between; gap: 1rem; align-items: flex-start; }
    .fact .main { display: grid; gap: .3rem; min-width: 0; }
    .fact .side { flex: none; }
    @media (max-width: 960px) { .layout { grid-template-columns: 1fr; } }
    @media (max-width: 640px) { .fact { flex-direction: column; } }
  `]
})
export class ProfileComponent {
  private readonly api = inject(ApiService);
  private readonly notify = inject(NotifyService);
  readonly types = FACT_TYPES;
  readonly profile = signal<CandidateProfile | null>(null);
  readonly facts = signal<CandidateFact[]>([]);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly loaded = signal(false);
  readonly filter = signal<'ALL' | 'VERIFIED' | 'UNVERIFIED'>('ALL');
  readonly verifiedCount = computed(() => this.facts().filter(f => f.isVerified).length);
  readonly filterOptions = computed(() => [
    { label: 'All', value: 'ALL' },
    { label: `Verified (${this.verifiedCount()})`, value: 'VERIFIED' },
    { label: `Unverified (${this.facts().length - this.verifiedCount()})`, value: 'UNVERIFIED' }
  ]);
  readonly visible = computed(() => { const f = this.filter(); return this.facts().filter(x => f === 'ALL' || (f === 'VERIFIED') === x.isVerified); });
  fact = { factType: 'SKILL', name: '', value: '', sourceReference: '' };

  constructor() { void this.load(); }

  async load(): Promise<void> {
    try {
      const [p, f] = await Promise.all([
        this.api.profile().catch(error => {
          if (error instanceof HttpErrorResponse && error.status === 404) return blankProfile();
          throw error;
        }),
        this.api.facts()
      ]);
      this.profile.set(p);
      this.facts.set(f);
    }
    catch (e) { this.error.set(describeError(e)); }
    finally { this.loaded.set(true); }
  }

  saveProfile() { const p = this.profile()!; return this.run(async () => { await this.api.updateProfile({ name: p.name, currentRole: p.currentRole, currentCompany: p.currentCompany || null }); this.notify.success('Profile saved.'); }); }
  addFact() { return this.run(async () => { await this.api.createFact({ ...this.fact, sourceReference: this.fact.sourceReference || null }); this.fact = { factType: 'SKILL', name: '', value: '', sourceReference: '' }; this.notify.success('Fact added. Mark it verified once you have confirmed the evidence.'); }); }
  verify(f: CandidateFact) { return this.run(() => this.api.verifyFact(f.id)); }
  invalidate(f: CandidateFact) { return this.run(() => this.api.invalidateFact(f.id)); }
  async remove(f: CandidateFact) {
    if (!await this.notify.confirm({ title: 'Delete fact?', message: `"${f.name}" will be removed and no longer used for matching.`, confirmLabel: 'Delete', danger: true })) return;
    await this.run(() => this.api.deleteFact(f.id));
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true); this.error.set('');
    try { await action(); await this.load(); } catch (e) { this.error.set(describeError(e)); } finally { this.busy.set(false); }
  }
}
