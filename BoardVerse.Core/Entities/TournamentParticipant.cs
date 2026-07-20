using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Người chơi đăng ký tham gia tournament Splendor.
/// Mỗi user chỉ có 1 participant record / tournament (unique index).
///
/// Lifecycle:
/// Registered (đăng ký online) -> CheckedIn (đến quán) -> Active (đang thi đấu)
/// -> Eliminated / Finished (cuối giải).
/// </summary>
public class TournamentParticipant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // === Relationships ===
    public Guid TournamentId { get; set; }

    /// <summary>
    /// User đăng ký (online). Null cho walk-in (khách vãng lai không có tài khoản).
    /// </summary>
    public Guid? UserId { get; set; }

    // === Registration ===
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    /// <summary>Điểm Karma tại thời điểm đăng ký (snapshot). Walk-in = 0.</summary>
    public int KarmaAtRegistration { get; set; }

    // === Check-in ===
    public DateTime? CheckedInAt { get; set; }

    /// <summary>Staff đã quét mã check-in người chơi này.</summary>
    public Guid? CheckedInByStaffId { get; set; }

    // === Walk-in (khách vãng lai) ===
    /// <summary>
    /// True nếu participant do manager tạo thủ công tại POS cho khách vãng lai.
    /// Walk-in có UserId = null, không sync Elo/Karma về UserProfile (BR-13/14 mirror).
    /// </summary>
    public bool IsWalkIn { get; set; }

    /// <summary>
    /// Tên hiển thị cho walk-in (vd: "Lê A", "Khách vãng lai #1").
    /// Null khi IsWalkIn = false.
    /// </summary>
    public string? WalkInDisplayName { get; set; }

    /// <summary>
    /// Số điện thoại liên lạc cho walk-in (optional — dùng để thông báo kết quả hoặc prize collection nếu thắng giải).
    /// Null khi IsWalkIn = false.
    /// </summary>
    public string? WalkInPhoneNumber { get; set; }

    /// <summary>Manager đã tạo walk-in participant này.</summary>
    public Guid? RegisteredByStaffId { get; set; }

    /// <summary>
    /// Vòng mà participant chính thức tham gia (1 = từ đầu, 2 = late-join, ...).
    /// Dùng để audit và tính Elo scaled.
    /// </summary>
    public int JoinedRoundNumber { get; set; } = 1;

    // === Tournament progress ===
    public TournamentParticipantStatus Status { get; set; } = TournamentParticipantStatus.Registered;

    /// <summary>
    /// Tổng điểm Prestige Points sau 3 vòng Swiss (snapshot lúc Final bắt đầu).
    /// Cùng với CardsBought dùng để xếp hạng khi hòa.
    /// </summary>
    public int TotalPrestigePoints { get; set; }

    /// <summary>
    /// Tổng số thẻ Development đã mua (tiebreaker khi hòa điểm Prestige).
    /// Ít thẻ hơn = thắng khi hòa (theo luật Splendor).
    /// </summary>
    public int TotalCardsBought { get; set; }

    /// <summary>
    /// Thứ hạng cuối cùng (1 = Winner). Set khi Tournament.Status = Completed.
    /// </summary>
    public int? FinalRank { get; set; }

    // === Elo rating (BR-10: Elo chỉ dùng trong phân hệ Giải đấu) ===
    /// <summary>
    /// Điểm Elo toàn cục của người chơi tại thời điểm đăng ký tournament (snapshot ban đầu).
    /// Dùng để tính Elo delta qua 3 vòng Swiss + Final.
    /// </summary>
    public int InitialElo { get; set; }

    /// <summary>Số trận thắng trong vòng Swiss (Swiss points = wins).</summary>
    public int SwissWins { get; set; }

    /// <summary>Số trận hòa trong vòng Swiss (0.5 điểm mỗi trận).</summary>
    public int SwissDraws { get; set; }

    /// <summary>Số trận thua trong vòng Swiss.</summary>
    public int SwissLosses { get; set; }

    /// <summary>
    /// Elo delta tích lũy sau tất cả các vòng (Swiss + Final).
    /// Dương = tăng, âm = giảm.
    /// </summary>
    public int EloDelta { get; set; }

    /// <summary>
    /// Điểm Elo cuối cùng sau khi tournament hoàn thành (= InitialElo + EloDelta).
    /// = 0 nếu chưa Complete.
    /// </summary>
    public int FinalElo { get; set; }

    // === Audit ===
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // === Navigation ===
    public virtual Tournament Tournament { get; set; } = null!;
    public virtual User? User { get; set; }
    public virtual User? CheckedInByStaff { get; set; }
    public virtual User? RegisteredByStaff { get; set; }
}
