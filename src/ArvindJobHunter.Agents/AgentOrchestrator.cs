using ArvindJobHunter.Agents.Tools;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Agents;

public sealed record JobPipelineResult(AgentRun Run, Job Job, ResumeVersion? Resume, EmailDraft? CoverLetter, ApprovalRequest? ResumeApproval);

/// <summary>
/// Owns sequencing and state for agent workflows. Agents/tools only propose structured outputs;
/// the orchestrator persists them and requests approvals. It never executes side effects.
/// </summary>
public sealed class AgentOrchestrator(
    ToolRegistry tools,
    ILlmProviderResolver llmResolver,
    IRuntimeSettingsProvider settings,
    JobService jobs,
    ApplicationService applications,
    CandidateFactService facts,
    CandidateProfileService profiles,
    ResumeService resumes,
    EmailDraftService emails,
    AgentRunService runs,
    AuditService audit)
{
    public async Task<JobPipelineResult> AnalyzeAndMatchAsync(Guid jobId, Guid userId, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(jobId, cancellationToken) ?? throw new KeyNotFoundException("Job not found.");
        var (context, current) = await StartAsync("JobAnalysisAgent", "AnalyzeAndMatch", job.Id, userId, cancellationToken);
        var run = context.Run;
        try
        {
            (job, run) = await AnalyzeAndMatchCoreAsync(job, context with { Run = run }, current, userId, cancellationToken);
            run = run.Complete();
            return new JobPipelineResult(run, job, null, null, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run = run.Fail(ex.Message);
            throw;
        }
        finally
        {
            await runs.SaveAsync(run, cancellationToken);
            await audit.RecordAsync(userId, "AGENT_RUN_" + run.Status, "AgentRun", run.Id.ToString(), run.Status.ToString(), run.Error, cancellationToken);
        }
    }

    public async Task<JobPipelineResult> PrepareApplicationAsync(Guid jobId, Guid userId, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(jobId, cancellationToken) ?? throw new KeyNotFoundException("Job not found.");
        var (context, current) = await StartAsync("ApplicationPreparationAgent", "PrepareApplication", job.Id, userId, cancellationToken);
        var run = context.Run;
        ResumeVersion? resume = null;
        EmailDraft? letter = null;
        ApprovalRequest? approval = null;
        try
        {
            if (job.Analysis is null || job.Match is null)
            {
                (job, run) = await AnalyzeAndMatchCoreAsync(job, context with { Run = run }, current, userId, cancellationToken);
            }

            var verified = await facts.ListVerifiedAsync(cancellationToken);
            var profile = await profiles.GetCurrentAsync(cancellationToken);

            var (resumeDoc, run1) = await ToolInvoker.InvokeAsync(tools.Get<ReadResumeInput, ResumeDocument>("ReadResumeTool"), new ReadResumeInput(current.MasterResumePath), context with { Run = run }, cancellationToken);
            run = run1;
            var (changes, run2) = await ToolInvoker.InvokeAsync(tools.Get<CustomizeResumeInput, IReadOnlyList<ResumeChange>>("CustomizeResumeTool"),
                new CustomizeResumeInput(job, job.Match!, resumeDoc.Value ?? new ResumeDocument([]), verified), context with { Run = run }, cancellationToken);
            run = run2;
            if (changes.Succeeded && changes.Value is { Count: > 0 })
            {
                resume = await resumes.ProposeAsync(job.Id, changes.Value, userId, cancellationToken);
                approval = await resumes.RequestApprovalAsync(resume.Id, userId, cancellationToken);
            }

            var (email, run3) = await ToolInvoker.InvokeAsync(tools.Get<GenerateEmailInput, GeneratedEmail>("GenerateCoverLetterTool"),
                new GenerateEmailInput(job, job.Match!, profile?.Name ?? "Candidate", "COVER_LETTER", null), context with { Run = run }, cancellationToken);
            run = run3;
            if (email.Succeeded && email.Value is not null)
            {
                letter = await emails.CreateAsync(job.Id, "COVER_LETTER", "", email.Value.Subject, email.Value.Body, userId, cancellationToken);
            }

            var application = await applications.EnsureForJobAsync(job.Id, cancellationToken);
            if (resume is not null) application = application.WithResume(resume.Id);
            if (letter is not null) application = application.WithCoverLetter(letter.Id);
            await applications.SaveAsync(application, cancellationToken);
            application = await applications.TryAdvanceAsync(application, ApplicationStatus.SHORTLISTED, "Application preparation started.", userId, cancellationToken);
            application = await applications.TryAdvanceAsync(application, ApplicationStatus.RESUME_PREPARED, "Resume changes proposed.", userId, cancellationToken);
            if (approval is not null)
            {
                await applications.TryAdvanceAsync(application, ApplicationStatus.AWAITING_APPROVAL, "Awaiting approval of resume changes.", userId, cancellationToken);
            }

            run = run.Complete();
            return new JobPipelineResult(run, job, resume, letter, approval);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run = run.Fail(ex.Message);
            throw;
        }
        finally
        {
            await runs.SaveAsync(run, cancellationToken);
            await audit.RecordAsync(userId, "AGENT_RUN_" + run.Status, "AgentRun", run.Id.ToString(), run.Status.ToString(), run.Error, cancellationToken);
        }
    }

    public async Task<EmailDraft> DraftRecruiterEmailAsync(Guid jobId, string recipient, Guid userId, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(jobId, cancellationToken) ?? throw new KeyNotFoundException("Job not found.");
        var (context, current) = await StartAsync("EmailAgent", "DraftRecruiterEmail", job.Id, userId, cancellationToken);
        var run = context.Run;
        try
        {
            if (job.Match is null) (job, run) = await AnalyzeAndMatchCoreAsync(job, context with { Run = run }, current, userId, cancellationToken);
            var profile = await profiles.GetCurrentAsync(cancellationToken);
            var (email, run1) = await ToolInvoker.InvokeAsync(tools.Get<GenerateEmailInput, GeneratedEmail>("GenerateCoverLetterTool"),
                new GenerateEmailInput(job, job.Match!, profile?.Name ?? "Candidate", "RECRUITER", recipient), context with { Run = run }, cancellationToken);
            run = run1;
            if (!email.Succeeded || email.Value is null) throw new InvalidOperationException(email.Error ?? "Email generation failed.");
            var draft = await emails.CreateAsync(job.Id, "RECRUITER", recipient, email.Value.Subject, email.Value.Body, userId, cancellationToken);
            run = run.Complete();
            return draft;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run = run.Fail(ex.Message);
            throw;
        }
        finally
        {
            await runs.SaveAsync(run, cancellationToken);
        }
    }

    private async Task<(ToolContext Context, RuntimeSettings Settings)> StartAsync(string agent, string workflow, Guid jobId, Guid userId, CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync(cancellationToken);
        var llm = await llmResolver.ResolveAsync(cancellationToken);
        var run = AgentRun.Start(agent, workflow, jobId, current.Mode, userId, llm.Name);
        await runs.SaveAsync(run, cancellationToken);
        return (new ToolContext(userId, current.Mode, llm, run), current);
    }

    private async Task<(Job Job, AgentRun Run)> AnalyzeAndMatchCoreAsync(Job job, ToolContext context, RuntimeSettings current, Guid userId, CancellationToken cancellationToken)
    {
        var (analysis, run1) = await ToolInvoker.InvokeAsync(tools.Get<AnalyzeJobInput, JobAnalysis>("AnalyzeJobTool"), new AnalyzeJobInput(job), context, cancellationToken);
        if (!analysis.Succeeded || analysis.Value is null) throw new InvalidOperationException(analysis.Error ?? "Analysis failed.");
        job = job.WithAnalysis(analysis.Value);

        var verified = await facts.ListVerifiedAsync(cancellationToken);
        var (match, run2) = await ToolInvoker.InvokeAsync(tools.Get<MatchJobInput, JobMatch>("MatchJobTool"), new MatchJobInput(job, analysis.Value, verified), context with { Run = run1 }, cancellationToken);
        if (!match.Succeeded || match.Value is null) throw new InvalidOperationException(match.Error ?? "Match failed.");
        job = job.WithMatch(match.Value, current.QualificationThreshold);
        await jobs.SaveAsync(job, cancellationToken);

        var application = await applications.EnsureForJobAsync(job.Id, cancellationToken);
        await applications.TryAdvanceAsync(application, ApplicationStatus.MATCHED, $"Match score {match.Value.Score}.", userId, cancellationToken);
        return (job, run2);
    }
}
