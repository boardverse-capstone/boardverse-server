using BoardVerse.Core.Entities;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class FriendReportRepository : IFriendReportRepository
{
    private readonly BoardVerseDbContext _db;

    public FriendReportRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public Task<FriendReport?> GetByIdAsync(Guid id)
        => _db.FriendReports.FirstOrDefaultAsync(r => r.Id == id);

    public Task<FriendReport?> GetPendingByReporterAndTargetAsync(Guid reporterId, Guid targetUserId)
        => _db.FriendReports.FirstOrDefaultAsync(r =>
            r.ReporterId == reporterId &&
            r.TargetUserId == targetUserId &&
            r.Status == "Pending");

    public async Task<IReadOnlyList<FriendReport>> GetByReporterAsync(Guid reporterId)
        => await _db.FriendReports
            .Where(r => r.ReporterId == reporterId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

    public Task AddAsync(FriendReport report)
    {
        _db.FriendReports.Add(report);
        return Task.CompletedTask;
    }

    public void Update(FriendReport report) => _db.FriendReports.Update(report);

    public Task SaveChangesAsync() => _db.SaveChangesAsync();
}
