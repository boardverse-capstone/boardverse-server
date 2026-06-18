namespace BoardVerse.Tests.Integration.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IntegrationTestCollection : ICollectionFixture<BoardVerseWebApplicationFactory>
{
    public const string Name = "BoardVerse Integration Tests";
}
