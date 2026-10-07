using ArvindJobHunter.Infrastructure.Persistence;

namespace ArvindJobHunter.IntegrationTests;

public sealed class JsonFileStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"ArvindJobHunterTests-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAsync_PersistsVersionedDocumentThatCanBeReloaded()
    {
        var firstStore = new JsonFileStore<TestDocument>(directory, "candidate.json");
        await firstStore.SaveAsync(new TestDocument { Value = "persisted" }, CancellationToken.None);

        var secondStore = new JsonFileStore<TestDocument>(directory, "candidate.json");
        var reloaded = await secondStore.LoadAsync(CancellationToken.None);

        Assert.Equal("persisted", reloaded.Value);
        var json = await File.ReadAllTextAsync(Path.Combine(directory, "candidate.json"));
        Assert.Contains("schemaVersion", json, StringComparison.Ordinal);
        Assert.Contains("lastUpdated", json, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    public sealed class TestDocument
    {
        public string Value { get; init; } = string.Empty;
    }
}
