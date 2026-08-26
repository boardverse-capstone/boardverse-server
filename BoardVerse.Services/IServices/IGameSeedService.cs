namespace BoardVerse.Services.IServices
{
    public interface IGameSeedService
    {
        Task SeedGamesFromCatalogAsync(List<string>? slugs = null, CancellationToken cancellationToken = default);
        Task SeedSingleGameAsync(string slug, CancellationToken cancellationToken = default);
    }
}
