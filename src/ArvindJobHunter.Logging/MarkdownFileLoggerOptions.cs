namespace ArvindJobHunter.Logging;

/// <summary>Options for the Markdown file logger, bound from the <c>Logging:MarkdownFile</c> configuration section.</summary>
public sealed class MarkdownFileLoggerOptions
{
    /// <summary>Turns the Markdown sink on or off without removing the provider.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Folder for the log files. Relative paths are resolved against the host content root.</summary>
    public string Directory { get; set; } = "logs";

    /// <summary>Files are named <c>{FileNamePrefix}-{yyyy-MM-dd}.md</c>, plus a <c>-002</c> style suffix when a day exceeds <see cref="MaxFileSizeBytes"/>.</summary>
    public string FileNamePrefix { get; set; } = "jobhunter";

    /// <summary>Title written at the top of every log file.</summary>
    public string Title { get; set; } = "Job Hunter";

    /// <summary>A new part file is started once the current file reaches this size. Zero or less disables size-based rolling.</summary>
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Only the newest files are kept; older ones are deleted. Zero or less keeps every file.</summary>
    public int RetainedFileCountLimit { get; set; } = 31;

    /// <summary>Write timestamps (and roll files at midnight) in UTC instead of local time.</summary>
    public bool UseUtcTimestamps { get; set; }

    /// <summary>Entries buffered for the background writer; when full, new entries are dropped and counted.</summary>
    public int QueueCapacity { get; set; } = 10_000;

    /// <summary>Messages longer than this are truncated so a single entry cannot flood the file.</summary>
    public int MaxMessageLength { get; set; } = 8_000;

    /// <summary>Masks bearer tokens, API keys, passwords and OAuth codes before anything is written.</summary>
    public bool RedactSecrets { get; set; } = true;

    internal MarkdownFileLoggerOptions WithDirectory(string directory) => new()
    {
        Enabled = Enabled,
        Directory = directory,
        FileNamePrefix = string.IsNullOrWhiteSpace(FileNamePrefix) ? "jobhunter" : FileNamePrefix.Trim(),
        Title = string.IsNullOrWhiteSpace(Title) ? "Job Hunter" : Title.Trim(),
        MaxFileSizeBytes = MaxFileSizeBytes,
        RetainedFileCountLimit = RetainedFileCountLimit,
        UseUtcTimestamps = UseUtcTimestamps,
        QueueCapacity = QueueCapacity,
        MaxMessageLength = MaxMessageLength,
        RedactSecrets = RedactSecrets
    };
}
