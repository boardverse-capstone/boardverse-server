using BoardVerse.Core.Entities;

namespace BoardVerse.Core.IRepositories
{
    public interface ILobbyRepository
    {
        Task<Lobby?> GetByIdAsync(Guid lobbyId);
        Task<Lobby?> GetByIdWithMembersAsync(Guid lobbyId);
        Task<Lobby?> GetByActiveSessionIdAsync(Guid activeSessionId);
        Task<IReadOnlyList<Lobby>> GetActiveLobbiesForGameAsync(Guid gameTemplateId, Guid? excludeLobbyId);
        Task<IReadOnlyList<Lobby>> SearchLobbiesNearbyAsync(Guid gameTemplateId, double latitude, double longitude, double radiusKm, int? minKarmaScore);
        Task AddAsync(Lobby lobby);
        Task AddMemberAsync(LobbyMember member);
        Task SaveChangesAsync();
    }
}
