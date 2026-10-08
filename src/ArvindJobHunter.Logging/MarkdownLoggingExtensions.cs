using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;

namespace ArvindJobHunter.Logging;

public static class MarkdownLoggingExtensions
{
    /// <summary>
    /// Adds the Markdown file logger. <paramref name="configure"/> supplies host-specific defaults;
    /// values in the <c>Logging:MarkdownFile</c> configuration section override them.
    /// </summary>
    public static ILoggingBuilder AddMarkdownFile(this ILoggingBuilder builder, Action<MarkdownFileLoggerOptions>? configure = null)
    {
        builder.AddConfiguration();
        if (configure is not null) builder.Services.Configure(configure);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, MarkdownFileLoggerProvider>());
        LoggerProviderOptions.RegisterProviderOptions<MarkdownFileLoggerOptions, MarkdownFileLoggerProvider>(builder.Services);
        return builder;
    }

    /// <summary>Logs one line per request and adds the <c>X-Correlation-Id</c> response header. Register before the exception handler.</summary>
    public static IApplicationBuilder UseMarkdownRequestLogging(this IApplicationBuilder app) => app.UseMiddleware<RequestLoggingMiddleware>();

    /// <summary>The Markdown logger registered in the container, if any.</summary>
    public static MarkdownFileLoggerProvider? GetMarkdownLogger(this IServiceProvider services) =>
        services.GetServices<ILoggerProvider>().OfType<MarkdownFileLoggerProvider>().FirstOrDefault();

    /// <summary>
    /// Records unhandled and unobserved exceptions. When the process is about to terminate the Markdown
    /// log is flushed first, so the crash is on disk.
    /// </summary>
    public static IHost LogUnhandledExceptions(this IHost host, string category)
    {
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(category);
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        void OnUnhandled(object? sender, UnhandledExceptionEventArgs e)
        {
            logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception (process terminating: {IsTerminating})", e.IsTerminating);
            if (e.IsTerminating) host.Services.GetMarkdownLogger()?.Dispose();
        }

        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e) =>
            logger.LogError(e.Exception, "Unobserved task exception");

        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        lifetime.ApplicationStopped.Register(() =>
        {
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        });
        return host;
    }
}
