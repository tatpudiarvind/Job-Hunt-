import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { ApiService } from '../../core/api.service';
import { CandidateFact, CandidateProfile } from '../../core/models';
import { describeError } from '../../core/auth.interceptor';
import { PageHeaderComponent } from '../../shared/page-header.component';
import { NotifyService } from '../../shared/notify.service';

const FACT_TYPES = ['SKILL', 'EXPERIENCE', 'EDUCATION', 'CERTIFICATION', 'LANGUAGE', 'ACHIEVEMENT'];

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [FormsModule, MatIconModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatProgressBarModule, MatTooltipModule, MatButtonToggleModule, PageHeaderComponent],
  template: `
    <div class="page">
      <app-page-header eyebrow="Truth layer" title="Profile & verified facts" icon="person" subtitle="Only verified facts are used for matching and resume tailoring. The agent cannot invent experience you have not recorded here." />

      @if (busy()) { <mat-progress-bar mode="indeterminate" /> }
      @if (error()) { <div class="alert error"><mat-icon class="material-symbols-rounded">error</mat-icon><span>{{ error() }}</span></div> }

      <div class="layout">
        <div class="stack">
          @if (profile(); as p) {
            <section class="card">
              <div class="card-title"><mat-icon class="material-symbols-rounded">badge</mat-icon>Profile</div>
              <form class="stack" (ngSubmit)="saveProfile()">
                <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>Name</mat-label><input matInput name="name" [(ngModel)]="p.name" required /></mat-form-field>
                <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>Current role</mat-label><input matInput name="role" [(ngModel)]="p.currentRole" required /></mat-form-field>
                <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>Current company</mat-label><input matInput name="company" [(ngModel)]="p.currentCompany" /></mat-form-field>
                <div class="row end"><button mat-flat-button color="primary" type="submit" [disabled]="busy()">Save profile</button></div>
              </form>
            </section>
          }
          <section class="card">
            <div class="card-title"><mat-icon class="material-symbols-rounded">add_circle</mat-icon>Add a fact</div>
            <form class="stack" (ngSubmit)="addFact()">
              <mat-form-field appearance="outline" subscriptSizing="dynamic">
                <mat-label>Type</mat-label>
                <mat-select name="type" [(ngModel)]="fact.factType">@for (t of types; track t) { <mat-option [value]="t">{{ t }}</mat-option> }</mat-select>
              </mat-form-field>
              <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>Name</mat-label><input matInput name="fname" [(ngModel)]="fact.name" required placeholder="e.g. Kubernetes" /></mat-form-field>
              <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>Value</mat-label><input matInput name="fvalue" [(ngModel)]="fact.value" required placeholder="e.g. 3 years, production clusters at Acme" /></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Evidence / source</mat-label><input matInput name="fsrc" [(ngModel)]="fact.sourceReference" placeholder="e.g. Resume 2025 §Experience" /><mat-hint>Where this can be verified</mat-hint></mat-form-field>
              <div class="row end"><button mat-stroked-button type="submit" [disabled]="busy() || !fact.name || !fact.value">Add as unverified</button></div>
            </form>
          </section>
        </div>

        <section class="card">
          <div class="row between toolbar">
            <div class="card-title no-mb"><mat-icon class="material-symbols-rounded">verified</mat-icon>Facts <span class="muted">({{ facts().length }})</span></div>
            <mat-button-toggle-group [value]="filter()" (change)="filter.set($event.value)" hideSingleSelectionIndicator>
              <mat-button-toggle value="ALL">All</mat-button-toggle>
              <mat-button-toggle value="VERIFIED">Verified ({{ verifiedCount() }})</mat-button-toggle>
              <mat-button-toggle value="UNVERIFIED">Unverified ({{ facts().length - verifiedCount() }})</mat-button-toggle>
            </mat-button-toggle-group>
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
                @if (!f.isVerified) { <button mat-stroked-button [disabled]="busy()" (click)="verify(f)">Mark verified</button> }
                @else { <button mat-button [disabled]="busy()" (click)="invalidate(f)">Mark unverified</button> }
                <button mat-icon-button matTooltip="Delete" [disabled]="busy()" (click)="remove(f)"><mat-icon class="material-symbols-rounded">delete</mat-icon></button>
              </div>
            </div>
          } @empty { <div class="empty"><mat-icon class="material-symbols-rounded">fact_check</mat-icon>{{ facts().length ? 'No facts match this filter.' : 'No facts yet — add your skills and experience to the left.' }}</div> }
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
  readonly filter = signal<'ALL' | 'VERIFIED' | 'UNVERIFIED'>('ALL');
  readonly verifiedCount = computed(() => this.facts().filter(f => f.isVerified).length);
  readonly visible = computed(() => { const f = this.filter(); return this.facts().filter(x => f === 'ALL' || (f === 'VERIFIED') === x.isVerified); });
  fact = { factType: 'SKILL', name: '', value: '', sourceReference: '' };

  constructor() { void this.load(); }

  async load(): Promise<void> {
    try { const [p, f] = await Promise.all([this.api.profile(), this.api.facts()]); this.profile.set(p); this.facts.set(f); }
    catch (e) { this.error.set(describeError(e)); }
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
