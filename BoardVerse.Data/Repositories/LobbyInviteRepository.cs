using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories;

public class LobbyInviteRepository : ILobbyInviteRepository
{
    private readonly BoardVerseDbContext _db;

    public LobbyInviteRepository(BoardVerseDbContext db)
    {
        _db = db;
    }

    public async Task<LobbyInvite?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.LobbyInvites
            .Include(i => i.Lobby).ThenInclude(l => l.Members)
            .Include(i => i.Inviter).ThenInclude(u => u.Profile)
            .Include(i => i.Invitee).ThenInclude(u => u.Profile)
            .FirstOrDefaultAsync(i => i.Id == id);
    }

    public async Task<LobbyInvite?> GetPendingInviteAsync(Guid lobbyId, Guid inviteeId, CancellationToken cancellationToken = default)
    {
        return await _db.LobbyInvites
            .FirstOrDefaultAsync(i => i.LobbyId == lobbyId
                && i.InviteeId == inviteeId
                && i.Status == LobbyInviteStatus.Pending
                && i.ExpiresAt > DateTime.UtcNow);
    }

    public async Task<LobbyInvite?> GetAcceptedInviteAsync(Guid lobbyId, Guid inviteeId, CancellationToken cancellationToken = default)
    {
        return await _db.LobbyInvites
            .FirstOrDefaultAsync(i => i.LobbyId == lobbyId
                && i.InviteeId == inviteeId
                && i.Status == LobbyInviteStatus.Accepted);
    }

    public async Task<IReadOnlyList<LobbyInvite>> GetByLobbyAsync(Guid lobbyId, LobbyInviteStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = _db.LobbyInvites
            .Include(i => i.Inviter).ThenInclude(u => u.Profile)
            .Include(i => i.Invitee).ThenInclude(u => u.Profile)
            .Where(i => i.LobbyId == lobbyId);

        if (status.HasValue)
        {
            query = query.Where(i => i.Status == status.Value);
        }

        return await query.OrderByDescending(i => i.CreatedAt).ToListAsync();
    }

    public async Task<IReadOnlyList<LobbyInvite>> GetPendingByInviteeAsync(Guid inviteeId, CancellationToken cancellationToken = default)
    {
        return await _db.LobbyInvites
            .Include(i => i.Lobby).ThenInclude(l => l.Members)
            .Include(i => i.Inviter).ThenInclude(u => u.Profile)
            .Where(i => i.InviteeId == inviteeId
                && i.Status == LobbyInviteStatus.Pending
                && i.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<LobbyInvite>> GetAllByInviteeAsync(Guid inviteeId, LobbyInviteStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = _db.LobbyInvites
            .Include(i => i.Lobby).ThenInclude(l => l.Members)
            .Include(i => i.Inviter).ThenInclude(u => u.Profile)
            .Where(i => i.InviteeId == inviteeId);

        if (status.HasValue)
        {
            query = query.Where(i => i.Status == status.Value);
        }

        return await query.OrderByDescending(i => i.CreatedAt).ToListAsync();
    }

    public Task AddAsync(LobbyInvite invite, CancellationToken cancellationToken = default)
    {
        _db.LobbyInvites.Add(invite);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<LobbyInvite>> CancelPendingBetweenAsync(Guid userAId, Guid userBId, CancellationToken cancellationToken = default)
    {
        var pending = await _db.LobbyInvites
            .Where(i => i.Status == LobbyInviteStatus.Pending &&
                        ((i.InviterId == userAId && i.InviteeId == userBId) ||
                         (i.InviterId == userBId && i.InviteeId == userAId)))
            .ToListAsync();

        if (pending.Count == 0) return pending;

        var now = DateTime.UtcNow;
        foreach (var inv in pending)
        {
            inv.Status = LobbyInviteStatus.Cancelled;
            inv.RespondedAt = now;
        }
        await _db.SaveChangesAsync();
        return pending;
    }

    public async Task<IReadOnlyList<LobbyInvite>> GetExpiredPendingAsync(
        DateTime now, int limit = 500, CancellationToken cancellationToken = default)
    {
        if (limit <= 0) limit = 500;
        return await _db.LobbyInvites
            .Where(i => i.Status == LobbyInviteStatus.Pending && i.ExpiresAt <= now)
            .OrderBy(i => i.ExpiresAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CancelAllPendingForLobbyAsync(Guid lobbyId, CancellationToken cancellationToken = default)
    {
        var pending = await _db.LobbyInvites
            .Where(i => i.LobbyId == lobbyId && i.Status == LobbyInviteStatus.Pending)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var inv in pending)
        {
            inv.Status = LobbyInviteStatus.Cancelled;
            inv.RespondedAt = now;
        }

        if (pending.Count > 0)
        {
            await _db.SaveChangesAsync();
        }
        return pending.Count;
    }

    public async Task<int> CancelPendingForLobbyAndInviteeAsync(Guid lobbyId, Guid inviteeId, CancellationToken cancellationToken = default)
    {
        var pending = await _db.LobbyInvites
            .Where(i => i.LobbyId == lobbyId
                && i.InviteeId == inviteeId
                && i.Status == LobbyInviteStatus.Pending)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var inv in pending)
        {
            inv.Status = LobbyInviteStatus.Cancelled;
            inv.RespondedAt = now;
        }

        if (pending.Count > 0)
        {
            await _db.SaveChangesAsync();
        }
        return pending.Count;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync();
    }

    public async Task<int> CountPendingByInviteeSinceAsync(Guid inviteeId, DateTime since, CancellationToken cancellationToken = default)
    {
        return await _db.LobbyInvites
            .Where(i => i.InviteeId == inviteeId
                && i.Status == LobbyInviteStatus.Pending
                && i.CreatedAt >= since)
            .CountAsync();
    }

    public async Task<int> CountSentByInviterSinceAsync(Guid inviterId, DateTime since, CancellationToken cancellationToken = default)
    {
        return await _db.LobbyInvites
            .Where(i => i.InviterId == inviterId && i.CreatedAt >= since)
            .CountAsync();
    }
}