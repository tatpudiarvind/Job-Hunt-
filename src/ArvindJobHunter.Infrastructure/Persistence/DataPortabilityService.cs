using System.IO.Compression;
using System.Text.Json;

namespace ArvindJobHunter.Infrastructure.Persistence;

/// <summary>
/// Portable backup of the JSON data directory. Secrets (DataProtection keys, Google tokens, local account hash)
/// are never exported; imports are validated as JSON, confined to known file names, and preceded by a local backup.
/// </summary>
public sealed class DataPortabilityService(string dataDirectory)
{
    public static readonly IReadOnlySet<string> ExportableFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "candidate.json", "candidate-facts.json", "jobs.json", "applications.json", "approvals.json",
        "agent-runs.json", "resumes.json", "emails.json", "receipts.json", "audit.json", "settings.json"
    };

    public sealed record ImportResult(IReadOnlyList<string> ImportedFiles, IReadOnlyList<string> SkippedEntries, string BackupDirectory);

    public async Task<byte[]> ExportAsync(CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in ExportableFiles.OrderBy(n => n))
            {
                var path = Path.Combine(dataDirectory, name);
                if (!File.Exists(path)) continue;
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                await using var target = entry.Open();
                await using var source = File.OpenRead(path);
                await source.CopyToAsync(target, cancellationToken);
            }

            var manifest = zip.CreateEntry("manifest.json");
            await using var manifestStream = manifest.Open();
            await JsonSerializer.SerializeAsync(manifestStream, new { app = "ArvindJobHunter", exportedAt = DateTimeOffset.UtcNow, files = ExportableFiles.OrderBy(n => n) }, cancellationToken: cancellationToken);
        }

        return buffer.ToArray();
    }

    public async Task<ImportResult> ImportAsync(Stream zipStream, CancellationToken cancellationToken)
    {
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var staged = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var skipped = new List<string>();

        foreach (var entry in zip.Entries)
        {
            var name = Path.GetFileName(entry.FullName);
            if (!ExportableFiles.Contains(name) || entry.FullName != name)
            {
                skipped.Add(entry.FullName);
                continue;
            }

            if (entry.Length > 50 * 1024 * 1024) throw new InvalidOperationException($"{name} is too large to import.");

            await using var source = entry.Open();
            using var ms = new MemoryStream();
            await source.CopyToAsync(ms, cancellationToken);
            var bytes = ms.ToArray();

            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("schemaVersion", out _) || !doc.RootElement.TryGetProperty("data", out _))
            {
                throw new InvalidOperationException($"{name} is not a valid ArvindJobHunter data file.");
            }

            staged[name] = bytes;
        }

        if (staged.Count == 0) throw new InvalidOperationException("The archive contains no importable data files.");

        Directory.CreateDirectory(dataDirectory);
        var backupDirectory = Path.Combine(dataDirectory, "backups", $"pre-import-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
        Directory.CreateDirectory(backupDirectory);
        foreach (var name in staged.Keys)
        {
            var existing = Path.Combine(dataDirectory, name);
            if (File.Exists(existing)) File.Copy(existing, Path.Combine(backupDirectory, name), true);
        }

        foreach (var (name, bytes) in staged)
        {
            var target = Path.Combine(dataDirectory, name);
            var tmp = target + ".import.tmp";
            await File.WriteAllBytesAsync(tmp, bytes, cancellationToken);
            File.Move(tmp, target, true);
        }

        return new ImportResult(staged.Keys.OrderBy(k => k).ToList(), skipped, backupDirectory);
    }
}
