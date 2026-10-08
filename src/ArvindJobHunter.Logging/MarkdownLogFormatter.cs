using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Logging;

/// <summary>Renders log entries as GitHub-flavoured Markdown: one table per run of entries, exceptions in collapsible blocks.</summary>
internal static class MarkdownLogFormatter
{
    public const string TableHeader =
        "| Time | Level | Source | Message | Correlation |\n" +
        "| :--- | :--- | :--- | :--- | :--- |\n";

    public const string Legend = "🔍 TRACE · 🐞 DEBUG · ℹ️ INFO · ⚠️ WARN · ❌ ERROR · 🔥 CRITICAL";

    private const string TimeFormat = "HH:mm:ss.fff";

    public static string LevelLabel(LogLevel level) => level switch
    {
        LogLevel.Trace => "🔍 TRACE",
        LogLevel.Debug => "🐞 DEBUG",
        LogLevel.Information => "ℹ️ INFO",
        LogLevel.Warning => "⚠️ WARN",
        LogLevel.Error => "❌ **ERROR**",
        LogLevel.Critical => "🔥 **CRITICAL**",
        _ => level.ToString().ToUpperInvariant()
    };

    /// <summary>Keeps the last two segments of a category, e.g. <c>Features.ExecuteApprovedActionCommand</c> or <c>Hosting.Lifetime</c>.</summary>
    public static string ShortCategory(string category)
    {
        var lastDot = category.LastIndexOf('.');
        if (lastDot <= 0) return category;
        var previousDot = category.LastIndexOf('.', lastDot - 1);
        return previousDot < 0 ? category : category[(previousDot + 1)..];
    }

    public static string Row(in MarkdownLogEntry entry, string message, bool hasDetails)
    {
        var builder = new StringBuilder(160 + message.Length);
        builder.Append("| ").Append(entry.Timestamp.ToString(TimeFormat, CultureInfo.InvariantCulture))
            .Append(" | ").Append(LevelLabel(entry.Level))
            .Append(" | ").Append(EscapeCell(ShortCategory(entry.Category)))
            .Append(" | ").Append(EscapeCell(message));
        if (hasDetails) builder.Append(" ⤵");
        builder.Append(" | ");
        if (!string.IsNullOrEmpty(entry.CorrelationId))
        {
            builder.Append('`').Append(entry.CorrelationId.Replace("`", "", StringComparison.Ordinal).Replace("|", "", StringComparison.Ordinal)).Append('`');
        }

        return builder.Append(" |\n").ToString();
    }

    public static string ExceptionDetails(in MarkdownLogEntry entry, string exception)
    {
        var text = exception.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
        var firstLine = text.Split('\n', 2)[0].Trim();
        if (firstLine.Length > 200) firstLine = firstLine[..200] + "…";
        var icon = entry.Level >= LogLevel.Critical ? "🔥" : entry.Level >= LogLevel.Error ? "❌" : "⚠️";
        var fence = Fence(text);
        return new StringBuilder(text.Length + 256)
            .Append("\n<details>\n<summary>").Append(icon).Append(' ')
            .Append(entry.Timestamp.ToString(TimeFormat, CultureInfo.InvariantCulture))
            .Append(" · <code>").Append(WebUtility.HtmlEncode(firstLine)).Append("</code></summary>\n\n")
            .Append(fence).Append("text\n").Append(text).Append('\n').Append(fence)
            .Append("\n\n</details>\n\n")
            .ToString();
    }

    public static string FileHeader(string title, DateOnly date, int part, bool utc, TimeSpan offset)
    {
        var day = date.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture);
        var zone = utc ? "UTC" : $"local time (UTC{(offset < TimeSpan.Zero ? "-" : "+")}{offset.Duration():hh\\:mm})";
        return new StringBuilder()
            .Append("# 📒 ").Append(title).Append(" — log for ").Append(day).Append(part > 1 ? $" (part {part})" : "").Append("\n\n")
            .Append("> Written by the Job Hunter Markdown logger. Times are **").Append(zone).Append("**; every application start appends a new session.\n")
            .Append("> Exceptions are attached under their entry in collapsible blocks (⤵). Tokens, API keys, passwords and OAuth codes are masked as `***`.\n")
            .Append(">\n> **Levels:** ").Append(Legend).Append("\n\n")
            .ToString();
    }

    public static string SessionHeader(MarkdownSessionInfo session, DateTimeOffset at, string? continuedFrom)
    {
        var builder = new StringBuilder("\n---\n\n");
        var time = at.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        if (continuedFrom is not null)
        {
            return builder.Append("## ⏩ Session continued · ").Append(time).Append(" · PID ").Append(session.ProcessId).Append("\n\n")
                .Append("_Continued from `").Append(continuedFrom).Append("`; the session started ")
                .Append(session.StartedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append("._\n\n")
                .ToString();
        }

        return builder.Append("## ▶️ Session started · ").Append(time).Append(" · PID ").Append(session.ProcessId).Append("\n\n")
            .Append("| Property | Value |\n| :--- | :--- |\n")
            .Append("| Application | ").Append(EscapeCell(session.ApplicationName)).Append(" `").Append(EscapeCode(session.Version)).Append("` |\n")
            .Append("| Environment | ").Append(EscapeCell(session.Environment)).Append(" |\n")
            .Append("| Machine | ").Append(EscapeCell(session.Machine)).Append(" |\n")
            .Append("| Runtime | ").Append(EscapeCell(session.Runtime)).Append(" · ").Append(EscapeCell(session.OperatingSystem)).Append(" |\n")
            .Append("| Content root | `").Append(EscapeCode(session.ContentRoot)).Append("` |\n")
            .Append("| Log folder | `").Append(EscapeCode(session.LogDirectory)).Append("` |\n\n")
            .ToString();
    }

    public static string SessionFooter(DateTimeOffset at, TimeSpan uptime, long entries, long warnings, long errors) =>
        string.Create(CultureInfo.InvariantCulture,
            $"\n### ⏹️ Session ended · {at:HH:mm:ss} · uptime {(int)uptime.TotalHours:00}:{uptime.Minutes:00}:{uptime.Seconds:00} · entries: {entries:N0} · warnings: {warnings:N0} · errors: {errors:N0}\n\n");

    public static string ContinuationNote(string nextFile) => $"\n_The session continues in `{EscapeCode(nextFile)}`._\n";

    public static string RecreatedNote(MarkdownSessionInfo session, DateTimeOffset at) =>
        string.Create(CultureInfo.InvariantCulture,
            $"\n---\n\n## ⏩ Session continued · {at:HH:mm:ss} · PID {session.ProcessId}\n\n_This file was deleted while the application was running, so it was started again; earlier entries of this session are gone._\n\n");

    /// <summary>Makes arbitrary text safe for a single Markdown table cell (no column breaks, no raw HTML, one physical line).</summary>
    public static string EscapeCell(string value)
    {
        var builder = new StringBuilder(value.Length + 16);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '|': builder.Append("\\|"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                case '\n': builder.Append("<br>"); break;
                case '\r': break;
                case '\t': builder.Append(' '); break;
                default:
                    if (!char.IsControl(ch)) builder.Append(ch);
                    break;
            }
        }

        return builder.ToString().Trim();
    }

    private static string EscapeCode(string value) => value.Replace("`", "'", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal);

    private static string Fence(string content)
    {
        int longest = 0, run = 0;
        foreach (var ch in content)
        {
            run = ch == '`' ? run + 1 : 0;
            if (run > longest) longest = run;
        }

        return new string('`', Math.Max(3, longest + 1));
    }
}
