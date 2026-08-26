using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Cafe;

/// <summary>
/// Chi tiết đầy đủ về quán cafe cho player/manager.
/// Bổ sung thông tin availability, refund policy, schedule, deposit config.
/// </summary>
public class CafeDetailDto : CafeDto
{
    // === Operational Status (BR-Operational) ===
    /// <summary>Trạng thái vận hành: DataBlank | Active | Inactive | Banned.</summary>
    public string OperationalStatus { get; set; } = "ACTIVE";

    /// <summary>Lý do nếu quán bị Inactive/Banned.</summary>
    public string? OperationalStatusReason { get; set; }

    /// <summary>Quán có đang mở cửa không (dựa vào giờ hiện tại + schedule).</summary>
    public bool IsCurrentlyOpen { get; set; }

    // === BR-18: Deposit Refund Policy ===
    /// <summary>Chính sách hoàn cọc: Full | Partial | None.</summary>
    public string RefundPolicy { get; set; } = "Partial";

    /// <summary>Các bậc hoàn cọc khi Policy=Partial.</summary>
    public List<RefundTierDto>? RefundTiers { get; set; }

    // === BR-DEPOSIT-03 + BR-NEW-01: Deposit defaults ===
    // NOTE: Hard-coded theo BR. Sẽ migrate từ CafeConfigEntity khi có bảng riêng.

    /// <summary>Tỷ lệ cọc theo người (BVC/người). Hard-coded theo BR-DEPOSIT-03 (min 1, max 100, default 10).</summary>
    public int DepositRatePerPerson { get; set; } = 10;

    /// <summary>Mức cọc tối thiểu theo khoảng cách playDate (BVC). Hard-coded theo BR-NEW-01.</summary>
    public CafeMinDepositDto MinDeposit { get; set; } = new();

    // === BR-05: Seat Availability ===
    /// <summary>Tổng số ghế trống hiện tại.</summary>
    public int AvailableSeats { get; set; }

    /// <summary>Số ghế đang được giữ (reserved + holding).</summary>
    public int HeldSeats { get; set; }

    /// <summary>Số ghế đang có người ngồi (in use).</summary>
    public int InUseSeats { get; set; }

    /// <summary>Số ghế trống theo từng timeSlot (nếu có thông tin).</summary>
    public Dictionary<string, int>? AvailableSeatsByTimeSlot { get; set; }

    // === BR-NEW-12: Cafe Configuration Limits (defaults) ===
    // NOTE: Hard-coded theo BR. Sẽ migrate từ CafeConfigEntity khi có bảng riêng.
    /// <summary>Cấu hình hạn mức riêng của cafe (override BR-NEW-01 defaults).</summary>
    public CafeConfigDto CafeConfig { get; set; } = new();

    // === Schedule Overrides ===
    /// <summary>Danh sách override giờ mở cửa cho ngày đặc biệt (lễ, Tết...).</summary>
    public List<CafeScheduleOverrideDto>? ScheduleOverrides { get; set; }

    // === Additional Info ===
    /// <summary>Số lượng bàn đã cấu hình trên POS.</summary>
    public int NumberOfTables { get; set; }

    /// <summary>Số phòng riêng (nếu có).</summary>
    public int NumberOfPrivateRooms { get; set; }

    /// <summary>Số lượng game quán sở hữu.</summary>
    public int NumberOfGamesOwned { get; set; }

    /// <summary>Quán có Game Master hỗ trợ không.</summary>
    public bool HasGameMaster { get; set; }

    /// <summary>Khoảng cách từ user (km) — chỉ có khi truyền lat/lng vào.</summary>
    public double? DistanceKm { get; set; }
}

/// <summary>
/// Mức cọc tối thiểu theo khoảng cách playDate (BR-NEW-01).
/// Hard-coded defaults, sẽ migrate từ CafeConfigEntity khi có schema.
/// </summary>
public class CafeMinDepositDto
{
    /// <summary>Mức cọc tối thiểu khi đặt trong ngày (BVC). BR-NEW-01: 50.000.</summary>
    public int SameDay { get; set; } = 50_000;

    /// <summary>Mức cọc tối thiểu khi đặt 1 ngày sau (BVC). BR-NEW-01: 50.000.</summary>
    public int OneDay { get; set; } = 50_000;

