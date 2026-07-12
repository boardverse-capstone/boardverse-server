using BoardVerse.Core.DTOs.Lobby;
using BoardVerse.Core.Entities;

namespace BoardVerse.Services.IServices
{
    public interface ILobbyService
    {
        Task<LobbyResponseDto> CreateLobbyAsync(Guid hostUserId, CreateLobbyRequestDto request);
        Task<LobbyResponseDto> JoinLobbyAsync(Guid lobbyId, Guid userId);
        Task<LobbyResponseDto> LeaveLobbyAsync(Guid lobbyId, Guid userId);
        Task<LobbyResponseDto> GetLobbyAsync(Guid lobbyId);
        Task<IReadOnlyList<LobbyResponseDto>> SearchLobbiesAsync(SearchLobbiesRequestDto request);
        Task<LobbyResponseDto> CloseLobbyAsync(Guid lobbyId, Guid hostUserId);
        Task<LobbyResponseDto> LockLobbyAsync(Guid lobbyId, Guid hostUserId);
        Task<LobbyResponseDto> OpenKarmaWindowAsync(Guid lobbyId, Guid hostUserId);
        Task<LobbyResponseDto> TransitionToInProgressAsync(Guid lobbyId, Guid? activeSessionId);
        Task<LobbyResponseDto> TransitionToClosedAsync(Guid lobbyId);
    }
}
