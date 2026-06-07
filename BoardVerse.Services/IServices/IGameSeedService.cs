using BoardVerse.Core.DTOs.BGG;

namespace BoardVerse.Services.IServices
{
    public interface IGameSeedService
    {
        Task SeedGamesFromBggAsync(List<int> bggGameIds);
        Task SeedSingleGameFromBggAsync(int bggGameId);
    }
}
