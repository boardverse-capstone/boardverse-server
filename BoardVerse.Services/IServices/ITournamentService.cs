using BoardVerse.Core.DTOs.Admin;
using BoardVerse.Core.DTOs.Tournament;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

using System.Threading;
namespace BoardVerse.Services.IServices;

public interface ITournamentService
{
    // === Manager: Tournament lifecycle ===
    Task<TournamentResponseDto> CreateTournamentAsync(Guid managerId, Guid cafeId, CreateTournamentRequestDto request, CancellationToken cancellationToken = default);
    Task<TournamentResponseDto> UpdateTournamentAsync(Guid managerId, Guid tournamentId, UpdateTournamentRequestDto request, CancellationToken cancellationToken = default);
    Task<TournamentResponseDto> OpenRegistrationAsync(Guid managerId, Guid tournamentId, CancellationToken cancellationToken = default);
    Task<TournamentResponseDto> CloseRegistrationAsync(Guid managerId, Guid tournamentId, CancellationToken cancellationToken = default);
    Task<TournamentResponseDto> ReopenRegistrationAsync(Guid managerId, Guid tournamentId, CancellationToken cancellationToken = default);
    Task<TournamentResponseDto> StartTournamentAsync(Guid managerId, Guid tournamentId, CancellationToken cancellationToken = default);
    Task<TournamentResponseDto> StartTournamentWithOptionsAsync(
        Guid managerId, Guid tournamentId, StartTournamentOptionsDto options, CancellationToken cancellationToken = default);
    Task<TournamentResponseDto> ExtendRegistrationAsync(Guid managerId, Guid tournamentId);
    Task<TournamentResponseDto> CancelTournamentAsync(Guid managerId, Guid tournamentId, string? reason, CancellationToken cancellationToken = default);
    Task<TournamentResponseDto> CompleteTournamentAsync(Guid managerId, Guid tournamentId, CancellationToken cancellationToken = default);

