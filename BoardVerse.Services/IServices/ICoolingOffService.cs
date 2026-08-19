namespace BoardVerse.Services.IServices;

/// <summary>
/// BR-NEW-10 + BR-RISK-03/04 — Cooling-off background service.
///
/// Quản lý vòng đời <c>Wallet.IsCoolingOff</c> + <c>CoolingOffExpiresAt</c>:
/// <list type="bullet">
///   <item><description>Detect signals: 3 lobby <c>timeoutFailed</c> / <c>hostCancelled</c> liên tiếp trong 7 ngày, hoặc tổng cọc forfeit/no-show &gt; 500k BVC trong 30 ngày.</description></item>
///   <item><description>Auto-activate cooling-off 30 ngày (cọc ×2 theo BR-RISK-03).</description></item>
///   <item><description>Auto-deactivate khi <c>CoolingOffExpiresAt &lt; now</c>.</description></item>
///   <item><description>Escalate nếu user fail trong cooling-off: gia hạn 30 ngày + cọc ×3.</description></item>
/// </list>
/// </summary>
public interface ICoolingOffService
{
    /// <summary>
    /// BR-NEW-10 — Quét tất cả wallet active, detect signals.
    /// Trả về số wallet được activate cooling-off trong tick này.
    /// </summary>
    /// <param name="now">Thời điểm hiện tại (UTC).</param>
    /// <param name="batchSize">Số wallet quét / lần (giới hạn để tránh lock DB).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<int> DetectAndActivateAsync(DateTime now, int batchSize, CancellationToken ct = default);

    /// <summary>
    /// BR-NEW-10 — Tự động deactivate cooling-off đã hết hạn.
    /// Trả về số wallet được deactivate.
    /// </summary>
    /// <param name="now">Thời điểm hiện tại (UTC).</param>
    /// <param name="batchSize">Số wallet quét / lần.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<int> ExpireOverdueAsync(DateTime now, int batchSize, CancellationToken ct = default);

    /// <summary>
    /// BR-RISK-03 — Khi user tiếp tục fail trong cooling-off:
    /// gia hạn 30 ngày + riskMultiplier ×3.
    /// </summary>
    /// <param name="userId">User bị escalate.</param>
    /// <param name="reason">Lý do escalate (từ BR-NEW-10 signal trigger).</param>
    /// <param name="ct">Cancellation token.</param>
    Task EscalateAsync(Guid userId, string reason, CancellationToken ct = default);

    /// <summary>
    /// BR-NEW-10 — Helper detect signals cho 1 user cụ thể.
    /// Dùng cho manual test hoặc admin tool.
    /// Trả về tuple: (timeoutCount7d, cancelCount7d, forfeitAmount30d).
    /// </summary>
    Task<(int TimeoutFailedCount7d, int HostCancelledCount7d, long ForfeitAmount30d)> DetectSignalsAsync(
        Guid userId,
        DateTime now,
        CancellationToken ct = default);

    /// <summary>
    /// BR-NEW-10 §XI.2 — Admin manually extend cooling-off cho 1 user (customer support tool).
    /// </summary>
    /// <param name="adminUserId">Admin user đang thực hiện (ghi audit log).</param>
    /// <param name="targetUserId">User bị extend.</param>
    /// <param name="additionalDays">Số ngày gia hạn thêm (1..90).</param>
    /// <param name="reason">Lý do extend (lưu audit log).</param>
    /// <param name="ct">Cancellation token.</param>
    Task ExtendAsync(Guid adminUserId, Guid targetUserId, int additionalDays, string reason, CancellationToken ct = default);
}
