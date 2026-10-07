export type ExecutionMode = 'DEMO' | 'DRY_RUN' | 'LIVE';

export interface AuthStatus { configured: boolean; authenticated: boolean; }
export interface Session { token: string; expiresAt: string; userId: string; }

export interface Dashboard {
  jobs: number; qualifiedJobs: number; applications: number; pendingApprovals: number;
  agentRuns: number; externalActions: number; verifiedFacts: number; mode: ExecutionMode; llmProvider: string;
}

export interface CandidateProfile { id: string; name: string; currentRole: string; currentCompany: string | null; updatedAt: string; version: string; }

export interface CandidateFact {
  id: string; factType: string; name: string; value: string; sourceType: string; sourceReference: string;
  isVerified: boolean; verifiedAt: string | null; createdAt: string;
}

export interface JobAnalysis { requiredSkills: string[]; niceToHaveSkills: string[]; seniorityLevel: string; summary: string; responsibilities: string[]; }
export interface JobMatch { score: number; matchedSkills: string[]; missingSkills: string[]; reason: string; matchedAt: string; }
export interface Job {
  id: string; title: string; company: string; location: string; source: string; url: string | null; description: string;
  status: 'DISCOVERED' | 'ANALYZED' | 'QUALIFIED' | 'DISMISSED'; createdAt: string; updatedAt: string;
  analysis: JobAnalysis | null; match: JobMatch | null;
}

export interface ApplicationTransition { from: string | null; to: string; at: string; reason: string; }
export interface JobApplication {
  id: string; jobId: string; status: string; resumeVersionId: string | null; coverLetterDraftId: string | null;
  notes: string | null; createdAt: string; updatedAt: string; history: ApplicationTransition[];
  followUpDueAt?: string | null; followUpNote?: string | null;
}

export interface ApprovalRequest {
  id: string; actionType: string; targetType: string; targetId: string; payloadHash: string; summary: string; mode: ExecutionMode;
  status: 'PENDING' | 'APPROVED' | 'REJECTED' | 'EXPIRED' | 'INVALIDATED' | 'CONSUMED';
  requestedAt: string; expiresAt: string; decidedAt: string | null; decisionNote: string | null; consumedAt: string | null;
}

export interface ExecutionReceipt {
  id: string; approvalId: string; idempotencyKey: string; actionType: string; mode: ExecutionMode;
  result: 'SUCCESS' | 'FAILED' | 'UNKNOWN'; externalReference: string | null; message: string | null; executedAt: string;
}

export interface ResumeChange { section: string; oldText: string; newText: string; evidence: string[]; }
export interface ResumeVersion {
  id: string; jobId: string; masterPath: string; outputPath: string | null; status: string; changes: ResumeChange[];
  approvalId: string | null; createdAt: string;
}

export interface EmailDraft {
  id: string; jobId: string | null; kind: string; to: string; subject: string; body: string; status: string;
  approvalId: string | null; externalId: string | null; createdAt: string; updatedAt: string;
}

export interface AgentToolCall { toolName: string; inputSummary: string; outputSummary: string; succeeded: boolean; at: string; durationMs: number; }
export interface AgentRun {
  id: string; agentName: string; workflow: string; jobId: string | null; status: 'RUNNING' | 'COMPLETED' | 'FAILED'; mode: ExecutionMode;
  startedAt: string; completedAt: string | null; error: string | null; llmProvider: string; toolCalls: AgentToolCall[];
}

export interface AuditEvent { id: string; action: string; targetType: string; targetId: string; outcome: string; details: string | null; at: string; }

export interface JobPipelineResult { run: AgentRun; job: Job; resume: ResumeVersion | null; coverLetter: EmailDraft | null; resumeApproval: ApprovalRequest | null; }

export interface Settings { mode: ExecutionMode; llmProvider: string; openAiConfigured: boolean; masterResumePath: string; masterResumeExists: boolean; dataDirectory: string; }
export interface GoogleStatus { configured: boolean; connected: boolean; accountEmail?: string | null; }
export interface JobPostingDraft { url: string; title: string | null; company: string | null; location: string | null; description: string; source: string; warnings: string[]; }
export interface InterviewResource { title: string; url: string; provider: string; kind: 'video' | 'website' | 'search'; why: string | null; }
export interface InterviewResourceGroup { topic: string; resources: InterviewResource[]; }
export interface InterviewPrepPlan { jobId: string; jobTitle: string; company: string; focusSkills: string[]; groups: InterviewResourceGroup[]; generatedAt: string; }
