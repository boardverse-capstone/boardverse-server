namespace BoardVerse.Tests.Integration.Infrastructure;

public static class IntegrationTestEnvironment
{
    public static bool IsDatabaseAvailable =>
        !string.IsNullOrWhiteSpace(TestConfiguration.ConnectionString);
}
