namespace BoardVerse.Tests.Integration.Infrastructure;

/// <summary>Resolved fixture values set during integration test bootstrap.</summary>
public static class IntegrationTestFixtures
{
    public static Guid CatanInventoryId { get; internal set; }
    public static string PosBoxBarcode { get; internal set; } = string.Empty;
}
