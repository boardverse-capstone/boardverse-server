using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Cấu hình hạn mức riêng của cafe (BR-NEW-12 §XIII).
/// MVP: nếu chưa có row thì dùng giá trị default hard-coded trong entity.
/// </summary>
public interface ICafeConfigRepository
{
    Task<CafeConfig?> GetByCafeIdAsync(Guid cafeId, CancellationToken cancellationToken = default);

    Task<CafeConfig> GetOrCreateDefaultAsync(Guid cafeId, CancellationToken cancellationToken = default);

    Task UpdateAsync(CafeConfig config, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}