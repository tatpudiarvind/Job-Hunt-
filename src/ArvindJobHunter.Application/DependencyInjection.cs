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
        services.AddSingleton<InterviewPrepService>();
        services.AddScoped<ApplicationService>();
        services.AddScoped<ResumeService>();
        services.AddScoped<EmailDraftService>();
        services.AddScoped<AgentRunService>();
        services.AddScoped<ExecuteApprovedActionCommand>();
        services.AddSingleton<ILocalAuthenticationService, LocalAuthenticationService>();
        services.AddSingleton<IExternalActionGuard, ExternalActionGuard>();
        return services;
    }
}
