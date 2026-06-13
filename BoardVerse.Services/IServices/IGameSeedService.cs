namespace BoardVerse.Services.IServices
{
    public interface IGameSeedService
    {
        Task SeedGamesFromCatalogAsync(List<string>? slugs = null);
        Task SeedSingleGameAsync(string slug);
    }
}
