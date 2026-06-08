using BoardVerse.Core.DTOs.BGG;

namespace BoardVerse.Services.IServices
{
    public interface IBggApiService
    {
        bool IsConfigured { get; }
        Task<BggGameDto?> GetGameByIdAsync(int bggGameId);
        Task<List<BggGameDto>> GetGamesByIdsAsync(List<int> bggGameIds);
    }
}
