using BoardVerse.Core.DTOs.BGG;

namespace BoardVerse.Services.IServices
{
    public interface IBggApiService
    {
        Task<BggGameDto?> GetGameByIdAsync(int bggGameId);
        Task<List<BggGameDto>> GetGamesByIdsAsync(List<int> bggGameIds);
    }
}
