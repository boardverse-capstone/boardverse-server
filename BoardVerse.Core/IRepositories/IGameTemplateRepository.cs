using BoardVerse.Core.Common;
using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories
{
    public interface IGameTemplateRepository
    {
        Task<PaginatedResponse<GameTemplate>> GetPagedAsync(GetMasterGamesQuery query, CancellationToken cancellationToken = default);
        Task<PaginatedResponse<GameTemplate>> GetBoardGamesPagedAsync(GetMasterGamesQuery query, CancellationToken cancellationToken = default);

        /// <summary>
        /// Top N board game được chơi nhiều nhất trong hệ thống (đếm theo số lượt session đã bắt đầu).
        /// Dùng cho widget "Top board game hot" trên UI mobile.
        /// </summary>
        /// <param name="topCount">Số lượng game trả về (ví dụ 5).</param>
        /// <param name="cancellationToken">Token hủy.</param>
        /// <returns>Danh sách TopBoardGameDto sắp xếp giảm dần theo PlayCount.</returns>
        Task<List<TopBoardGameDto>> GetTopPlayedBoardGamesAsync(int topCount, CancellationToken cancellationToken = default);

        Task<GameTemplate?> GetByIdWithComponentsAsync(Guid id, CancellationToken cancellationToken = default);
        Task<GameTemplate?> GetActiveByIdWithComponentsAsync(Guid id, CancellationToken cancellationToken = default);
        Task<GameTemplate?> GetByIdWithCategoriesForUpdateAsync(Guid id, CancellationToken cancellationToken = default);
        Task<GameTemplate?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);
        Task<GameTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<GameTemplate?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
        Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
        Task<Dictionary<Guid, int>> GetComponentCountsByGameIdsAsync(IReadOnlyCollection<Guid> gameIds, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lấy danh sách board game master đang thiếu <c>Weight</c> (Weight == null).
        /// Dùng cho admin dashboard — game import từ BGG mà BGG chưa có đủ vote averageweight,
        /// hoặc admin tạo tay nhưng quên set Weight. Sort theo tên.
        /// </summary>
        /// <param name="includeInactive">Khi true, bao gồm cả game IsActive = false. Mặc định false (chỉ active).</param>
        /// <param name="cancellationToken">Token hủy.</param>
        /// <returns>Danh sách GameTemplate có Weight = null, sort theo Name.</returns>
        Task<List<GameTemplate>> GetGamesWithMissingWeightAsync(bool includeInactive, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lấy danh sách game candidate cho background job retry Weight từ BGG.
        /// Chỉ lấy game:
        /// <list type="bullet">
        /// <item>Weight = null</item>
        /// <item>BggId != null (mới retry được)</item>
        /// <item>IsActive = true (skip game admin đã deactivate)</item>
        /// <item>BggRetryCount &lt; maxRetryRounds (chưa vượt ngưỡng)</item>
        /// </list>
        /// Sort theo <c>LastBggRetryAt ASC NULLS FIRST</c> để ưu tiên game retry ít lần trước nhất.
        /// </summary>
        /// <param name="maxRetryRounds">Ngưỡng retry tối đa (lấy từ BggSettings.MissingWeightMaxRetryRounds).</param>
        /// <param name="batchSize">Số lượng tối đa (lấy từ BggSettings.MissingWeightRetryBatchSize).</param>
        /// <param name="cancellationToken">Token hủy.</param>
        Task<List<GameTemplate>> GetMissingWeightRetryCandidatesAsync(
            int maxRetryRounds,
            int batchSize,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Kiểm tra cafe có trong kho (CafeGameInventory) game này không.
        /// </summary>
        Task<bool> CafeHasGameAsync(Guid cafeId, Guid gameTemplateId, CancellationToken cancellationToken = default);

        Task SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
