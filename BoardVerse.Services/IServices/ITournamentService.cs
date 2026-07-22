using BoardVerse.Core.DTOs.Tournament;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;

namespace BoardVerse.Services.IServices;

public interface ITournamentService
{
    // === Manager: Tournament lifecycle ===
    Task<TournamentResponseDto> CreateTournamentAsync(Guid managerId, Guid cafeId, CreateTournamentRequestDto request);
    Task<TournamentResponseDto> UpdateTournamentAsync(Guid managerId, Guid tournamentId, UpdateTournamentRequestDto request);
    Task<TournamentResponseDto> OpenRegistrationAsync(Guid managerId, Guid tournamentId);
    Task<TournamentResponseDto> CloseRegistrationAsync(Guid managerId, Guid tournamentId);
    Task<TournamentResponseDto> ReopenRegistrationAsync(Guid managerId, Guid tournamentId);
    Task<TournamentResponseDto> StartTournamentAsync(Guid managerId, Guid tournamentId);
    Task<TournamentResponseDto> StartTournamentWithOptionsAsync(
        Guid managerId, Guid tournamentId, StartTournamentOptionsDto options);
    Task<TournamentResponseDto> ExtendRegistrationAsync(Guid managerId, Guid tournamentId);
    Task<TournamentResponseDto> CancelTournamentAsync(Guid managerId, Guid tournamentId, string? reason);
    Task<TournamentResponseDto> CompleteTournamentAsync(Guid managerId, Guid tournamentId);

    // === Queries ===
    Task<TournamentResponseDto> GetTournamentAsync(Guid tournamentId, Guid? currentUserId);
    Task<IReadOnlyList<TournamentResponseDto>> GetOpenTournamentsAsync(Guid? currentUserId);
    Task<IReadOnlyList<TournamentResponseDto>> GetCafeTournamentsAsync(Guid cafeId, Guid? currentUserId, string? status);

    // === Player: Register / Withdraw / Check-in ===
    Task<TournamentParticipantResponseDto> RegisterAsync(Guid tournamentId, Guid userId);
    Task<TournamentParticipantResponseDto> WithdrawRegistrationAsync(Guid tournamentId, Guid userId);
    Task<IReadOnlyList<TournamentParticipantResponseDto>> GetParticipantsAsync(Guid tournamentId);

    // === Player: Personal data ===
    /// <summary>
    /// Lấy danh sách tournament user đang/đã đăng ký (status filter optional).
    /// Trả về MyTournamentRegistrationDto với thông tin player trong từng tournament (status, rank, elo).
    /// </summary>
    Task<IReadOnlyList<MyTournamentRegistrationDto>> GetMyRegistrationsAsync(Guid userId, string? status = null);

    /// <summary>Lịch sử Elo của user qua các tournament đã/đang tham gia.</summary>
    Task<EloHistoryResponseDto> GetEloHistoryAsync(Guid userId);

    // === Leaderboard ===
    /// <summary>
    /// Top N players theo GlobalElo (default 100).
    /// Nếu <paramref name="gameTemplateId"/> != null → chỉ aggregate Elo
    /// từ tournament thuộc gameTemplateId đó (vd: top Splendor players).
    /// </summary>
    Task<LeaderboardResponseDto> GetLeaderboardAsync(int topCount = 100, Guid? gameTemplateId = null);

    /// <summary>
    /// Danh sách tournament đang OnGoing của 1 cafe (manager dashboard).
    /// Khác GetCafeTournamentsAsync ở chỗ: đã filter sẵn OnGoing + sort theo CurrentRound desc.
    /// </summary>
    Task<IReadOnlyList<TournamentResponseDto>> GetCafeActiveTournamentsAsync(Guid cafeId, Guid managerId);

    // === POS: Check-in participants ===
    Task<TournamentParticipantResponseDto> CheckInParticipantAsync(Guid managerId, Guid tournamentId, Guid participantId);
    Task<TournamentParticipantResponseDto> MarkNoShowAsync(Guid managerId, Guid tournamentId, Guid participantId);

    // === Matches ===
    Task<IReadOnlyList<TournamentMatchResponseDto>> GetMatchesAsync(Guid tournamentId);
    Task<IReadOnlyList<TournamentMatchResponseDto>> GetRoundMatchesAsync(Guid tournamentId, int roundNumber);
    Task<TournamentMatchResponseDto> StartMatchAsync(Guid managerId, Guid matchId);
    Task<TournamentMatchResponseDto> RecordMatchResultAsync(Guid managerId, Guid matchId, RecordMatchResultRequestDto request);
    Task<TournamentMatchResponseDto> UpdateMatchResultAsync(Guid managerId, Guid matchId, UpdateMatchResultRequestDto request);
    Task<TournamentMatchResponseDto> CancelMatchAsync(Guid managerId, Guid matchId, string reason);

    /// <summary>
    /// Chuyển sang vòng đấu kế tiếp. Tự động build matches cho Round tiếp theo:
    /// - Round N &lt; PreliminaryRounds: build Swiss round N+1 từ active participants.
    /// - Sau PreliminaryRounds: build bàn chung kết (Final).
    /// Yêu cầu: Round hiện tại đã hoàn thành hết các bàn.
    /// </summary>
    Task<TournamentResponseDto> AdvanceRoundAsync(Guid managerId, Guid tournamentId);

    // === Manager: Manual Pairing (override auto Swiss pairing) ===

    /// <summary>Đổi pairing mode (Auto/Manual) cho tournament.</summary>
    Task<TournamentResponseDto> SetPairingModeAsync(Guid managerId, Guid tournamentId, TournamentPairingMode mode);

    /// <summary>Preview auto pairings cho 1 round (chưa save, chỉ xem).</summary>
    Task<RoundPairingsResponseDto> PreviewPairingsAsync(Guid managerId, Guid tournamentId, int roundNumber);

    /// <summary>Manager lưu manual pairings cho 1 round (override auto).</summary>
    Task<RoundPairingsResponseDto> SetRoundPairingsAsync(Guid managerId, Guid tournamentId, SetRoundPairingsRequestDto request);

    /// <summary>Xóa manual pairings cho 1 round (quay lại dùng auto).</summary>
    Task<RoundPairingsResponseDto> ClearRoundPairingsAsync(Guid managerId, Guid tournamentId, int roundNumber);

    // === Manager: Walk-in participant (khách vãng lai) ===

    /// <summary>
    /// Manager tạo walk-in participant tại POS cho khách vãng lai (không có tài khoản BoardVerse).
    /// Cho phép tạo ở RegistrationOpen / RegistrationClosed / OnGoing (chỉ khi R1 chưa Completed).
    /// Walk-in có UserId = null, không nhận Karma bonus / Elo sync (BR-13/14 mirror).
    /// Thực tế board game cafe: lock walk-in sau khi R1 hoàn thành để giữ fairness
    /// — player gốc đã đầu tư 1 round, walk-in không thể nhảy vào R2+ để "rửa" Swiss score.
    /// </summary>
    Task<TournamentParticipantResponseDto> ManagerAddWalkInParticipantAsync(
        Guid managerId, Guid tournamentId, AddWalkInParticipantRequestDto request);

    // === Background jobs ===
    Task<int> AutoCloseExpiredRegistrationsAsync(DateTime cutoffTime);

    /// <summary>
    /// Cancellable variant — pass stoppingToken xuống DB calls.
    /// Background job nên dùng overload này để shutdown nhanh khi app tắt.
    /// </summary>
    Task<int> AutoCloseExpiredRegistrationsAsync(DateTime cutoffTime, CancellationToken cancellationToken);

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
}