    // === Queries ===
    Task<TournamentResponseDto> GetTournamentAsync(Guid tournamentId, Guid? currentUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TournamentResponseDto>> GetOpenTournamentsAsync(Guid? currentUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách tournament có thể lọc theo status.
    /// - status = null → lấy tất cả (mọi status) để frontend tự filter.
    /// - status = "RegistrationOpen" → trả danh sách đang mở đăng ký (giống <see cref="GetOpenTournamentsAsync"/> nhưng có Validate trước).
    /// - status = "all" → cũng trả tất cả.
    /// Status hợp lệ: Draft, RegistrationOpen, RegistrationClosed, OnGoing, Completed, Cancelled (case-insensitive).
    /// </summary>
    Task<IReadOnlyList<TournamentResponseDto>> GetTournamentsAsync(Guid? currentUserId, string? status, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TournamentResponseDto>> GetCafeTournamentsAsync(Guid cafeId, Guid? currentUserId, string? status, CancellationToken cancellationToken = default);

    // === Player: Register / Withdraw / Check-in ===
    Task<TournamentParticipantResponseDto> RegisterAsync(Guid tournamentId, Guid userId, CancellationToken cancellationToken = default);
    Task<TournamentParticipantResponseDto> WithdrawRegistrationAsync(Guid tournamentId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Manager xóa (kick) 1 participant khỏi tournament (BR-MGR-KICK-01).</summary>
    Task<TournamentParticipantResponseDto> ManagerKickParticipantAsync(
        Guid managerId, Guid tournamentId, Guid participantId, string reason, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TournamentParticipantResponseDto>> GetParticipantsAsync(Guid tournamentId);

    /// <summary>Manager/POS: Lấy danh sách participants cho check-in (validate tournament ownership).</summary>
    Task<IReadOnlyList<TournamentParticipantResponseDto>> GetParticipantsForPosAsync(Guid managerId, Guid tournamentId, CancellationToken cancellationToken = default);

    // === Player: Personal data ===
    /// <summary>
    /// Lấy danh sách tournament user đang/đã đăng ký (status filter optional).
    /// Trả về MyTournamentRegistrationDto với thông tin player trong từng tournament (status, rank, elo).
    /// </summary>
    Task<IReadOnlyList<MyTournamentRegistrationDto>> GetMyRegistrationsAsync(Guid userId, string? status = null, CancellationToken cancellationToken = default);

    /// <summary>Lịch sử Elo của user qua các tournament đã/đang tham gia.</summary>
    Task<EloHistoryResponseDto> GetEloHistoryAsync(Guid userId, CancellationToken cancellationToken = default);

    // === Leaderboard ===
    /// <summary>
    /// Top N players theo GlobalElo (default 100).
    /// Nếu <paramref name="gameTemplateId"/> != null → chỉ aggregate Elo
    /// từ tournament thuộc gameTemplateId đó (vd: top Splendor players).
    /// </summary>
    Task<LeaderboardResponseDto> GetLeaderboardAsync(int topCount = 100, Guid? gameTemplateId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Danh sách tournament đang OnGoing của 1 cafe (manager dashboard).
    /// Khác GetCafeTournamentsAsync ở chỗ: đã filter sẵn OnGoing + sort theo CurrentRound desc.
    /// </summary>
    Task<IReadOnlyList<TournamentResponseDto>> GetCafeActiveTournamentsAsync(Guid cafeId, Guid managerId, CancellationToken cancellationToken = default);

    // === POS: Check-in participants ===
    Task<TournamentParticipantResponseDto> CheckInParticipantAsync(Guid managerId, Guid tournamentId, Guid participantId, CancellationToken cancellationToken = default);
    Task<TournamentParticipantResponseDto> MarkNoShowAsync(Guid managerId, Guid tournamentId, Guid participantId, CancellationToken cancellationToken = default);

    // === Matches ===
    Task<IReadOnlyList<TournamentMatchResponseDto>> GetMatchesAsync(Guid tournamentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TournamentMatchResponseDto>> GetRoundMatchesAsync(Guid tournamentId, int roundNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Manager POS: Lấy tất cả matches của tournament (đã validate manager owns cafe).
    /// </summary>
    Task<IReadOnlyList<TournamentMatchResponseDto>> GetMatchesForPosAsync(Guid managerId, Guid tournamentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Manager POS: Lấy matches của 1 round cụ thể (đã validate manager owns cafe).
    /// </summary>
    Task<IReadOnlyList<TournamentMatchResponseDto>> GetRoundMatchesForPosAsync(Guid managerId, Guid tournamentId, int roundNumber, CancellationToken cancellationToken = default);

    Task<TournamentMatchResponseDto> StartMatchAsync(Guid managerId, Guid matchId, CancellationToken cancellationToken = default);
    Task<TournamentMatchResponseDto> RecordMatchResultAsync(Guid managerId, Guid matchId, RecordMatchResultRequestDto request, CancellationToken cancellationToken = default);
    Task<TournamentMatchResponseDto> UpdateMatchResultAsync(Guid managerId, Guid matchId, UpdateMatchResultRequestDto request, CancellationToken cancellationToken = default);
    Task<TournamentMatchResponseDto> CancelMatchAsync(Guid managerId, Guid matchId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chuyển sang vòng đấu kế tiếp. Tự động build matches cho Round tiếp theo:
    /// - Round N &lt; PreliminaryRounds: build Swiss round N+1 từ active participants.
    /// - Sau PreliminaryRounds: build bàn chung kết (Final).
    /// Yêu cầu: Round hiện tại đã hoàn thành hết các bàn.
    /// </summary>
    Task<TournamentResponseDto> AdvanceRoundAsync(Guid managerId, Guid tournamentId, CancellationToken cancellationToken = default);

    // === Manager: Manual Pairing (override auto Swiss pairing) ===

    /// <summary>Đổi pairing mode (Auto/Manual) cho tournament.</summary>
    Task<TournamentResponseDto> SetPairingModeAsync(Guid managerId, Guid tournamentId, TournamentPairingMode mode, CancellationToken cancellationToken = default);

    /// <summary>Preview auto pairings cho 1 round (chưa save, chỉ xem).</summary>
    Task<RoundPairingsResponseDto> PreviewPairingsAsync(Guid managerId, Guid tournamentId, int roundNumber, CancellationToken cancellationToken = default);

    /// <summary>Manager lưu manual pairings cho 1 round (override auto).</summary>
    Task<RoundPairingsResponseDto> SetRoundPairingsAsync(Guid managerId, Guid tournamentId, SetRoundPairingsRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Xóa manual pairings cho 1 round (quay lại dùng auto).</summary>
    Task<RoundPairingsResponseDto> ClearRoundPairingsAsync(Guid managerId, Guid tournamentId, int roundNumber, CancellationToken cancellationToken = default);

    /// <summary>Hoán đổi vị trí 2 người chơi giữa 2 bàn trong cùng round.</summary>
    Task<RoundPairingsResponseDto> SwapPairingAsync(Guid managerId, Guid tournamentId, SwapPairingRequestDto request, CancellationToken cancellationToken = default);

    // === Manager: Walk-in participant (khách vãng lai) ===

    /// <summary>
    /// Manager tạo walk-in participant tại POS cho khách vãng lai (không có tài khoản BoardVerse).
    /// Cho phép tạo ở RegistrationOpen / RegistrationClosed / OnGoing (chỉ khi R1 chưa Completed).
    /// Walk-in có UserId = null, không nhận Karma bonus / Elo sync (BR-13/14 mirror).
    /// Thực tế board game cafe: lock walk-in sau khi R1 hoàn thành để giữ fairness
    /// — player gốc đã đầu tư 1 round, walk-in không thể nhảy vào R2+ để "rửa" Swiss score.
    /// </summary>
    Task<TournamentParticipantResponseDto> ManagerAddWalkInParticipantAsync(
        Guid managerId, Guid tournamentId, AddWalkInParticipantRequestDto request, CancellationToken cancellationToken = default);

    // === Background jobs ===
    Task<int> AutoCloseExpiredRegistrationsAsync(DateTime cutoffTime, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gửi reminder notification cho participants chưa check-in của các giải đấu sắp bắt đầu.
    /// Reminder schedule: T-30, T-15, T-5 phút.
    /// </summary>
    /// <param name="now">Thời điểm hiện tại (UTC).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Số lượng notification đã gửi.</returns>
    Task<int> SendTournamentRemindersAsync(DateTime now, CancellationToken ct = default);

    /// <summary>
    /// Tự động đánh dấu no-show cho participants đã đăng ký nhưng không check-in
    /// khi giải đấu bắt đầu (OnGoing + CurrentRound = 1).
    /// Áp dụng Karma penalty nếu có cấu hình.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result chứa TournamentId và số no-show đã đánh dấu.</returns>
    Task<NoShowDetectionResult> AutoMarkNoShowsAsync(CancellationToken ct = default);

    // === Admin: Tournament management ===
    Task<AdminTournamentListResponseDto> GetAdminTournamentsAsync(
        int page, int pageSize, string? searchTerm, string? status, Guid? cafeId, CancellationToken cancellationToken = default);
    Task<AdminTournamentDetailDto?> GetAdminTournamentDetailAsync(Guid tournamentId);
    Task<TournamentResponseDto> AdminCreateTournamentAsync(
        Guid adminUserId, AdminCreateTournamentRequestDto request, CancellationToken cancellationToken = default);
    Task<TournamentResponseDto> AdminUpdateTournamentAsync(
        Guid adminUserId, Guid tournamentId, AdminUpdateTournamentRequestDto request, CancellationToken cancellationToken = default);
    Task AdminDeleteTournamentAsync(Guid adminUserId, Guid tournamentId);
    Task<AdminTournamentParticipantsResponseDto> GetAdminTournamentParticipantsAsync(
        Guid tournamentId, string? status, CancellationToken cancellationToken = default);
    Task<TournamentResponseDto> AdminOpenRegistrationAsync(Guid adminUserId, Guid tournamentId);
    Task<TournamentResponseDto> AdminCloseRegistrationAsync(Guid adminUserId, Guid tournamentId);
    Task<TournamentResponseDto> AdminStartTournamentAsync(Guid adminUserId, Guid tournamentId, CancellationToken cancellationToken = default);
    Task<TournamentResponseDto> AdminCompleteTournamentAsync(Guid adminUserId, Guid tournamentId, CancellationToken cancellationToken = default);
    Task<TournamentResponseDto> AdminCancelTournamentAsync(
        Guid adminUserId, Guid tournamentId, string? reason, CancellationToken cancellationToken = default);
}