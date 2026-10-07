namespace ArvindJobHunter.Domain;

public enum ExecutionMode
{
    DEMO,
    DRY_RUN,
    LIVE
}

public enum JobStatus
{
    DISCOVERED,
    ANALYZED,
    QUALIFIED,
    DISMISSED
}

public enum ApplicationStatus
{
    DISCOVERED,
    MATCHED,
    SHORTLISTED,
    RESUME_PREPARED,
    AWAITING_APPROVAL,
    APPROVED,
    APPLIED,
    RECRUITER_CONTACTED,
    RECRUITER_REPLIED,
    INTERVIEW,
    TECHNICAL_INTERVIEW,
    HR_INTERVIEW,
    OFFER,
    REJECTED,
    WITHDRAWN,
    CLOSED
}

public enum ApprovalStatus
{
    PENDING,
    APPROVED,
    REJECTED,
    EXPIRED,
    INVALIDATED,
    CONSUMED
}

public enum ApprovalActionType
{
    APPLY_RESUME_CHANGES,
    CREATE_EMAIL_DRAFT,
    SEND_EMAIL,
    SUBMIT_APPLICATION,
    SEND_FOLLOW_UP
}

public enum AgentRunStatus
{
    RUNNING,
    COMPLETED,
    FAILED
}

public enum ExecutionResult
{
    SUCCESS,
    FAILED,
    UNKNOWN
}

public enum EmailDraftStatus
{
    DRAFT,
    APPROVED,
    CREATED_IN_GMAIL,
    SENT,
    FAILED
}

public enum ResumeVersionStatus
{
    PROPOSED,
    APPROVED,
    GENERATED,
    REJECTED
}
