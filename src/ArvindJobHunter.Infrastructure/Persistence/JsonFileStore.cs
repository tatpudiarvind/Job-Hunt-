using System.Text.Json;
using System.Text.Json.Serialization;
using ArvindJobHunter.Application.Abstractions;

namespace ArvindJobHunter.Infrastructure.Persistence;

/// <summary>Receives a notification when a corrupt JSON file was quarantined so the caller can emit a PERSISTENCE_RECOVERY audit event.</summary>
public delegate Task RecoveryNotifier(string fileName, string quarantinePath, bool restoredFromBackup, CancellationToken cancellationToken);

public sealed class JsonFileStore<T>(string dataDirectory, string fileName, RecoveryNotifier? onRecovery = null) : IJsonStore<T>
    where T : class, new()
{
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string path = Path.Combine(dataDirectory, fileName);

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
                    throw new InvalidOperationException($"{fileName} uses schema version {envelope.SchemaVersion}, which is newer than this build supports ({CurrentSchemaVersion}).");
                }

                return envelope.Data ?? new T();
            }
            catch (JsonException)
            {
                var corruptPath = Path.Combine(dataDirectory, $"{Path.GetFileNameWithoutExtension(fileName)}.corrupt.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json");
                File.Move(path, corruptPath, true);
                var (value, restored) = await LoadBackupAsync(cancellationToken);
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
                    throw new InvalidOperationException("Generated JSON document did not pass validation.");
                }
            }

            if (File.Exists(path))
            {
                File.Copy(path, path + ".bak", true);
            }

            File.Move(temporaryPath, path, true);
        }
        finally
        {
            gate.Release();
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