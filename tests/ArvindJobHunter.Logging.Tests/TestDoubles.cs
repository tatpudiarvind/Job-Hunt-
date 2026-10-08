using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace ArvindJobHunter.Logging.Tests;

/// <summary>Deterministic clock in a fixed +05:30 zone so file names, headers and roll-over are predictable.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private readonly Lock sync = new();
    private DateTimeOffset now = start;

    public override TimeZoneInfo LocalTimeZone { get; } =
        TimeZoneInfo.CreateCustomTimeZone("Test", start.Offset, "Test", "Test");

    public override DateTimeOffset GetUtcNow()
    {
        lock (sync) return now.ToUniversalTime();
    }

    public void Advance(TimeSpan by)
    {
        lock (sync) now = now.Add(by);
    }
}

internal sealed class TestEnvironment(string contentRoot) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "ArvindJobHunter.Logging.Tests";
    public string ContentRootPath { get; set; } = contentRoot;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
