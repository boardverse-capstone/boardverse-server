namespace BoardVerse.Core.Settings;

/// <summary>
/// Cấu hình cho legacy Booking flow (Flow B).
/// Flow này đã được thay thế bằng Reservation flow (Flow A, BVC wallet).
/// Settings ở đây để gate controller + chạy cleanup job cho dữ liệu cũ.
/// </summary>
public class LegacyBookingSettings
{
    public const string SectionName = "LegacyBooking";

    /// <summary>
    /// Bật/tắt BookingController endpoints. Khi <c>false</c>, mọi endpoint trong
    /// <c>/api/bookings/*</c> trả <c>410 Gone</c> (RFC 8594 Sunset đã tới).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Bật background job dọn dẹp legacy Booking rows quá hạn:
    /// - <c>Status = PendingDeposit</c> + <c>ScheduledStartTime &lt; now - PendingDepositGraceMinutes</c> → <c>NoShow</c>.
    /// - <c>Status = Confirmed</c> + <c>ScheduledStartTime &lt; now - ConfirmedGraceMinutes</c> + chưa check-in → <c>NoShow</c>.
    /// Job chỉ chạy khi <see cref="Enabled"/> = <c>true</c>.
    /// </summary>
    public bool CleanupJobEnabled { get; set; } = true;

    /// <summary>
    /// Chu kỳ job (phút). Mặc định 5 phút — đủ nhanh để không làm phiền POS,
    /// đủ thưa để không query DB liên tục.
    /// </summary>
    public int CleanupIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// Grace cho Booking <c>PendingDeposit</c> quá giờ (phút).
    /// BR-05/BR-06 cũ không định nghĩa nhánh này; chọn 30 phút để khớp grace check-in.
    /// </summary>
    public int PendingDepositGraceMinutes { get; set; } = 30;

    /// <summary>
    /// Grace cho Booking <c>Confirmed</c> quá giờ mà chưa check-in (phút).
    /// Theo BR-06 = 30 phút.
    /// </summary>
    public int ConfirmedGraceMinutes { get; set; } = 30;

    /// <summary>
    /// Batch size cho cleanup query — tránh lock DB quá lâu nếu có nhiều row legacy.
    /// </summary>
    public int CleanupBatchSize { get; set; } = 100;
}