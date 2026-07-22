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

    public async Task<LobbyInvite?> GetByIdAsync(Guid id)
    {
        return await _db.LobbyInvites
            .Include(i => i.Lobby).ThenInclude(l => l.Members)
            .Include(i => i.Inviter).ThenInclude(u => u.Profile)
            .Include(i => i.Invitee).ThenInclude(u => u.Profile)
            .FirstOrDefaultAsync(i => i.Id == id);
    }

    public async Task<LobbyInvite?> GetPendingInviteAsync(Guid lobbyId, Guid inviteeId)
    {
        return await _db.LobbyInvites
            .FirstOrDefaultAsync(i => i.LobbyId == lobbyId
                && i.InviteeId == inviteeId
                && i.Status == LobbyInviteStatus.Pending
                && i.ExpiresAt > DateTime.UtcNow);
    }

    public async Task<LobbyInvite?> GetAcceptedInviteAsync(Guid lobbyId, Guid inviteeId)
    {
        return await _db.LobbyInvites
            .FirstOrDefaultAsync(i => i.LobbyId == lobbyId
                && i.InviteeId == inviteeId
                && i.Status == LobbyInviteStatus.Accepted);
    }

    public async Task<IReadOnlyList<LobbyInvite>> GetByLobbyAsync(Guid lobbyId, LobbyInviteStatus? status = null)
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

    public async Task<IReadOnlyList<LobbyInvite>> GetPendingByInviteeAsync(Guid inviteeId)
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

    public async Task<IReadOnlyList<LobbyInvite>> GetAllByInviteeAsync(Guid inviteeId, LobbyInviteStatus? status = null)
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

    public Task AddAsync(LobbyInvite invite)
    {
        _db.LobbyInvites.Add(invite);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<LobbyInvite>> CancelPendingBetweenAsync(Guid userAId, Guid userBId)
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

    public async Task<IReadOnlyList<LobbyInvite>> GetExpiredPendingAsync(DateTime now)
    {
        return await _db.LobbyInvites
            .Where(i => i.Status == LobbyInviteStatus.Pending && i.ExpiresAt <= now)
            .ToListAsync();
    }

    public async Task<int> CancelAllPendingForLobbyAsync(Guid lobbyId)
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

    public async Task<int> CancelPendingForLobbyAndInviteeAsync(Guid lobbyId, Guid inviteeId)
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

    public Task SaveChangesAsync()
    {
        return _db.SaveChangesAsync();
    }
}