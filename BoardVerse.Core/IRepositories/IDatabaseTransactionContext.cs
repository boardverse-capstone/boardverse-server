using System.Data;

namespace BoardVerse.Core.IRepositories;

/// <summary>
/// Lightweight abstraction cho database transaction — tránh phụ thuộc Microsoft.EntityFrameworkCore
/// từ BoardVerse.Core (chỉ chứa domain interfaces). Implementation nằm trong BoardVerse.Data.
/// </summary>
public interface IDatabaseTransactionContext : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
