using BoardVerse.API;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BoardVerse.Tests.Integration.Infrastructure;

public sealed class BoardVerseWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
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
