using BoardVerse.Core.Enum;
using BoardVerse.Core.Helpers;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Giải đấu board game tại quán cafe.
/// Theo thể thức Splendor Tournament: Swiss 3 rounds + Final 4.
///
/// Luồng trạng thái:
/// Draft -> RegistrationOpen -> RegistrationClosed -> OnGoing -> Completed
///                                       \-> Cancelled
///                                       \-> Cancelled (từ RegistrationOpen)
///
/// Quy tắc:
/// - Chỉ dành cho game Splendor (hardcode vì giáo viên yêu cầu chỉ tập trung 1 game).
/// - Miễn phí tham gia (EntryFee = 0).
/// - MinParticipants: 4 (tối thiểu 1 bàn), MaxParticipants: 32 (8 bàn tối đa).
/// - Top 4 sau 3 vòng Swiss vào Final; vòng Final chọn ra 1 Winner.
/// </summary>
public class Tournament
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // === Cafe & Manager ===
    public Guid CafeId { get; set; }
    public Guid CreatedByManagerId { get; set; }

    // === Info ===
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    // === Game (hardcode Splendor tại thời điểm thiết kế) ===
    /// <summary>
    /// Splendor GameTemplateId. Hardcode/lookup tại thời điểm tạo tournament.
    /// Có thể thay đổi nếu sau này mở rộng sang game khác.
    /// </summary>
    public Guid GameTemplateId { get; set; }

    // === Scheduling ===
    /// <summary>Thời điểm bắt đầu tournament (round đầu tiên).</summary>
    public DateTime StartTime { get; set; }

    /// <summary>Hạn chót đăng ký (mặc định = StartTime - 1 ngày).</summary>
    public DateTime RegistrationDeadline { get; set; }

    /// <summary>Thời lượng dự kiến mỗi vòng đấu (phút). Splendor ~30 phút/bàn, set 45 để có buffer.</summary>
    public int RoundDurationMinutes { get; set; } = 45;

    // === Capacity ===
    /// <summary>Số người tối thiểu để mở giải. Mặc định 4 (= 1 bàn).</summary>
    public int MinParticipants { get; set; } = 4;

    /// <summary>Số người tối đa. Mặc định 32 (= 8 bàn). Phải là bội số của 4.</summary>
    public int MaxParticipants { get; set; } = 32;

    /// <summary>Phí tham gia. Hiện tại = 0 (miễn phí). Để sẵn cho tương lai.</summary>
    public decimal EntryFee { get; set; } = 0m;

    // === Format ===
    /// <summary>Tổng số vòng Swiss + 1 vòng Final. Mặc định 4 (3 Swiss + 1 Final).</summary>
    public int TotalRounds { get; set; } = 4;

    /// <summary>Số vòng Swiss trước khi vào Final. Mặc định 3.</summary>
    public int PreliminaryRounds { get; set; } = 3;

    /// <summary>Số người vào Final. Mặc định 4 (1 bàn chung kết).</summary>
    public int FinalistCount { get; set; } = 4;

    /// <summary>Vòng hiện tại (0 = chưa bắt đầu, 1-3 = Swiss, 4 = Final).</summary>
    public int CurrentRound { get; set; } = 0;

    /// <summary>
    /// Thời điểm bắt đầu giải đấu (khi chuyển sang OnGoing).
    /// Dùng để track khi nào tournament start để detect no-show.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    // === Karma gating ===
    /// <summary>Điểm Karma tối thiểu để đăng ký. Mặc định 0 (= không yêu cầu). Range 0-100.</summary>
    public int MinKarmaRequirement { get; set; } = 0;

    // === Elo gating ===
    /// <summary>Elo tối thiểu để đăng ký. Mặc định 800.</summary>
    public int MinEloRequirement { get; set; } = 800;

    /// <summary>Elo tối đa để đăng ký. Mặc định 2400.</summary>
    public int MaxEloRequirement { get; set; } = 2400;

    // === Pairing mode ===
    /// <summary>
    /// Cách chia bàn cho các vòng đấu: Auto (hệ thống tự chia) hoặc Manual (manager tự chọn).
    /// Mặc định Auto.
    /// </summary>
    public TournamentPairingMode PairingMode { get; set; } = TournamentPairingMode.Auto;

    /// <summary>
    /// JSON lưu manual pairings cho Round 1 (Swiss). Null = dùng Auto.
    /// Format: [{"MatchNumber":1,"PlayerIds":["guid",...]}, ...]
    /// </summary>
    public string? Round1PairingsJson { get; set; }

    /// <summary>Manual pairings cho Round 2 (Swiss). Null = dùng Auto.</summary>
    public string? Round2PairingsJson { get; set; }

    /// <summary>Manual pairings cho Round 3 (Swiss). Null = dùng Auto.</summary>
    public string? Round3PairingsJson { get; set; }

    /// <summary>Manual pairings cho Round 4 (Final). Null = dùng Auto (Top 4 từ Swiss score).</summary>
    public string? FinalPairingsJson { get; set; }

    // === Karma bonus ===
    /// <summary>Điểm Karma cộng cho người thắng cuộc. Hệ thống tự tính theo <see cref="BoardVerse.Core.Helpers.TournamentKarmaPolicy.WinnerBonus"/> (mặc định +5). Manager không nhập tay.</summary>
    public int WinnerKarmaBonus { get; set; } = TournamentKarmaPolicy.WinnerBonus;

    /// <summary>Điểm Karma cộng cho runner-up (Top 2-4). Hệ thống tự tính theo <see cref="BoardVerse.Core.Helpers.TournamentKarmaPolicy.GetFinalistBonus(int,int)"/>.</summary>
    public int FinalistKarmaBonus { get; set; } = TournamentKarmaPolicy.WinnerBonus;

    /// <summary>Điểm Karma trừ cho người không đến (no-show). Mặc định -10, manager có thể override qua DTO.</summary>
    public int NoShowKarmaPenalty { get; set; } = TournamentKarmaPolicy.NoShowPenalty;

    // === Cancellation ===
    public string? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }

    // === Shortage handling ===
    /// <summary>
    /// Có cho phép tự động extend registration khi thiếu người không.
    /// Manager bật khi tạo tournament. Nếu thiếu người khi bấm Start:
    /// - AutoExtendOnShortage = true: extend registration +30 phút + push notif cho users chưa check-in.
    /// - AutoExtendOnShortage = false: throw 409 yêu cầu force-start manual.
    /// </summary>
    public bool AutoExtendOnShortage { get; set; } = false;

    /// <summary>Số lần extend tối đa. Mặc định 2 (= tổng cộng +60 phút).</summary>
    public int MaxExtensionCount { get; set; } = 2;

    /// <summary>Số phút mỗi lần extend. Mặc định 30.</summary>
    public int ExtensionMinutesPerAttempt { get; set; } = 30;

    /// <summary>Số lần đã extend. Reset khi đạt MaxExtensionCount → không cho extend nữa.</summary>
    public int ExtensionCount { get; set; } = 0;

    /// <summary>
    /// Số rounds Swiss đã chạy tại thời điểm OnGoing (có thể < PreliminaryRounds nếu auto-shorten).
    /// Ví dụ: 5 người → auto-shorten xuống 2 rounds Swiss + Final.
    /// </summary>
    public int? ActualPreliminaryRounds { get; set; }

    /// <summary>Tournament có đang ở chế độ force-start (bỏ qua MinParticipants check) không.</summary>
    public bool StartedWithShortage { get; set; } = false;

    /// <summary>
    /// Đánh dấu đã sync Elo từ participants lên UserProfile chưa.
    /// Tránh re-apply winner bonus khi CompleteTournamentAsync được retry.
    /// </summary>
    public bool IsFinalEloSynced { get; set; } = false;

    // === State ===
    public TournamentStatus Status { get; set; } = TournamentStatus.Draft;

    // === Audit ===
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // === Navigation ===
    public virtual Cafe Cafe { get; set; } = null!;
    public virtual User CreatedByManager { get; set; } = null!;
    public virtual GameTemplate GameTemplate { get; set; } = null!;
    public virtual ICollection<TournamentParticipant> Participants { get; set; } = [];
    public virtual ICollection<TournamentMatchBracket> Matches { get; set; } = [];
}
