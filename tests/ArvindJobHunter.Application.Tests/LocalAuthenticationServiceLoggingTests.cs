using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Application.Features;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Application.Tests;

public sealed class LocalAuthenticationServiceLoggingTests
{
    private const string Password = "correct-horse-battery-staple";

    private sealed class InMemoryJsonStore<T> : IJsonStore<T> where T : class, new()
    {
        private T? value;
        public Task<T> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(value ?? new T());
        public Task SaveAsync(T item, CancellationToken cancellationToken) { value = item; return Task.CompletedTask; }
        public Task<bool> ExistsAsync(CancellationToken cancellationToken) => Task.FromResult(value is not null);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private static async Task<(LocalAuthenticationService Service, CapturingLogger<LocalAuthenticationService> Log)> CreateAsync()
    {
        var log = new CapturingLogger<LocalAuthenticationService>();
        var service = new LocalAuthenticationService(new InMemoryJsonStore<LocalAccountRecord>(), log);
        Assert.True(await service.SignupAsync("arvind", Password, CancellationToken.None));
        return (service, log);
    }

    [Fact]
    public async Task AFailedLogin_NeverLogsWhatWasTypedIntoTheUsernameBox()
    {
        var (service, log) = await CreateAsync();

        Assert.Null(await service.LoginAsync("Tr0ub4dor&3-horse", Password, CancellationToken.None));

        Assert.DoesNotContain(log.Messages, m => m.Contains("Tr0ub4dor", StringComparison.Ordinal));
        Assert.Contains("Login failed: unknown username", log.Messages);
    }

    [Fact]
    public async Task AWrongPassword_IsLoggedWithTheAccountName_ButNeverThePassword()
    {
        var (service, log) = await CreateAsync();

        Assert.Null(await service.LoginAsync("arvind", "not-the-password", CancellationToken.None));
        Assert.NotNull(await service.LoginAsync("ARVIND", Password, CancellationToken.None));

        Assert.Contains("Login failed for arvind: wrong password", log.Messages);
        Assert.Contains(log.Messages, m => m.StartsWith("Login succeeded for arvind", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Messages, m => m.Contains("not-the-password", StringComparison.Ordinal) || m.Contains(Password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARejectedPasswordReset_DoesNotLogTheTypedUsername()
    {
        var (service, log) = await CreateAsync();

        Assert.False(await service.ResetPasswordAsync("s3cret-typed-here", "another-long-password", CancellationToken.None));

        Assert.DoesNotContain(log.Messages, m => m.Contains("s3cret-typed-here", StringComparison.Ordinal));
    }
}
