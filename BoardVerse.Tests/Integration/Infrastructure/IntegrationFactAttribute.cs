namespace BoardVerse.Tests.Integration.Infrastructure;

public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (!IntegrationTestEnvironment.IsDatabaseAvailable)
        {
            Skip = "Set ConnectionStrings:DefaultConnection in appsettings.local.json, or DATABASE_URL / NEON_CONNECTION.";
        }
    }
}
