using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace ArvindJobHunter.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<CandidateProfileService>();
        services.AddScoped<AuditService>();
        services.AddScoped<CandidateFactService>();
        services.AddScoped<ApprovalService>();
        services.AddScoped<JobService>();
        services.AddScoped<JobDiscoveryService>();
        services.AddSingleton<InterviewPrepService>();
        services.AddScoped<ApplicationService>();
        services.AddScoped<ApplicationPackageService>();
        services.AddScoped<ResumeService>();
        services.AddScoped<ResumeAnalysisService>();
        services.AddScoped<EmailDraftService>();
        services.AddScoped<AgentRunService>();
        services.AddScoped(sp => ActivatorUtilities.CreateInstance<ExecuteApprovedActionCommand>(sp,
            sp.GetRequiredService<IRepository<ArvindJobHunter.Domain.Entities.ApplicationPackage>>(),
            sp.GetRequiredService<IRepository<ArvindJobHunter.Domain.Entities.SubmissionPackage>>()));
        services.AddScoped<ApplicationSubmissionService>();
        services.AddScoped<FollowUpDraftService>();
        services.AddSingleton<ILocalAuthenticationService, LocalAuthenticationService>();
        services.AddSingleton<IExternalActionGuard, ExternalActionGuard>();
        return services;
    }
}
