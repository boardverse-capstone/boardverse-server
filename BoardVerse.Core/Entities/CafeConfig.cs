namespace BoardVerse.Core.Entities;

/// <summary>
/// Cấu hình hạn mức riêng của từng cafe (BR-NEW-12 §XIII).
/// MVP có thể dùng giá trị mặc định hard-coded nếu row chưa tồn tại.
/// </summary>
public class CafeConfig
{
    public Guid Id { get; set; }

    /// <summary>FK Cafe (1-1).</summary>
    public Guid CafeId { get; set; }

    public int Capacity { get; set; } = 30;

    public int MaxLobbiesPerUserPerDay { get; set; } = 1;
    public int MaxPlayersPerLobbySameDay { get; set; } = 30;
    public int MaxPlayersPerLobby1Day { get; set; } = 20;
    public int MaxPlayersPerLobby2Days { get; set; } = 15;
    public int MaxPlayersPerLobby3To4Days { get; set; } = 10;
    public int MaxPlayersPerLobby5To7Days { get; set; } = 6;

    /// <summary>BVC — theo BR-NEW-01 §8.</summary>
    public long MinDepositSameDay { get; set; } = 50_000;
    public long MinDeposit1Day { get; set; } = 50_000;
    public long MinDeposit2Days { get; set; } = 100_000;
    public long MinDeposit3To4Days { get; set; } = 150_000;
    public long MinDeposit5To7Days { get; set; } = 200_000;

    public bool RequireApprovalForDistant { get; set; } = true;

    /// <summary>BR-NEW-11: ngưỡng số ngày tới playDate để bắt buộc cafe duyệt.</summary>
    public int DistantThresholdDays { get; set; } = 2;

    public int ApprovalTimeoutHours { get; set; } = 24;

    /// <summary>BR-USER-LIMIT-03: cap tổng heldBalance / user (≤ 1.000.000 theo §13).</summary>
    public long MaxTotalDepositPerUser { get; set; } = 500_000;

    /// <summary>BR-LOBBY-01a/c: buffer tối thiểu giữa now và recruitmentDeadline.</summary>
    public int RecruitmentDeadlineBufferMinutes { get; set; } = 120;

    /// <summary>BR-REFUND-03: grace period cho phép host hủy 100% không phạt.</summary>
    public int CancellationGraceMinutes { get; set; } = 15;

    /// <summary>BR-DEPOSIT-03: BVC / người mà cafe cấu hình (1 ≤ x ≤ 100).</summary>
    public long DepositRatePerPerson { get; set; } = 5;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual Cafe? Cafe { get; set; }
}