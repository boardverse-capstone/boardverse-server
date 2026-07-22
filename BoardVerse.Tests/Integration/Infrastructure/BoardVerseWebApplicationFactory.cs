using BoardVerse.API;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Tests.Integration.Infrastructure;

public sealed class BoardVerseWebApplicationFactory : WebApplicationFactory<Program>
{
    public static readonly string LogFilePath = Path.Combine(
        Path.GetTempPath(), "boardverse-test-log.txt");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (File.Exists(LogFilePath)) File.Delete(LogFilePath);
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging =>
        {
            logging.AddProvider(new TestLogProvider(LogFilePath));
            logging.SetMinimumLevel(LogLevel.Information);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        ApplyTestEnvironmentVariables();
        var host = base.CreateHost(builder);
        IntegrationTestDataBootstrapper.EnsureAllFixturesAsync(host.Services).GetAwaiter().GetResult();
        return host;
    }

    private static void ApplyTestEnvironmentVariables()
    {
        var connectionString = TestConfiguration.ConnectionString
            ?? throw new InvalidOperationException(
                "Missing test DB connection. Copy appsettings.local.json.example to appsettings.local.json.");

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", connectionString);
        Environment.SetEnvironmentVariable("JwtSettings__SecurityKey", "IntegrationTestSigningKey_Min32Chars!");
        Environment.SetEnvironmentVariable("JwtSettings__ValidIssuer", "BoardVerseIntegrationTests");
        Environment.SetEnvironmentVariable("JwtSettings__ValidAudience", "BoardVerseIntegrationTests");
        Environment.SetEnvironmentVariable("REDIS_URL", "");
        Environment.SetEnvironmentVariable("Redis__ConnectionString", "");
    }
}

internal sealed class TestLogProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly object _lock = new();
    public TestLogProvider(string path) => _path = path;
    public ILogger CreateLogger(string categoryName) => new TestLogger(categoryName, _path, _lock);
    public void Dispose() { }
}

internal sealed class TestLogger : ILogger
{
    private readonly string _category;
    private readonly string _path;
    private readonly object _lock;
    public TestLogger(string category, string path, object @lock)
    {
        _category = category; _path = path; _lock = @lock;
    }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var msg = formatter(state, exception);
        lock (_lock)
        {
            File.AppendAllText(_path, $"[{logLevel}] {_category}: {msg}\n");
            if (exception != null)
            {
                File.AppendAllText(_path, $"  EX: {exception.GetType().Name}: {exception.Message}\n");
                File.AppendAllText(_path, exception.StackTrace + "\n");
                if (exception.InnerException != null)
                {
                    File.AppendAllText(_path, $"  INNER: {exception.InnerException.GetType().Name}: {exception.InnerException.Message}\n");
                    File.AppendAllText(_path, exception.InnerException.StackTrace + "\n");
                }
            }
        }
    }
}
