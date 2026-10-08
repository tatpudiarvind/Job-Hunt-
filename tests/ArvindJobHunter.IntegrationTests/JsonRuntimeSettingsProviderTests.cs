using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Infrastructure.Persistence;

namespace ArvindJobHunter.IntegrationTests;

public sealed class JsonRuntimeSettingsProviderTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"ajh-settings-{Guid.NewGuid():N}");

    private static readonly RuntimeSettings Configured = new()
    {
        Mode = ExecutionMode.DRY_RUN,
        LlmProvider = "OpenAI",
        LlmDisplayName = "Configured Gateway",
        LlmBaseUrl = "http://localhost:11434/v1/",
        LlmApiKey = "configured-key",
        LlmModel = "llama3.1:8b",
        MasterResumePath = @"C:\resumes\master.docx",
        QualificationThreshold = 75,
        ApprovalTtlHours = 12
    };

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    [Fact]
    public async Task WithoutASavedFile_TheConfiguredRuntimeValuesApply()
    {
        var provider = new JsonRuntimeSettingsProvider(new JsonFileStore<RuntimeSettings>(directory, "settings.json"), Configured);

        var settings = await provider.GetAsync(CancellationToken.None);

        Assert.Equal(Configured, settings);
    }

    [Fact]
    public async Task ASavedFile_WinsForEditableFields_WhileThresholdAndTtlFollowConfiguration()
    {
        var store = new JsonFileStore<RuntimeSettings>(directory, "settings.json");
        await store.SaveAsync(new RuntimeSettings { Mode = ExecutionMode.LIVE, LlmProvider = "Demo", LlmDisplayName = "Demo", MasterResumePath = "", QualificationThreshold = 60, ApprovalTtlHours = 24 }, CancellationToken.None);
        var provider = new JsonRuntimeSettingsProvider(store, Configured);

        var settings = await provider.GetAsync(CancellationToken.None);

        Assert.Equal(ExecutionMode.LIVE, settings.Mode);
        Assert.Equal("Demo", settings.LlmProvider);
        Assert.Equal("", settings.MasterResumePath);
        Assert.Equal(75, settings.QualificationThreshold);
        Assert.Equal(12, settings.ApprovalTtlHours);
        Assert.Equal("configured-key", settings.LlmApiKey);
    }

    [Fact]
    public async Task ExistsAsync_BecomesTrueAfterTheFirstSave()
    {
        var store = new JsonFileStore<RuntimeSettings>(directory, "settings.json");
        Assert.False(await store.ExistsAsync(CancellationToken.None));
        await store.SaveAsync(new RuntimeSettings(), CancellationToken.None);
        Assert.True(await store.ExistsAsync(CancellationToken.None));
    }

    [Fact]
    public void RuntimeSettings_ToString_NeverRevealsTheApiKey()
    {
        var text = Configured.ToString();

        Assert.DoesNotContain("configured-key", text);
        Assert.Contains("LlmApiKey = ***", text);
        Assert.Contains("Mode = DRY_RUN", text);
        Assert.Contains("LlmApiKey = <none>", (Configured with { LlmApiKey = "" }).ToString());
    }

    [Fact]
    public void Describe_SummarisesWithoutTheKey()
    {
        var summary = JsonRuntimeSettingsProvider.Describe(Configured);

        Assert.Contains("mode DRY_RUN", summary);
        Assert.Contains("API key set", summary);
        Assert.DoesNotContain("configured-key", summary);
    }
}
