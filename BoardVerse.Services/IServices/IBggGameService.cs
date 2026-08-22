namespace BoardVerse.Services.IServices
{
    public interface IBggGameService
    {
        Task<IReadOnlyList<Core.DTOs.Bgg.BggComponentCatalogItemDto>> GetComponentCatalogAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<Core.DTOs.Bgg.BggSearchResultItemDto>> SearchGamesAsync(string query, CancellationToken cancellationToken = default);
        Task<Core.DTOs.Bgg.BggGamePreviewDto> GetGamePreviewAsync(int bggId, bool curatedComponentsOnly = false, CancellationToken cancellationToken = default);
        Task<Core.DTOs.Bgg.ImportGameFromBggResponseDto> ImportGameAsync(Core.DTOs.Bgg.ImportGameFromBggRequestDto request, CancellationToken cancellationToken = default);
    }
}
