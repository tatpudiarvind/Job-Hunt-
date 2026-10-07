using ArvindJobHunter.Infrastructure.Persistence;

namespace ArvindJobHunter.IntegrationTests;

public sealed class JsonFileStoreRecoveryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"ArvindJobHunterTests-{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadAsync_QuarantinesCorruptFileAndRestoresLatestBackup()
    {
        var store = new JsonFileStore<TestDocument>(directory, "candidate.json");
        await store.SaveAsync(new TestDocument { Value = "first" }, CancellationToken.None);
        await store.SaveAsync(new TestDocument { Value = "backup-value" }, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(directory, "candidate.json"), "not valid json");

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.Equal("first", result.Value);
        Assert.Single(Directory.GetFiles(directory, "candidate.corrupt.*.json"));
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
