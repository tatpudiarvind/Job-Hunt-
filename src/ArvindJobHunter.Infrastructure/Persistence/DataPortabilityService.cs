using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Infrastructure.Persistence;

/// <summary>
/// Portable backup of the JSON data directory. Secrets (DataProtection keys, Google tokens, local account hash, the LLM API key)
/// are never exported; imports are validated as JSON, confined to known file names, and preceded by a local backup.
/// </summary>
public sealed class DataPortabilityService(string dataDirectory, ILogger<DataPortabilityService>? logger = null)
{
    private const string SettingsFile = "settings.json";

    /// <summary>Secret fields inside settings.json: blanked on export, kept from the local copy on import.</summary>
    private static readonly string[] SecretSettings = ["llmApiKey"];

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    public static readonly IReadOnlySet<string> ExportableFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "candidate.json", "candidate-facts.json", "jobs.json", "applications.json", "approvals.json",
        "agent-runs.json", "resumes.json", "emails.json", "receipts.json", "audit.json", SettingsFile
    };

    public sealed record ImportResult(IReadOnlyList<string> ImportedFiles, IReadOnlyList<string> SkippedEntries, string BackupDirectory);

    public async Task<byte[]> ExportAsync(CancellationToken cancellationToken)
    {
        var exported = new List<string>();
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in ExportableFiles.OrderBy(n => n))
            {
                var path = Path.Combine(dataDirectory, name);
                if (!File.Exists(path)) continue;
                var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                if (string.Equals(name, SettingsFile, StringComparison.OrdinalIgnoreCase))
                {
                    var redacted = RemoveSecrets(bytes);
                    if (redacted is null)
                    {
                        logger?.LogWarning("{File} could not be parsed, so it was left out of the export rather than risk exporting secrets", name);
                        continue;
                    }

                    bytes = redacted;
                }

                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                await using var target = entry.Open();
                await target.WriteAsync(bytes, cancellationToken);
                exported.Add(name);
            }

            var manifest = zip.CreateEntry("manifest.json");
            await using var manifestStream = manifest.Open();
            await JsonSerializer.SerializeAsync(manifestStream, new { app = "ArvindJobHunter", exportedAt = DateTimeOffset.UtcNow, files = ExportableFiles.OrderBy(n => n) }, cancellationToken: cancellationToken);
        }

        var archive = buffer.ToArray();
        logger?.LogInformation("Data export created with {FileCount} file(s), {Bytes} bytes; secrets were removed", exported.Count, archive.Length);
        return archive;
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
        if (staged.TryGetValue(SettingsFile, out var importedSettings))
        {
            staged[SettingsFile] = KeepLocalSecrets(importedSettings, Path.Combine(dataDirectory, SettingsFile));
        }

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

        var imported = staged.Keys.OrderBy(k => k).ToList();
        logger?.LogWarning("Data import replaced {Files}; previous copies were backed up to {BackupDirectory}{Skipped}",
            string.Join(", ", imported), backupDirectory, skipped.Count == 0 ? "" : $"; skipped entries: {string.Join(", ", skipped)}");
        return new ImportResult(imported, skipped, backupDirectory);
    }

    /// <summary>Returns settings.json with every secret blanked, or null if the document cannot be parsed.</summary>
    private static byte[]? RemoveSecrets(byte[] json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            if (root?["data"] is not JsonObject data) return json;
            foreach (var key in data.Select(p => p.Key).Where(k => SecretSettings.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
            {
                data[key] = "";
            }

            return JsonSerializer.SerializeToUtf8Bytes(root, IndentedJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Exports carry no API key; keep the key already configured on this machine instead of wiping it.</summary>
    private static byte[] KeepLocalSecrets(byte[] imported, string localPath)
    {
        if (!File.Exists(localPath)) return imported;
        try
        {
            var importedRoot = JsonNode.Parse(imported);
            var importedData = importedRoot?["data"] as JsonObject;
            var localData = JsonNode.Parse(File.ReadAllBytes(localPath))?["data"] as JsonObject;
            if (importedData is null || localData is null) return imported;

            var changed = false;
            foreach (var key in SecretSettings)
            {
                if (StringValue(importedData, key) is { Length: > 0 }) continue;
                if (StringValue(localData, key) is not { Length: > 0 } local) continue;
                importedData[key] = local;
                changed = true;
            }

            return changed ? JsonSerializer.SerializeToUtf8Bytes(importedRoot, IndentedJson) : imported;
        }
        catch (JsonException)
        {
            return imported;
        }
    }

    private static string? StringValue(JsonObject data, string key) =>
        data.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Value is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;
}
