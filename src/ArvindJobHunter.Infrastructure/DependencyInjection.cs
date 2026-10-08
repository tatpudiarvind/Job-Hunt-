using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;
using ArvindJobHunter.Infrastructure.Google;
using ArvindJobHunter.Infrastructure.Jobs;
using ArvindJobHunter.Infrastructure.Persistence;
using ArvindJobHunter.Infrastructure.Resume;
using ArvindJobHunter.ResumeAutomation.Word;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Infrastructure;

public static class DependencyInjection
{
    private const string StoreLogCategory = "ArvindJobHunter.Infrastructure.Persistence.JsonFileStore";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string dataDirectory,
        RuntimeSettings defaultSettings,
        string appSettingsPath,
        GoogleOAuthConfiguration googleOptions,
        WorkerOptions? workerOptions = null)
    {
        services.AddSingleton<IJsonStore<List<AuditEvent>>>(sp => new JsonFileStore<List<AuditEvent>>(dataDirectory, "audit.json", logger: StoreLogger(sp)));
        services.AddSingleton<IAuditStore, JsonAuditStore>();

        AddStore<CandidateProfileDocument>(services, dataDirectory, "candidate.json");
        AddStore<LocalAccountRecord>(services, dataDirectory, "account.json");
        AddStore<GoogleTokenDocument>(services, dataDirectory, "google-token.json");
        AddStore<RuntimeSettings>(services, dataDirectory, "settings.json");
        AddListRepository<CandidateFact>(services, dataDirectory, "candidate-facts.json", f => f.Id);
        AddListRepository<Job>(services, dataDirectory, "jobs.json", j => j.Id);
        AddListRepository<JobApplication>(services, dataDirectory, "applications.json", a => a.Id);
        AddListRepository<ApprovalRequest>(services, dataDirectory, "approvals.json", a => a.Id);
        AddListRepository<AgentRun>(services, dataDirectory, "agent-runs.json", r => r.Id);
        AddListRepository<ResumeVersion>(services, dataDirectory, "resumes.json", r => r.Id);
        AddListRepository<EmailDraft>(services, dataDirectory, "emails.json", e => e.Id);
        AddListRepository<ExecutionReceipt>(services, dataDirectory, "receipts.json", r => r.Id);

        services.AddSingleton<ICandidateProfileRepository, CandidateProfileRepository>();
        services.AddSingleton<ICacheInvalidatable>(sp => (ICacheInvalidatable)sp.GetRequiredService<ICandidateProfileRepository>());
        services.AddSingleton<ICacheInvalidatable>(sp => (ICacheInvalidatable)sp.GetRequiredService<IAuditStore>());
        services.AddSingleton<ICacheInvalidatable>(sp => (ICacheInvalidatable)sp.GetRequiredService<IRuntimeSettingsProvider>());
        services.AddSingleton(sp => new DataPortabilityService(dataDirectory, sp.GetRequiredService<ILogger<DataPortabilityService>>()));
        services.AddSingleton<IRuntimeSettingsProvider>(sp => new JsonRuntimeSettingsProvider(
            sp.GetRequiredService<IJsonStore<RuntimeSettings>>(), defaultSettings, sp.GetRequiredService<ILogger<JsonRuntimeSettingsProvider>>()));
        workerOptions ??= new WorkerOptions();
        // Redirects are followed by the fetcher itself so that every hop is checked against private/loopback targets,
        // and the connect callback re-checks the address actually dialled (DNS rebinding).
        services.AddHttpClient<IJobPostingFetcher, HttpJobPostingFetcher>(http => http.Timeout = TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                ConnectCallback = HttpJobPostingFetcher.ConnectToPublicAddressAsync
            });
        services.AddSingleton(workerOptions);
        services.AddSingleton<OpenXmlResumeDocumentService>();
        if (workerOptions.Enabled)
        {
            services.AddHttpClient("ResumeWorker", http =>
            {
                http.BaseAddress = new Uri(workerOptions.BaseUrl);
                http.DefaultRequestHeaders.Add("X-Worker-Secret", workerOptions.SharedSecret);
                http.Timeout = TimeSpan.FromSeconds(30);
            });
            services.AddSingleton<IResumeDocumentService>(sp => new WorkerResumeDocumentService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("ResumeWorker"),
                workerOptions,
                sp.GetRequiredService<OpenXmlResumeDocumentService>(),
                sp.GetRequiredService<ILogger<WorkerResumeDocumentService>>()));
        }
        else
        {
            services.AddSingleton<IResumeDocumentService>(sp => sp.GetRequiredService<OpenXmlResumeDocumentService>());
        }

        services.AddSingleton<IGoogleOAuthSettingsProvider>(sp => new JsonGoogleOAuthSettingsProvider(appSettingsPath, googleOptions, sp.GetRequiredService<ILogger<JsonGoogleOAuthSettingsProvider>>()));
        services.AddHttpClient(GoogleOAuthService.HttpClientName);
        // Singleton: the OAuth state issued by /authorize must survive until Google calls /callback.
        services.AddSingleton<IGoogleOAuthService>(sp => new GoogleOAuthService(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IDataProtectionProvider>().CreateProtector("ArvindJobHunter.GoogleOAuth.v1"),
            sp.GetRequiredService<IJsonStore<GoogleTokenDocument>>(),
            sp.GetRequiredService<IGoogleOAuthSettingsProvider>(),
            sp.GetRequiredService<ILogger<GoogleOAuthService>>()));
        services.AddHttpClient<IGmailClient, GmailClient>();
        return services;
    }

    private static ILogger StoreLogger(IServiceProvider sp) => sp.GetRequiredService<ILoggerFactory>().CreateLogger(StoreLogCategory);

    private static void AddStore<T>(IServiceCollection services, string dataDirectory, string fileName)
        where T : class, new() =>
        services.AddSingleton<IJsonStore<T>>(sp => new JsonFileStore<T>(dataDirectory, fileName, Notifier(sp), StoreLogger(sp)));

    private static void AddListRepository<T>(IServiceCollection services, string dataDirectory, string fileName, Func<T, Guid> idSelector)
        where T : class
    {
        services.AddSingleton<IJsonStore<List<T>>>(sp => new JsonFileStore<List<T>>(dataDirectory, fileName, Notifier(sp), StoreLogger(sp)));
        services.AddSingleton<IRepository<T>>(sp => new JsonListRepository<T>(sp.GetRequiredService<IJsonStore<List<T>>>(), idSelector));
        services.AddSingleton<ICacheInvalidatable>(sp => (ICacheInvalidatable)sp.GetRequiredService<IRepository<T>>());
    }

    private static RecoveryNotifier Notifier(IServiceProvider sp) =>
        (fileName, quarantinePath, restored, ct) => sp.GetRequiredService<IAuditStore>().AddAsync(
            AuditEvent.Create(LocalUser.Id, "PERSISTENCE_RECOVERY", "JsonFile", fileName, restored ? "RESTORED_FROM_BACKUP" : "RESET_TO_EMPTY", $"Corrupt file quarantined at {quarantinePath}"), ct);

    private sealed class CandidateProfileRepository(IJsonStore<CandidateProfileDocument> store) : ICandidateProfileRepository, ICacheInvalidatable
    {
        private readonly SemaphoreSlim gate = new(1, 1);
        private CandidateProfile? cachedProfile;

        public void Invalidate() { gate.Wait(); try { cachedProfile = null; } finally { gate.Release(); } }

        public async Task<CandidateProfile?> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            await EnsureLoadedAsync(cancellationToken);
            return cachedProfile?.Id == id ? cachedProfile : null;
        }

        public async Task SaveAsync(CandidateProfile profile, CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                cachedProfile = profile;
                await store.SaveAsync(CandidateProfileDocument.From(profile), cancellationToken);
            }
            finally { gate.Release(); }
        }

        private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
        {
            if (cachedProfile is not null) return;
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (cachedProfile is null)
                {
                    var document = await store.LoadAsync(cancellationToken);
                    cachedProfile = document.Id is null ? null : document.ToDomain();
                }
            }
            finally { gate.Release(); }
        }
    }
}
