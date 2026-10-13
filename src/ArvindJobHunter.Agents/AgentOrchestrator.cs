using System.Diagnostics;
using ArvindJobHunter.Agents.Tools;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Agents;

public sealed record JobPipelineResult(AgentRun Run, Job Job, ResumeVersion? Resume, EmailDraft? CoverLetter, ApprovalRequest? ResumeApproval);

/// <summary>An agent workflow failed after its run was recorded. The run, including the failed tool call, is persisted as FAILED.</summary>
public sealed class AgentRunFailedException(Guid runId, string message, Exception innerException) : InvalidOperationException(message, innerException)
{
    public Guid RunId { get; } = runId;
}

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
    AuditService audit,
    ILogger<AgentOrchestrator> logger)
{
    public async Task<JobPipelineResult> AnalyzeAndMatchAsync(Guid jobId, Guid userId, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(jobId, cancellationToken) ?? throw new KeyNotFoundException("Job not found.");
        var session = await StartAsync("JobAnalysisAgent", "AnalyzeAndMatch", job, userId, cancellationToken);
        try
        {
            job = await AnalyzeAndMatchCoreAsync(job, session, userId, cancellationToken);
            session.Run = session.Run.Complete();
            return new JobPipelineResult(session.Run, job, null, null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            session.Run = session.Run.Fail("Cancelled: the request was aborted before the run completed.");
            throw;
        }
        catch (Exception ex)
        {
            session.Run = session.Run.Fail(ex.Message);
            throw new AgentRunFailedException(session.Run.Id, ex.Message, ex);
        }
        finally
        {
            await FinishAsync(session, userId);
        }
    }

    public async Task<JobPipelineResult> PrepareApplicationAsync(Guid jobId, Guid userId, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(jobId, cancellationToken) ?? throw new KeyNotFoundException("Job not found.");
        var session = await StartAsync("ApplicationPreparationAgent", "PrepareApplication", job, userId, cancellationToken);
        ResumeVersion? resume = null;
        EmailDraft? letter = null;
        ApprovalRequest? approval = null;
        try
        {
            if (job.Analysis is null || job.Match is null)
            {
                job = await AnalyzeAndMatchCoreAsync(job, session, userId, cancellationToken);
            }

            var verified = await facts.ListVerifiedAsync(cancellationToken);
            var profile = await profiles.GetCurrentAsync(cancellationToken);

            var resumeDoc = await InvokeAsync<ReadResumeInput, ResumeDocument>(session, "ReadResumeTool", new ReadResumeInput(session.Settings.MasterResumePath), cancellationToken);
            var changes = await InvokeAsync<CustomizeResumeInput, IReadOnlyList<ResumeChange>>(session, "CustomizeResumeTool",
                new CustomizeResumeInput(job, job.Match!, resumeDoc.Value ?? new ResumeDocument([]), verified), cancellationToken);
            if (changes.Succeeded && changes.Value is { Count: > 0 })
            {
                resume = await resumes.ProposeAsync(job.Id, changes.Value, userId, cancellationToken);
                approval = await resumes.RequestApprovalAsync(resume.Id, userId, cancellationToken);
            }
            else
            {
                logger.LogInformation("Agent run {RunId}: no resume changes proposed for job {JobId} ({Reason})", session.Run.Id, job.Id, changes.Error ?? changes.Summary);
            }

            var email = await InvokeAsync<GenerateEmailInput, GeneratedEmail>(session, "GenerateCoverLetterTool",
                new GenerateEmailInput(job, job.Match!, profile?.Name ?? "Candidate", "COVER_LETTER", null), cancellationToken);
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

            session.Run = session.Run.Complete();
            return new JobPipelineResult(session.Run, job, resume, letter, approval);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            session.Run = session.Run.Fail("Cancelled: the request was aborted before the run completed.");
            throw;
        }
        catch (Exception ex)
        {
            session.Run = session.Run.Fail(ex.Message);
            throw new AgentRunFailedException(session.Run.Id, ex.Message, ex);
        }
        finally
        {
            await FinishAsync(session, userId);
        }
    }

    public async Task<EmailDraft> DraftRecruiterEmailAsync(Guid jobId, string recipient, Guid userId, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(jobId, cancellationToken) ?? throw new KeyNotFoundException("Job not found.");
        var session = await StartAsync("EmailAgent", "DraftRecruiterEmail", job, userId, cancellationToken);
        try
        {
            if (job.Match is null) job = await AnalyzeAndMatchCoreAsync(job, session, userId, cancellationToken);
            var profile = await profiles.GetCurrentAsync(cancellationToken);
            var email = await InvokeAsync<GenerateEmailInput, GeneratedEmail>(session, "GenerateCoverLetterTool",
                new GenerateEmailInput(job, job.Match!, profile?.Name ?? "Candidate", "RECRUITER", recipient), cancellationToken);
            if (!email.Succeeded || email.Value is null) throw new InvalidOperationException($"Email generation failed: {email.Error ?? "no output"}");
            var draft = await emails.CreateAsync(job.Id, "RECRUITER", recipient, email.Value.Subject, email.Value.Body, userId, cancellationToken);
            session.Run = session.Run.Complete();
            return draft;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            session.Run = session.Run.Fail("Cancelled: the request was aborted before the run completed.");
            throw;
        }
        catch (Exception ex)
        {
            session.Run = session.Run.Fail(ex.Message);
            throw new AgentRunFailedException(session.Run.Id, ex.Message, ex);
        }
        finally
        {
            await FinishAsync(session, userId);
        }
    }

    private async Task<AgentSession> StartAsync(string agent, string workflow, Job job, Guid userId, CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync(cancellationToken);
        var llm = await llmResolver.ResolveAsync(cancellationToken);
        var run = AgentRun.Start(agent, workflow, job.Id, current.Mode, userId, llm.Name);
        await runs.SaveAsync(run, cancellationToken);
        logger.LogInformation("Agent run {RunId} started: {Agent}/{Workflow} for job {JobId} \"{JobTitle}\" at {Company} (LLM {LlmProvider}, {Mode} mode)",
            run.Id, agent, workflow, job.Id, job.Title, job.Company, llm.Name, current.Mode);
        return new AgentSession(new ToolContext(userId, current.Mode, llm, run), current);
    }

    /// <summary>Always persists and audits the final run state, even when the request was cancelled.</summary>
    private async Task FinishAsync(AgentSession session, Guid userId)
    {
        if (session.Run.Status == AgentRunStatus.RUNNING) session.Run = session.Run.Fail("The run ended without completing.");
        var run = session.Run;
        logger.Log(run.Status == AgentRunStatus.COMPLETED ? LogLevel.Information : LogLevel.Warning,
            "Agent run {RunId} {Status} after {ElapsedMs} ms with {ToolCalls} tool call(s){Error}",
            run.Id, run.Status, session.Elapsed.ElapsedMilliseconds, run.ToolCalls.Count, run.Error is null ? "" : $": {run.Error}");
        await runs.SaveAsync(run, CancellationToken.None);
        await audit.RecordAsync(userId, "AGENT_RUN_" + run.Status, "AgentRun", run.Id.ToString(), run.Status.ToString(), run.Error, CancellationToken.None);
    }

    private async Task<ToolResult<TOutput>> InvokeAsync<TInput, TOutput>(AgentSession session, string toolName, TInput input, CancellationToken cancellationToken)
    {
        var (result, run) = await ToolInvoker.InvokeAsync(tools.Get<TInput, TOutput>(toolName), input, session.Context, cancellationToken, logger);
        session.Run = run;
        return result;
    }

    private async Task<Job> AnalyzeAndMatchCoreAsync(Job job, AgentSession session, Guid userId, CancellationToken cancellationToken)
    {
        var analysis = await InvokeAsync<AnalyzeJobInput, JobAnalysis>(session, "AnalyzeJobTool", new AnalyzeJobInput(job), cancellationToken);
        if (!analysis.Succeeded || analysis.Value is null) throw new InvalidOperationException($"Job analysis failed: {analysis.Error ?? "no output"}");
        job = job.WithAnalysis(analysis.Value);

        var verified = await facts.ListVerifiedAsync(cancellationToken);
        var match = await InvokeAsync<MatchJobInput, JobMatch>(session, "MatchJobTool", new MatchJobInput(job, analysis.Value, verified), cancellationToken);
        if (!match.Succeeded || match.Value is null) throw new InvalidOperationException($"Job matching failed: {match.Error ?? "no output"}");
        job = job.WithMatch(match.Value, session.Settings.QualificationThreshold);
        await jobs.SaveAsync(job, cancellationToken);
        logger.LogInformation("Job {JobId} scored {Score}/100 against threshold {Threshold} → {Status} (matched: {Matched}; missing: {Missing})",
            job.Id, match.Value.Score, session.Settings.QualificationThreshold, job.Status,
            string.Join(", ", match.Value.MatchedSkills), match.Value.MissingSkills.Count == 0 ? "none" : string.Join(", ", match.Value.MissingSkills));

        var application = await applications.EnsureForJobAsync(job.Id, cancellationToken);
        await applications.TryAdvanceAsync(application, ApplicationStatus.MATCHED, $"Match score {match.Value.Score}.", userId, cancellationToken);
        return job;
    }

    /// <summary>Mutable holder so every tool call is kept on the run, even when a later step throws.</summary>
    private sealed class AgentSession(ToolContext context, RuntimeSettings settings)
    {
        public AgentRun Run { get; set; } = context.Run;
        public RuntimeSettings Settings { get; } = settings;
        public ToolContext Context => context with { Run = Run };
        public Stopwatch Elapsed { get; } = Stopwatch.StartNew();
    }
}
