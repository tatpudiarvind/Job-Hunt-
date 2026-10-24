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
  templateUrl: './profile.component.html',
  styleUrl: './profile.component.scss'
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
