namespace BoardVerse.Services.IServices
{
    public interface IBggGameService
    {
        Task<IReadOnlyList<Core.DTOs.Bgg.BggComponentCatalogItemDto>> GetComponentCatalogAsync();
        Task<IReadOnlyList<Core.DTOs.Bgg.BggSearchResultItemDto>> SearchGamesAsync(string query);
        Task<Core.DTOs.Bgg.BggGamePreviewDto> GetGamePreviewAsync(int bggId, bool curatedComponentsOnly = false);
        Task<Core.DTOs.Bgg.ImportGameFromBggResponseDto> ImportGameAsync(Core.DTOs.Bgg.ImportGameFromBggRequestDto request);
    }
}
