namespace BoardVerse.Services.IServices
{
    /// <summary>
    /// Kết quả của <see cref="IBggGameService.ReimportWeightAsync"/>.
    /// Dùng cho background job <c>MissingWeightBggRetryJob</c> để quyết định tiếp tục retry hay dừng.
    /// </summary>
    public enum BggReimportResult
    {
        /// <summary>Game không còn tồn tại (đã bị admin xóa/IsActive=false). Skip.</summary>
        GameNotFound = 0,

        /// <summary>Game không có BggId (không thể reimport). Skip.</summary>
        NoBggId = 1,

        /// <summary>Game đã có Weight rồi. Skip.</summary>
        AlreadyHasWeight = 2,

        /// <summary>Reimport thành công, đã set Weight từ BGG.</summary>
        WeightUpdated = 3,

        /// <summary>Reimport chạy được nhưng BGG vẫn chưa trả Weight. Retry counter được increment.</summary>
        WeightStillMissing = 4,

        /// <summary>BGG API lỗi (timeout/network). Retry counter được increment.</summary>
        TransientFailure = 5
    }

    public interface IBggGameService
    {
        Task<IReadOnlyList<Core.DTOs.Bgg.BggComponentCatalogItemDto>> GetComponentCatalogAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<Core.DTOs.Bgg.BggSearchResultItemDto>> SearchGamesAsync(string query, CancellationToken cancellationToken = default);
        Task<Core.DTOs.Bgg.BggGamePreviewDto> GetGamePreviewAsync(int bggId, bool curatedComponentsOnly = false, CancellationToken cancellationToken = default);
        Task<Core.DTOs.Bgg.ImportGameFromBggResponseDto> ImportGameAsync(Core.DTOs.Bgg.ImportGameFromBggRequestDto request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Re-import Weight từ BGG cho 1 game đã tồn tại (lightweight, chỉ update Weight + audit fields).
        /// Dùng cho background job <c>MissingWeightBggRetryJob</c> tự động heal game missing Weight.
        /// Idempotent: gọi nhiều lần không gây side-effect ngoài update BggRetryCount + LastBggRetryAt.
        /// </summary>
        /// <param name="gameTemplateId">Id của GameTemplate cần retry.</param>
        /// <param name="cancellationToken">Token hủy.</param>
        /// <returns>Kết quả re-import — để caller quyết định có retry tiếp không.</returns>
        Task<BggReimportResult> ReimportWeightAsync(Guid gameTemplateId, CancellationToken cancellationToken = default);
    }
}
