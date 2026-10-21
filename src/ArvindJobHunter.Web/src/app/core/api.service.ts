import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  AgentRun, ApplicationPackage, ApplicationReview, ApprovalRequest, AuditEvent, CandidateFact, CandidateProfile, Dashboard, EmailDraft, ExecutionReceipt,
  GoogleOAuthSettings, GoogleStatus, InterviewPrepPlan, Job, JobApplication, JobDiscoveryResult, JobPipelineResult, JobPostingDraft, JobSearchPreferences, ResumePreview, ResumeVersion, Settings,
  SkillDemandReport, CountryProfile, SubmissionPackage, SiteSetting
} from './models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api`;

  private get<T>(path: string) { return firstValueFrom(this.http.get<T>(`${this.base}${path}`)); }
  private post<T>(path: string, body: unknown = {}) { return firstValueFrom(this.http.post<T>(`${this.base}${path}`, body)); }
  private put<T>(path: string, body: unknown) { return firstValueFrom(this.http.put<T>(`${this.base}${path}`, body)); }
  private delete(path: string) { return firstValueFrom(this.http.delete<void>(`${this.base}${path}`)); }

  dashboard() { return this.get<Dashboard>('/dashboard'); }
  profile() { return this.get<CandidateProfile>('/candidate-profile'); }
  updateProfile(body: { name: string; currentRole: string; currentCompany: string | null }) { return this.put<CandidateProfile>('/candidate-profile', body); }

  facts() { return this.get<CandidateFact[]>('/candidate-profile/facts'); }
  createFact(body: { factType: string; name: string; value: string; sourceReference: string | null }) { return this.post<CandidateFact>('/candidate-profile/facts', body); }
  verifyFact(id: string) { return this.post<CandidateFact>(`/candidate-profile/facts/${id}/verify`); }
  invalidateFact(id: string) { return this.post<CandidateFact>(`/candidate-profile/facts/${id}/invalidate`); }
  deleteFact(id: string) { return this.delete(`/candidate-profile/facts/${id}`); }

  jobs(all = false) { return this.get<Job[]>(all ? '/jobs?all=true' : '/jobs'); }
  job(id: string) { return this.get<Job>(`/jobs/${id}`); }
  createJob(body: { title: string; company: string; location: string | null; source: string | null; url: string | null; description: string }) { return this.post<Job>('/jobs', body); }
  previewJobImport(url: string) { return this.post<JobPostingDraft>('/jobs/import-preview', { url }); }
  skillDemand() { return this.get<SkillDemandReport>('/jobs/skill-demand'); }
  discoverJobs(maxResults = 20) { return this.post<JobDiscoveryResult>('/jobs/discover', { maxResults }); }
  interviewPrep(jobId: string) { return this.get<InterviewPrepPlan>(`/jobs/${jobId}/interview-prep`); }
  exportData() { return firstValueFrom(this.http.get(`${environment.apiBaseUrl}/api/data/export`, { responseType: 'blob' })); }
  importData(file: File) { const form = new FormData(); form.append('file', file, file.name); return firstValueFrom(this.http.post<{ importedFiles: string[]; skippedEntries: string[]; backupDirectory: string }>(`${environment.apiBaseUrl}/api/data/import`, form)); }
  analyzeJob(id: string) { return this.post<JobPipelineResult>(`/jobs/${id}/analyze`); }
  prepareJob(id: string) { return this.post<JobPipelineResult>(`/jobs/${id}/prepare`); }
  recruiterEmail(id: string, recipient: string | null) { return this.post<EmailDraft>(`/jobs/${id}/recruiter-email`, { recipient }); }
  setRecruiterContact(id: string, status: string, name: string | null, email: string | null, source: string | null) {
    return firstValueFrom(this.http.put<Job>(`${this.base}/jobs/${id}/recruiter-contact`, { status, name, email, source }));
  }
  dismissJob(id: string) { return this.post<Job>(`/jobs/${id}/dismiss`); }
  setJobCountry(id: string, countryCode: string | null) { return firstValueFrom(this.http.put<Job>(`${this.base}/jobs/${id}/country`, { countryCode })); }
  countryProfile(id: string) { return this.get<CountryProfile>(`/jobs/${id}/country-profile`); }
  deleteJob(id: string) { return this.delete(`/jobs/${id}`); }

  applications() { return this.get<JobApplication[]>('/applications'); }
  applicationStatuses() { return this.get<string[]>('/applications/statuses'); }
  applicationReview(id: string) { return this.get<ApplicationReview>(`/applications/${id}/review`); }
  createApplicationPackage(id: string) { return this.post<ApplicationPackage>(`/applications/${id}/packages`); }
  transitionApplication(id: string, status: string, reason: string | null) { return this.post<JobApplication>(`/applications/${id}/transition`, { status, reason }); }
  dueFollowUps() { return this.get<JobApplication[]>('/applications/follow-ups/due'); }
  scheduleFollowUp(id: string, dueAt: string, note: string | null) { return firstValueFrom(this.http.put<JobApplication>(`${environment.apiBaseUrl}/api/applications/${id}/follow-up`, { dueAt, note })); }
  clearFollowUp(id: string) { return firstValueFrom(this.http.delete<JobApplication>(`${environment.apiBaseUrl}/api/applications/${id}/follow-up`)); }
  draftFollowUpEmail(id: string) { return this.post<EmailDraft>(`/applications/${id}/follow-up-email`); }

  openInBrowser(url: string) { return this.post<void>('/browser/open', { url }); }
  browserDownloads() { return this.get<{ path: string; name: string; size: number; modified: string }[]>('/browser/downloads'); }
  importDownload(path: string, label: string | null, keywords: string[]) { return this.post<{ path: string; label: string; keywords: string[] }>('/browser/downloads/import', { path, label, keywords }); }
  resumeLibrary() { return this.get<{ path: string; label: string; keywords: string[] }[]>('/browser/resume-library'); }
  captureJobFromBrowser() { return this.post<JobPostingDraft>('/browser/capture-job'); }

  submissions(jobId?: string) { return this.get<SubmissionPackage[]>(jobId ? `/submissions?jobId=${jobId}` : '/submissions'); }
  prepareSubmission(jobId: string, adapter?: string) { return this.post<SubmissionPackage>(`/submissions/jobs/${jobId}/prepare`, { adapter: adapter ?? null }); }
  answerSubmission(id: string, answers: Record<string, string>) { return this.post<SubmissionPackage>(`/submissions/${id}/answers`, { answers }); }
  submissionScreenshot(id: string) { return firstValueFrom(this.http.get(`${this.base}/submissions/${id}/screenshot`, { responseType: 'blob' })); }
  sites() { return this.get<SiteSetting[]>('/sites'); }
  updateSites(sites: { adapter: string; enabled: boolean; dailyCap: number }[]) { return this.put<SiteSetting[]>('/sites', { sites }); }

  approvals() { return this.get<ApprovalRequest[]>('/approvals'); }
  approve(id: string, note: string | null) { return this.post<ApprovalRequest>(`/approvals/${id}/approve`, { note }); }
  reject(id: string, note: string | null) { return this.post<ApprovalRequest>(`/approvals/${id}/reject`, { note }); }
  execute(id: string, idempotencyKey: string) { return this.post<ExecutionReceipt>(`/approvals/${id}/execute`, { idempotencyKey }); }
  receipts() { return this.get<ExecutionReceipt[]>('/approvals/receipts'); }

  resumes() { return this.get<ResumeVersion[]>('/resumes'); }
  resumePreview(id: string) { return this.get<ResumePreview>(`/resumes/${id}/preview`); }
  requestResumeApproval(id: string) { return this.post<ApprovalRequest>(`/resumes/${id}/request-approval`); }
  setResumeTemplate(id: string, template: string) { return this.put<ResumeVersion>(`/resumes/${id}/template`, { template }); }

  emails() { return this.get<EmailDraft[]>('/emails'); }
  updateEmail(id: string, body: { to: string; subject: string; body: string }) { return this.put<EmailDraft>(`/emails/${id}`, body); }
  /** `send` is a required API flag: true asks to approve sending, false asks to approve creating a Gmail draft. */
  requestEmailApproval(id: string, send = true) { return this.post<ApprovalRequest>(`/emails/${id}/request-approval?send=${send}`); }
  requestCoverLetterDocument(id: string, resumeVersionId?: string) { return this.post<ApprovalRequest>(`/emails/${id}/request-document-approval${resumeVersionId ? `?resumeVersionId=${resumeVersionId}` : ''}`); }
  deleteEmail(id: string) { return this.delete(`/emails/${id}`); }

  agentRuns() { return this.get<AgentRun[]>('/agent-runs'); }
  auditLogs() { return this.get<AuditEvent[]>('/audit-logs'); }

  settings() { return this.get<Settings>('/settings'); }
  updateSettings(body: { mode: string | null; llmProvider: string | null; llmDisplayName: string | null; llmBaseUrl: string | null; llmApiKey: string | null; llmModel: string | null; masterResumePath: string | null }) { return this.put<unknown>('/settings', body); }
  jobSearchPreferences() { return this.get<JobSearchPreferences>('/settings/job-search-preferences'); }
  updateJobSearchPreferences(body: { preferredCountries: string[]; linkedInProfileUrl: string | null; preferredRoles: string[]; workMode: string | null; salaryExpectation: string | null; workAuthorization: string | null }) { return this.put<JobSearchPreferences>('/settings/job-search-preferences', body); }
  googleOAuthSettings() { return this.get<GoogleOAuthSettings>('/settings/google-oauth'); }
  updateGoogleOAuthSettings(body: { clientId: string; clientSecret: string | null; redirectUri: string }) { return this.put<GoogleOAuthSettings>('/settings/google-oauth', body); }
  uploadMasterResume(file: File, country?: string) { const form = new FormData(); form.append('file', file, file.name); const query = country ? `?country=${encodeURIComponent(country)}` : ''; return firstValueFrom(this.http.post<{ masterResumePath: string; fileName: string; size: number }>(`${environment.apiBaseUrl}/api/settings/master-resume${query}`, form)); }
  removeCountryResume(country: string) { return firstValueFrom(this.http.delete<void>(`${environment.apiBaseUrl}/api/settings/country-resumes/${encodeURIComponent(country)}`)); }
  googleStatus() { return this.get<GoogleStatus>('/integrations/google/status'); }
  googleAuthorizeUrl() { return this.get<{ url: string }>('/integrations/google/authorize'); }
  googleRevoke() { return this.post<void>('/integrations/google/revoke'); }
}