    /// <summary>Mức cọc tối thiểu khi đặt 2 ngày sau (BVC). BR-NEW-01: 100.000.</summary>
    public int TwoDays { get; set; } = 100_000;

    /// <summary>Mức cọc tối thiểu khi đặt 3-4 ngày sau (BVC). BR-NEW-01: 150.000.</summary>
    public int ThreeToFourDays { get; set; } = 150_000;

    /// <summary>Mức cọc tối thiểu khi đặt 5-7 ngày sau (BVC). BR-NEW-01: 200.000.</summary>
    public int FiveToSevenDays { get; set; } = 200_000;
}

/// <summary>
/// Cấu hình hạn mức riêng của cafe (BR-NEW-12).
/// Hard-coded defaults, sẽ migrate từ CafeConfigEntity khi có schema.
/// </summary>
public class CafeConfigDto
{
    /// <summary>Tổng số ghế của quán.</summary>
    public int Capacity { get; set; }

    /// <summary>Tối đa lobby active / user / day. BR-NEW-02.</summary>
    public int MaxLobbiesPerUserPerDay { get; set; } = 1;

    /// <summary>Tối đa người / lobby khi đặt cùng ngày. BR-NEW-01.</summary>
    public int MaxPlayersPerLobbySameDay { get; set; } = 30;

    /// <summary>Tối đa người / lobby khi đặt 1 ngày sau. BR-NEW-01.</summary>
    public int MaxPlayersPerLobby1Day { get; set; } = 20;

    /// <summary>Tối đa người / lobby khi đặt 2 ngày sau. BR-NEW-01.</summary>
    public int MaxPlayersPerLobby2Days { get; set; } = 15;

    /// <summary>Tối đa người / lobby khi đặt 3-4 ngày sau. BR-NEW-01.</summary>
    public int MaxPlayersPerLobby3To4Days { get; set; } = 10;

    /// <summary>Tối đa người / lobby khi đặt 5-7 ngày sau. BR-NEW-01.</summary>
    public int MaxPlayersPerLobby5To7Days { get; set; } = 6;

    /// <summary>Có yêu cầu cafe duyệt lobby public xa (> DistantThresholdDays) không. BR-NEW-11.</summary>
    public bool RequireApprovalForDistant { get; set; } = true;

    /// <summary>Ngưỡng ngày bắt đầu cần cafe duyệt. BR-NEW-11.</summary>
    public int DistantThresholdDays { get; set; } = 2;

    /// <summary>Timeout duyệt lobby (giờ). BR-NEW-11.</summary>
    public int ApprovalTimeoutHours { get; set; } = 24;

    /// <summary>Tổng cọc tối đa / user (BVC). BR-USER-LIMIT-03.</summary>
    public long MaxTotalDepositPerUser { get; set; } = 500_000;

    /// <summary>Buffer tối thiểu từ lúc tạo lobby đến recruitment deadline (phút). BR-LOBBY-01a.</summary>
    public int RecruitmentDeadlineBufferMinutes { get; set; } = 120;

    /// <summary>Grace period hủy lobby (phút). BR-REFUND-03.</summary>
    public int CancellationGraceMinutes { get; set; } = 15;
}

/// <summary>
/// Override giờ mở cửa cho ngày đặc biệt (BR-Schedule).
/// BR-NEW-15 (2026-08-18): BỎ TimeSlot - dùng ApplyDate/OpenTime/CloseTime.
/// </summary>
public class CafeScheduleOverrideDto
{
    /// <summary>Ngày áp dụng override.</summary>
    public DateOnly ApplyDate { get; set; }

    /// <summary>Lý do (VD: "Tết Nguyên Đán 2026").</summary>
    public string? Reason { get; set; }

    /// <summary>Giờ mở cửa override. Null = đóng cửa ngày đó.</summary>
    public TimeOnly? OpenTime { get; set; }

    /// <summary>Giờ đóng cửa override. Null = đóng cửa ngày đó.</summary>
    public TimeOnly? CloseTime { get; set; }

    /// <summary>True = đóng cửa ngày này.</summary>
    public bool IsClosed { get; set; }
}
