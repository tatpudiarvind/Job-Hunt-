import { Component, Input, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TabsModule } from 'primeng/tabs';
import { AccordionModule } from 'primeng/accordion';
import { SelectModule } from 'primeng/select';
import { ApiService } from '../../core/api.service';
import { CountryProfile, EmailDraft, Job, JobApplication, ResumeVersion } from '../../core/models';
import { describeError } from '../../core/auth.interceptor';
import { StatusChipComponent } from '../../shared/status-chip.component';
import { LoadingSkeletonComponent } from '../../shared/loading-skeleton.component';
import { NotifyService } from '../../shared/notify.service';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TooltipModule } from 'primeng/tooltip';
import { ProgressBarModule } from 'primeng/progressbar';

@Component({
  selector: 'app-job-detail',
  standalone: true,
  imports: [ButtonModule, InputTextModule, TooltipModule, ProgressBarModule, FormsModule, RouterLink, DatePipe, TabsModule, AccordionModule, SelectModule, StatusChipComponent, LoadingSkeletonComponent],
  templateUrl: './job-detail.component.html',
  styleUrl: './job-detail.component.scss'
})
export class JobDetailComponent {
  private readonly api = inject(ApiService);
  private readonly notify = inject(NotifyService);
  readonly job = signal<Job | null>(null);
  readonly application = signal<JobApplication | null>(null);
  readonly resumes = signal<ResumeVersion[]>([]);
  readonly emails = signal<EmailDraft[]>([]);
  readonly country = signal<CountryProfile | null>(null);
  readonly countryOptions = [
    { code: 'US', name: 'United States' }, { code: 'CA', name: 'Canada' }, { code: 'GB', name: 'United Kingdom' }, { code: 'IE', name: 'Ireland' },
    { code: 'DE', name: 'Germany' }, { code: 'NL', name: 'Netherlands' }, { code: 'AU', name: 'Australia' }, { code: 'NZ', name: 'New Zealand' },
    { code: 'AE', name: 'United Arab Emirates' }, { code: 'SG', name: 'Singapore' }, { code: 'IN', name: 'India' }
  ];
  readonly countrySelectOptions = [{ code: '', name: 'Auto-detect' }, ...this.countryOptions];
  readonly error = signal('');
  readonly busy = signal(false);
  recipient = '';
  contactSource = '';
  private jobId = '';

  @Input() set id(value: string) { this.jobId = value; void this.load(); }

  async load(): Promise<void> {
    try {
      const [job, apps, resumes, emails] = await Promise.all([this.api.job(this.jobId), this.api.applications(), this.api.resumes(), this.api.emails()]);
      this.job.set(job);
      this.application.set(apps.find(a => a.jobId === this.jobId) ?? null);
      this.resumes.set(resumes.filter(r => r.jobId === this.jobId));
      this.emails.set(emails.filter(e => e.jobId === this.jobId));
      this.country.set(await this.api.countryProfile(this.jobId));
    } catch (e) { this.error.set(describeError(e)); }
  }

  analyze(): Promise<void> { return this.run(async () => { await this.api.analyzeJob(this.jobId); this.notify.success('Analysis and match complete.'); }); }
  prepare(): Promise<void> {
    return this.run(async () => {
      const result = await this.api.prepareJob(this.jobId);
      this.notify.info(result.resumeApproval ? 'Resume changes proposed. Approve them in Approvals before anything is written.' : 'Application prepared.');
    });
  }
  setCountry(code: string): Promise<void> {
    return this.run(async () => { await this.api.setJobCountry(this.jobId, code || null); });
  }
  setTemplate(resume: ResumeVersion, template: string): Promise<void> {
    return this.run(async () => {
      await this.api.setResumeTemplate(resume.id, template);
      this.notify.info('Template changed. Request approval again before generating.');
    });
  }
  previewResume(resume: ResumeVersion): Promise<void> {
    return this.run(async () => {
      this.notify.previewResume(await this.api.resumePreview(resume.id));
    }, false);
  }
  draftRecruiter(): Promise<void> { return this.run(async () => { await this.api.recruiterEmail(this.jobId, this.recipient); this.notify.success('Recruiter email drafted. Nothing has been sent.'); }); }
  saveContact(status: 'USER_PROVIDED' | 'NOT_FOUND'): Promise<void> {
    return this.run(async () => {
      await this.api.setRecruiterContact(this.jobId, status, null, status === 'NOT_FOUND' ? null : this.recipient, status === 'NOT_FOUND' ? null : this.contactSource);
      this.notify.success(status === 'NOT_FOUND' ? 'Marked as no recruiter contact found.' : 'Recruiter contact saved.');
    });
  }

  private async run(action: () => Promise<void>, reload = true): Promise<void> {
    this.busy.set(true); this.error.set('');
    try { await action(); if (reload) await this.load(); } catch (e) { this.error.set(describeError(e)); } finally { this.busy.set(false); }
  }
}
