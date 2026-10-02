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

    /// <summary>
    /// [2026-10-02 OBSOLETE] BR-NEW-01 cũ: minimum deposit BVC cho lobby cùng ngày.
    /// Công thức cọc mới không áp dụng minDepositByDistance — giữ field cho backward compat DB.
    /// </summary>
    [Obsolete("2026-10-02: bỏ minDepositByDistance floor. Field giữ cho backward compat DB.")]
    public long MinDepositSameDay { get; set; } = 50;

    /// <summary>[2026-10-02 OBSOLETE] BR-NEW-01 cũ.</summary>
    [Obsolete("2026-10-02: bỏ minDepositByDistance floor. Field giữ cho backward compat DB.")]
    public long MinDeposit1Day { get; set; } = 50;

    /// <summary>[2026-10-02 OBSOLETE] BR-NEW-01 cũ.</summary>
    [Obsolete("2026-10-02: bỏ minDepositByDistance floor. Field giữ cho backward compat DB.")]
    public long MinDeposit2Days { get; set; } = 100;

    /// <summary>[2026-10-02 OBSOLETE] BR-NEW-01 cũ.</summary>
    [Obsolete("2026-10-02: bỏ minDepositByDistance floor. Field giữ cho backward compat DB.")]
    public long MinDeposit3To4Days { get; set; } = 150;

    /// <summary>[2026-10-02 OBSOLETE] BR-NEW-01 cũ.</summary>
    [Obsolete("2026-10-02: bỏ minDepositByDistance floor. Field giữ cho backward compat DB.")]
    public long MinDeposit5To7Days { get; set; } = 200;

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

    /// <summary>
    /// [2026-10-02 OBSOLETE] BR-DEPOSIT-03 cũ: BVC / người mà cafe cấu hình.
    /// Công thức cọc mới (2026-10-02) suy ra trực tiếp từ cafeBasePrice × 20% / 1000,
    /// không còn phụ thuộc field này. Giữ lại để backward compat DB / DTO.
    /// </summary>
    [Obsolete("2026-10-02: cọc suy ra từ cafeBasePrice × 20%. Field không còn ảnh hưởng tính toán.")]
    public long DepositRatePerPerson { get; set; } = 5;

    /// <summary>
    /// [2026-10-02 OBSOLETE] BR-DEPOSIT-03 cũ: min BVC / người.
    /// Field không còn dùng trong công thức cọc mới (2026-10-02).
    /// </summary>
    [Obsolete("2026-10-02: không còn dùng clamp [Min,Max]. Field giữ cho backward compat.")]
    public long MinDepositRatePerPerson { get; set; } = 1;

    /// <summary>
    /// [2026-10-02] BR-02/BR-03 cũ: floor + ceiling BVC / người.
    /// Field không còn dùng trong công thức cọc mới (2026-10-02).
    /// </summary>
    [Obsolete("2026-10-02: không còn dùng clamp [Min,Max]. Field giữ cho backward compat.")]
    public long MaxDepositRatePerPerson { get; set; } = 100;

    /// <summary>
    /// 2026-10-02: Phần trăm giá vé cơ bản quy đổi thành cọc.
    /// BR-02: cho phép cafe cấu hình (mặc định 0.20 = 20%).
    /// Stakeholder confirm 2026-03: 20% là con số cân bằng giữa "không quá rẻ để spam" và
    /// "không quá đắt để user từ bỏ".
    /// Range an toàn hệ thống: 0.10 ≤ value ≤ 0.50 (BR-03 cap 50%).
    /// </summary>
    public decimal DepositPercentageOfBasePrice { get; set; } = 0.20m;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual Cafe? Cafe { get; set; }
}