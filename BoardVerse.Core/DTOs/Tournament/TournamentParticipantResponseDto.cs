using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Tournament;

public class TournamentParticipantResponseDto
{
    public Guid Id { get; set; }
    public Guid TournamentId { get; set; }

    /// <summary>UserId (null cho walk-in).</summary>
    public Guid? UserId { get; set; }

    /// <summary>Username; null cho walk-in.</summary>
    public string? Username { get; set; }

    /// <summary>Avatar URL; null cho walk-in.</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>Tên hiển thị cho walk-in (vd: "Lê A", "Khách vãng lai #1"). Null khi IsWalkIn = false.</summary>
    public string? WalkInDisplayName { get; set; }

    /// <summary>Số điện thoại liên lạc cho walk-in (optional). Null khi IsWalkIn = false.</summary>
    public string? WalkInPhoneNumber { get; set; }

    /// <summary>True nếu là walk-in (khách vãng lai, không có tài khoản).</summary>
    public bool IsWalkIn { get; set; }

    /// <summary>Vòng tham gia (1 = từ đầu; > 1 = late-join).</summary>
    public int JoinedRoundNumber { get; set; }

    public DateTime RegisteredAt { get; set; }
    public int KarmaAtRegistration { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public Guid? CheckedInByStaffId { get; set; }
    public Guid? RegisteredByStaffId { get; set; }
    public TournamentParticipantStatus Status { get; set; }
    public int TotalPrestigePoints { get; set; }
    public int TotalCardsBought { get; set; }
    public int? FinalRank { get; set; }

    // === Elo (BR-10: chỉ dùng trong phân hệ Giải đấu) ===
    /// <summary>Elo của user ngay khi đăng ký tournament.</summary>
    public int InitialElo { get; set; }

    /// <summary>Elo hiện tại (running total sau các ván đã chơi).</summary>
    public int CurrentElo { get; set; }

    /// <summary>Elo delta tích lũy trong tournament này (chưa tính winner bonus).</summary>
    public int EloDelta { get; set; }

    /// <summary>Elo cuối cùng sau khi tournament Completed (= InitialElo + EloDelta + winner bonus nếu có).</summary>
    public int FinalElo { get; set; }

    // === Swiss performance ===
    public int SwissWins { get; set; }
    public int SwissDraws { get; set; }
    public int SwissLosses { get; set; }

    /// <summary>Điểm Swiss = Wins + Draws*0.5.</summary>
    public double SwissScore { get; set; }
}