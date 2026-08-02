using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Cấu hình hạn mức riêng của cafe (BR-NEW-12 §XIII).
/// MVP: nếu chưa có row thì dùng giá trị default hard-coded trong entity.
/// </summary>
public interface ICafeConfigRepository
{
    Task<CafeConfig?> GetByCafeIdAsync(Guid cafeId);

    Task<CafeConfig> GetOrCreateDefaultAsync(Guid cafeId);

    Task UpdateAsync(CafeConfig config);

    Task SaveChangesAsync();
}