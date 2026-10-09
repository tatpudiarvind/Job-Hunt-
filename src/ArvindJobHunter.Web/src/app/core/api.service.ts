import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  AgentRun, ApprovalRequest, AuditEvent, CandidateFact, CandidateProfile, Dashboard, EmailDraft, ExecutionReceipt,
  GoogleOAuthSettings, GoogleStatus, InterviewPrepPlan, Job, JobApplication, JobPipelineResult, JobPostingDraft, ResumeVersion, Settings
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

  jobs() { return this.get<Job[]>('/jobs'); }
  job(id: string) { return this.get<Job>(`/jobs/${id}`); }
  createJob(body: { title: string; company: string; location: string | null; source: string | null; url: string | null; description: string }) { return this.post<Job>('/jobs', body); }
  previewJobImport(url: string) { return this.post<JobPostingDraft>('/jobs/import-preview', { url }); }
  interviewPrep(jobId: string) { return this.get<InterviewPrepPlan>(`/jobs/${jobId}/interview-prep`); }
  exportData() { return firstValueFrom(this.http.get(`${environment.apiBaseUrl}/api/data/export`, { responseType: 'blob' })); }
  importData(file: File) { const form = new FormData(); form.append('file', file, file.name); return firstValueFrom(this.http.post<{ importedFiles: string[]; skippedEntries: string[]; backupDirectory: string }>(`${environment.apiBaseUrl}/api/data/import`, form)); }
  analyzeJob(id: string) { return this.post<JobPipelineResult>(`/jobs/${id}/analyze`); }
  prepareJob(id: string) { return this.post<JobPipelineResult>(`/jobs/${id}/prepare`); }
  recruiterEmail(id: string, recipient: string) { return this.post<EmailDraft>(`/jobs/${id}/recruiter-email`, { recipient }); }
  dismissJob(id: string) { return this.post<Job>(`/jobs/${id}/dismiss`); }
  deleteJob(id: string) { return this.delete(`/jobs/${id}`); }

  applications() { return this.get<JobApplication[]>('/applications'); }
  applicationStatuses() { return this.get<string[]>('/applications/statuses'); }
  transitionApplication(id: string, status: string, reason: string | null) { return this.post<JobApplication>(`/applications/${id}/transition`, { status, reason }); }
  dueFollowUps() { return this.get<JobApplication[]>('/applications/follow-ups/due'); }
  scheduleFollowUp(id: string, dueAt: string, note: string | null) { return firstValueFrom(this.http.put<JobApplication>(`${environment.apiBaseUrl}/api/applications/${id}/follow-up`, { dueAt, note })); }
  clearFollowUp(id: string) { return firstValueFrom(this.http.delete<JobApplication>(`${environment.apiBaseUrl}/api/applications/${id}/follow-up`)); }

  approvals() { return this.get<ApprovalRequest[]>('/approvals'); }
  approve(id: string, note: string | null) { return this.post<ApprovalRequest>(`/approvals/${id}/approve`, { note }); }
  reject(id: string, note: string | null) { return this.post<ApprovalRequest>(`/approvals/${id}/reject`, { note }); }
  execute(id: string, idempotencyKey: string) { return this.post<ExecutionReceipt>(`/approvals/${id}/execute`, { idempotencyKey }); }
  receipts() { return this.get<ExecutionReceipt[]>('/approvals/receipts'); }

  resumes() { return this.get<ResumeVersion[]>('/resumes'); }
  requestResumeApproval(id: string) { return this.post<ApprovalRequest>(`/resumes/${id}/request-approval`); }

  emails() { return this.get<EmailDraft[]>('/emails'); }
  updateEmail(id: string, body: { to: string; subject: string; body: string }) { return this.put<EmailDraft>(`/emails/${id}`, body); }
  requestEmailApproval(id: string) { return this.post<ApprovalRequest>(`/emails/${id}/request-approval`); }
  deleteEmail(id: string) { return this.delete(`/emails/${id}`); }

  agentRuns() { return this.get<AgentRun[]>('/agent-runs'); }
  auditLogs() { return this.get<AuditEvent[]>('/audit-logs'); }

  settings() { return this.get<Settings>('/settings'); }
  updateSettings(body: { mode: string | null; llmProvider: string | null; llmDisplayName: string | null; llmBaseUrl: string | null; llmApiKey: string | null; llmModel: string | null; masterResumePath: string | null }) { return this.put<unknown>('/settings', body); }
  googleOAuthSettings() { return this.get<GoogleOAuthSettings>('/settings/google-oauth'); }
  updateGoogleOAuthSettings(body: { clientId: string; clientSecret: string; redirectUri: string }) { return this.put<GoogleOAuthSettings>('/settings/google-oauth', body); }
  uploadMasterResume(file: File) { const form = new FormData(); form.append('file', file, file.name); return firstValueFrom(this.http.post<{ masterResumePath: string; fileName: string; size: number }>(`${environment.apiBaseUrl}/api/settings/master-resume`, form)); }
  googleStatus() { return this.get<GoogleStatus>('/integrations/google/status'); }
  googleAuthorizeUrl() { return this.get<{ url: string }>('/integrations/google/authorize'); }
  googleRevoke() { return this.post<void>('/integrations/google/revoke'); }
}
