using System.IO.Compression;
using System.Text;
using ArvindJobHunter.Infrastructure.Persistence;
using Xunit;

namespace ArvindJobHunter.IntegrationTests;

public sealed class DataPortabilityServiceTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), $"ajh-port-{Guid.NewGuid():N}");

    public DataPortabilityServiceTests() => Directory.CreateDirectory(dir);
    public void Dispose() { try { Directory.Delete(dir, true); } catch { /* best effort */ } }

    private static string Envelope(string data) => $$"""{"schemaVersion":1,"lastUpdated":"2026-01-01T00:00:00+00:00","data":{{data}}}""";

    [Fact]
    public async Task Export_IncludesDataFiles_ExcludesSecrets()
    {
        await File.WriteAllTextAsync(Path.Combine(dir, "jobs.json"), Envelope("[]"));
        await File.WriteAllTextAsync(Path.Combine(dir, "account.json"), Envelope("{}"));
        await File.WriteAllTextAsync(Path.Combine(dir, "google-token.json"), Envelope("{}"));
        Directory.CreateDirectory(Path.Combine(dir, "keys"));
        await File.WriteAllTextAsync(Path.Combine(dir, "keys", "key.xml"), "<key/>");

        var bytes = await new DataPortabilityService(dir).ExportAsync(CancellationToken.None);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains("jobs.json", names);
        Assert.Contains("manifest.json", names);
        Assert.DoesNotContain("account.json", names);
        Assert.DoesNotContain("google-token.json", names);
        Assert.DoesNotContain(names, n => n.Contains("key"));
    }

    [Fact]
    public async Task Import_ReplacesKnownFiles_BacksUp_SkipsUnknownAndTraversal()
    {
        await File.WriteAllTextAsync(Path.Combine(dir, "jobs.json"), Envelope("[1]"));
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            Write(zip, "jobs.json", Envelope("[2]"));
            Write(zip, "account.json", Envelope("{}"));
            Write(zip, "../evil.json", Envelope("{}"));
            Write(zip, "sub/jobs.json", Envelope("[3]"));
        }
        ms.Position = 0;

        var result = await new DataPortabilityService(dir).ImportAsync(ms, CancellationToken.None);

        Assert.Equal(["jobs.json"], result.ImportedFiles);
        Assert.Equal(3, result.SkippedEntries.Count);
        Assert.Contains("[2]", await File.ReadAllTextAsync(Path.Combine(dir, "jobs.json")));
        Assert.Contains("[1]", await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory, "jobs.json")));
        Assert.False(File.Exists(Path.Combine(dir, "account.json")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(dir)!, "evil.json")));
    }

    [Fact]
    public async Task Import_RejectsNonEnvelopeJson()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true)) Write(zip, "jobs.json", "[]");
        ms.Position = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DataPortabilityService(dir).ImportAsync(ms, CancellationToken.None));
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var s = zip.CreateEntry(name).Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
