using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArvindJobHunter.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Infrastructure.Persistence;

/// <summary>Receives a notification when a corrupt JSON file was quarantined so the caller can emit a PERSISTENCE_RECOVERY audit event.</summary>
public delegate Task RecoveryNotifier(string fileName, string quarantinePath, bool restoredFromBackup, CancellationToken cancellationToken);

public sealed class JsonFileStore<T>(string dataDirectory, string fileName, RecoveryNotifier? onRecovery = null, ILogger? logger = null) : IJsonStore<T>
    where T : class, new()
{
    private const int CurrentSchemaVersion = 1;

    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromMilliseconds(25), TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400)];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string path = Path.Combine(dataDirectory, fileName);

    public Task<bool> ExistsAsync(CancellationToken cancellationToken) => Task.FromResult(File.Exists(path));

    public async Task<T> LoadAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(dataDirectory);
            if (!File.Exists(path))
            {
                return new T();
            }

            try
            {
                await using var stream = File.OpenRead(path);
                var envelope = await JsonSerializer.DeserializeAsync<JsonEnvelope<T>>(stream, SerializerOptions, cancellationToken);
                if (envelope is null) return new T();
                if (envelope.SchemaVersion > CurrentSchemaVersion)
                {
                    logger?.LogError("{File} uses schema version {SchemaVersion}, newer than this build supports ({SupportedVersion}); refusing to load it", fileName, envelope.SchemaVersion, CurrentSchemaVersion);
                    throw new InvalidOperationException($"{fileName} uses schema version {envelope.SchemaVersion}, which is newer than this build supports ({CurrentSchemaVersion}).");
                }

                return envelope.Data ?? new T();
            }
            catch (JsonException ex)
            {
                var corruptPath = Path.Combine(dataDirectory, $"{Path.GetFileNameWithoutExtension(fileName)}.corrupt.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json");
                File.Move(path, corruptPath, true);
                var (value, restored) = await LoadBackupAsync(cancellationToken);
                logger?.LogError(ex, "{File} was corrupt and has been quarantined as {QuarantineFile}; {Outcome}", fileName, Path.GetFileName(corruptPath),
                    restored ? "the last good backup was loaded instead" : "no usable backup exists, so it starts empty");
                if (onRecovery is not null) await onRecovery(fileName, corruptPath, restored, cancellationToken);
                return value;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(T value, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var watch = Stopwatch.StartNew();
            Directory.CreateDirectory(dataDirectory);
            var temporaryPath = path + ".tmp";
            var envelope = new JsonEnvelope<T>(CurrentSchemaVersion, DateTimeOffset.UtcNow, value);
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, envelope, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            await using (var validationStream = File.OpenRead(temporaryPath))
            {
                var validation = await JsonSerializer.DeserializeAsync<JsonEnvelope<T>>(validationStream, SerializerOptions, cancellationToken);
                if (validation is not { SchemaVersion: CurrentSchemaVersion, Data: not null })
                {
                    logger?.LogError("Refusing to save {File}: the serialized document did not pass validation", fileName);
                    throw new InvalidOperationException("Generated JSON document did not pass validation.");
                }
            }

            if (File.Exists(path))
            {
                await RetryAsync(() => File.Copy(path, path + ".bak", true), "Backup", cancellationToken);
            }

            await RetryAsync(() => File.Move(temporaryPath, path, true), "Replace", cancellationToken);
            logger?.LogDebug("Saved {File} ({Bytes} bytes) in {ElapsedMs} ms", fileName, new FileInfo(path).Length, watch.ElapsedMilliseconds);
        }
        finally
        {
            gate.Release();
        }
    }

    // Antivirus scanners and search indexers on Windows briefly lock files that were just written.
    // A short retry keeps a save from failing (and the request from returning 500) because of that.
    private async Task RetryAsync(Action operation, string step, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                operation();
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < RetryDelays.Length)
            {
                logger?.LogDebug("{Step} of {File} failed ({Error}); retrying", step, fileName, ex.Message);
                await Task.Delay(RetryDelays[attempt], cancellationToken);
            }
        }
    }

    private async Task<(T Value, bool Restored)> LoadBackupAsync(CancellationToken cancellationToken)
    {
        var backupPath = path + ".bak";
        if (!File.Exists(backupPath))
        {
            return (new T(), false);
        }

        try
        {
            await using var stream = File.OpenRead(backupPath);
            var envelope = await JsonSerializer.DeserializeAsync<JsonEnvelope<T>>(stream, SerializerOptions, cancellationToken);
            return envelope is { Data: not null } ? (envelope.Data, true) : (new T(), false);
        }
        catch (JsonException)
        {
            return (new T(), false);
        }
    }

    private sealed record JsonEnvelope<TValue>(int SchemaVersion, DateTimeOffset LastUpdated, TValue Data);
}