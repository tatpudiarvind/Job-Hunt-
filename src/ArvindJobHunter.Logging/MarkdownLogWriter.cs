using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Logging;

/// <summary>
/// Single background consumer that renders queued entries as Markdown and appends them to the current file.
/// Logging callers never block on disk I/O. Each batch opens the file, appends and closes it again, so the log
/// can be read, copied or deleted at any time (a deleted file is recreated with its headers). Files roll at
/// midnight and when they exceed the size limit, and only the newest <see cref="MarkdownFileLoggerOptions.RetainedFileCountLimit"/>
/// files are kept.
/// </summary>
internal sealed class MarkdownLogWriter : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
    private const int MaxPendingCharacters = 1_000_000;

    private readonly MarkdownFileLoggerOptions options;
    private readonly MarkdownSessionInfo session;
    private readonly Func<DateTimeOffset> clock;
    private readonly Channel<MarkdownLogEntry> queue;
    private readonly Regex ownFiles;
    private readonly long maxFileBytes;
    private readonly StringBuilder pending = new();
    private readonly Task pump;

    private string? currentPath;
    private DateOnly currentDate;
    private int currentPart;
    private long currentSize;
    private bool tableOpen;
    private bool pendingHasFileHeader;
    private bool pendingStartsInTable;
    private long retryAtTicks;
    private long dropped;
    private long entries;
    private long warnings;
    private long errors;
    private int disposed;

    public MarkdownLogWriter(MarkdownFileLoggerOptions options, MarkdownSessionInfo session, Func<DateTimeOffset> clock)
    {
        this.options = options;
        this.session = session;
        this.clock = clock;
        maxFileBytes = options.MaxFileSizeBytes > 0 ? options.MaxFileSizeBytes : long.MaxValue;
        ownFiles = new Regex($"^{Regex.Escape(options.FileNamePrefix)}-(\\d{{4}}-\\d{{2}}-\\d{{2}})(?:-(\\d{{3}}))?\\.md$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        queue = Channel.CreateBounded<MarkdownLogEntry>(
            new BoundedChannelOptions(Math.Max(16, options.QueueCapacity)) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true },
            _ => Interlocked.Increment(ref dropped));
        pump = Task.Run(PumpAsync);
    }

    public string? CurrentFilePath => Volatile.Read(ref currentPath);

    public void Enqueue(in MarkdownLogEntry entry)
    {
        if (Volatile.Read(ref disposed) == 0) queue.Writer.TryWrite(entry);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1) return;
        queue.Writer.TryComplete();
        try { pump.Wait(ShutdownTimeout); }
        catch (AggregateException) { /* already reported by the pump */ }
    }

    private async Task PumpAsync()
    {
        try
        {
            EnsureFile(session.StartedAt);
            FlushPending(force: false);
            Task<bool>? waitForEntries = null;
            while (true)
            {
                waitForEntries ??= queue.Reader.WaitToReadAsync().AsTask();
                if (pending.Length > 0)
                {
                    // A write failed earlier: retry when the delay is over even if no new entry arrives.
                    var delay = TimeSpan.FromMilliseconds(Math.Max(0, retryAtTicks - Environment.TickCount64) + 50);
                    if (await Task.WhenAny(waitForEntries, Task.Delay(delay)).ConfigureAwait(false) != waitForEntries)
                    {
                        FlushPending(force: false);
                        continue;
                    }
                }

                var more = await waitForEntries.ConfigureAwait(false);
                waitForEntries = null;
                if (!more) break;
                while (queue.Reader.TryRead(out var entry)) Write(entry);
                WriteDroppedNotice();
                FlushPending(force: false);
            }
        }
        catch (Exception ex)
        {
            ReportInternalError("The Markdown log writer stopped unexpectedly.", ex);
        }
        finally
        {
            try
            {
                if (currentPath is not null)
                {
                    var now = clock();
                    Append(MarkdownLogFormatter.SessionFooter(now, now - session.StartedAt, entries, warnings, errors));
                    FlushPending(force: true);
                }
            }
            catch (Exception ex)
            {
                ReportInternalError("Could not write the session footer.", ex);
            }
        }
    }

    private void Write(in MarkdownLogEntry entry)
    {
        EnsureFile(entry.Timestamp);

        var message = Prepare(entry.Message, options.MaxMessageLength);
        var exception = entry.Exception is null ? null : Prepare(entry.Exception, options.MaxMessageLength * 4);
        if (!tableOpen)
        {
            Append(MarkdownLogFormatter.TableHeader);
            tableOpen = true;
        }

        Append(MarkdownLogFormatter.Row(entry, message, exception is not null));
        if (exception is not null)
        {
            Append(MarkdownLogFormatter.ExceptionDetails(entry, exception));
            tableOpen = false;
        }

        entries++;
        if (entry.Level == LogLevel.Warning) warnings++;
        else if (entry.Level is LogLevel.Error or LogLevel.Critical) errors++;
    }

    private string Prepare(string text, int limit)
    {
        if (options.RedactSecrets) text = LogRedactor.Redact(text);
        if (limit > 0 && text.Length > limit)
        {
            text = string.Concat(text.AsSpan(0, limit), $"… [truncated {text.Length - limit:N0} characters]");
        }

        return text;
    }

    private void WriteDroppedNotice()
    {
        var count = Interlocked.Exchange(ref dropped, 0);
        if (count == 0) return;
        Write(new MarkdownLogEntry(clock(), LogLevel.Warning, typeof(MarkdownLogWriter).FullName!,
            $"{count:N0} log entr{(count == 1 ? "y was" : "ies were")} dropped because the log queue was full.", null, null));
    }

    /// <summary>Selects the file for the entry's date, rolling over at midnight or when the current file is full.</summary>
    private void EnsureFile(DateTimeOffset timestamp)
    {
        var date = DateOnly.FromDateTime(timestamp.DateTime);
        if (currentPath is not null && date == currentDate && currentSize < maxFileBytes) return;

        var (path, part) = NextPath(date, currentPath is not null && date == currentDate ? currentPart + 1 : 1);
        var previous = currentPath;
        if (previous is not null)
        {
            Append(MarkdownLogFormatter.ContinuationNote(Path.GetFileName(path)));
            FlushPending(force: true);
            if (pending.Length > 0)
            {
                // Never carry the old file's tail into the new file.
                pending.Clear();
                pendingHasFileHeader = false;
            }
        }

        Volatile.Write(ref currentPath, path);
        currentDate = date;
        currentPart = part;
        currentSize = ExistingLength(path);
        tableOpen = false;
        if (currentSize == 0)
        {
            Append(MarkdownLogFormatter.FileHeader(options.Title, date, part, options.UseUtcTimestamps, timestamp.Offset));
            pendingHasFileHeader = true;
        }

        Append(MarkdownLogFormatter.SessionHeader(session, timestamp, previous is null ? null : Path.GetFileName(previous)));
        ApplyRetention();
    }

    private (string Path, int Part) NextPath(DateOnly date, int part)
    {
        while (true)
        {
            var path = Path.Combine(options.Directory, FileName(date, part));
            if (ExistingLength(path) < maxFileBytes) return (path, part);
            part++;
        }
    }

    private static long ExistingLength(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch (IOException) { return 0; }
    }

    private string FileName(DateOnly date, int part)
    {
        var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return part <= 1
            ? $"{options.FileNamePrefix}-{day}.md"
            : string.Create(CultureInfo.InvariantCulture, $"{options.FileNamePrefix}-{day}-{part:000}.md");
    }

    private void Append(string text)
    {
        if (pending.Length == 0) pendingStartsInTable = tableOpen;
        pending.Append(text);
        currentSize += Utf8.GetByteCount(text);
    }

    /// <summary>Opens the current file, appends the pending batch and closes it again.</summary>
    private void FlushPending(bool force)
    {
        if (pending.Length == 0 || currentPath is null) return;
        if (!force && Environment.TickCount64 < retryAtTicks)
        {
            DropPendingIfTooLarge();
            return;
        }

        try
        {
            Directory.CreateDirectory(options.Directory);
            using var stream = new FileStream(currentPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length == 0 && !pendingHasFileHeader)
            {
                // The file was deleted (or emptied) while the application was running: start it again with headers.
                var restart = MarkdownLogFormatter.FileHeader(options.Title, currentDate, currentPart, options.UseUtcTimestamps, clock().Offset)
                    + MarkdownLogFormatter.RecreatedNote(session, clock())
                    + (pendingStartsInTable ? MarkdownLogFormatter.TableHeader : "");
                stream.Write(Utf8.GetBytes(restart));
            }

            stream.Write(Utf8.GetBytes(pending.ToString()));
            currentSize = stream.Length;
            pending.Clear();
            pendingHasFileHeader = false;
            retryAtTicks = 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            retryAtTicks = Environment.TickCount64 + (long)RetryDelay.TotalMilliseconds;
            ReportInternalError($"Could not write to '{currentPath}'; retrying in {RetryDelay.TotalSeconds:0} s.", ex);
            DropPendingIfTooLarge();
        }
    }

    private void DropPendingIfTooLarge()
    {
        if (pending.Length <= MaxPendingCharacters) return;
        pending.Clear();
        pendingHasFileHeader = false;
        ReportInternalError("Log entries were discarded because the log file stayed unavailable.", new IOException(currentPath));
    }

    private void ApplyRetention()
    {
        if (options.RetainedFileCountLimit <= 0 || !Directory.Exists(options.Directory)) return;
        try
        {
            // The current file always counts as one of the retained files, even before its first write.
            var stale = new DirectoryInfo(options.Directory)
                .EnumerateFiles($"{options.FileNamePrefix}-*.md")
                .Where(file => !string.Equals(file.FullName, currentPath, StringComparison.OrdinalIgnoreCase))
                .Select(file => (File: file, Match: ownFiles.Match(file.Name)))
                .Where(x => x.Match.Success)
                .OrderByDescending(x => x.Match.Groups[1].Value, StringComparer.Ordinal)
                .ThenByDescending(x => x.Match.Groups[2].Success ? int.Parse(x.Match.Groups[2].Value, CultureInfo.InvariantCulture) : 1)
                .Skip(options.RetainedFileCountLimit - 1)
                .Select(x => x.File)
                .ToList();

            foreach (var file in stale)
            {
                try { file.Delete(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* in use or protected; try again next roll */ }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            ReportInternalError("Old Markdown log files could not be cleaned up.", ex);
        }
    }

    private static void ReportInternalError(string message, Exception exception)
    {
        try { Console.Error.WriteLine($"[MarkdownFileLogger] {message} {exception.GetType().Name}: {exception.Message}"); }
        catch (IOException) { /* console unavailable */ }
    }
}
