using BoardVerse.Core.DTOs.CafeShift;
using BoardVerse.Core.Entities;

using System.Threading;
namespace BoardVerse.Services.IServices;

public interface ICafeShiftService
{
    Task<CafeShiftResponseDto> OpenShiftAsync(Guid cafeId, Guid userId, decimal openingCashBalance, CancellationToken cancellationToken = default);
    Task<CafeShiftResponseDto> CloseShiftAsync(Guid shiftId, Guid userId, decimal closingCashBalance, CancellationToken cancellationToken = default);
    // P0-Fix-#6: truyền callerUserId để service validate ownership (Manager/CafeStaff phải thuộc cafe).
    Task<CafeShiftResponseDto?> GetCurrentShiftAsync(Guid cafeId, Guid callerUserId, bool isAdmin, CancellationToken cancellationToken = default);
    Task<CafeShiftHistoryResponseDto> GetShiftHistoryAsync(Guid cafeId, int page, int pageSize, Guid callerUserId, bool isAdmin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cộng doanh thu + 1 phiên vào ca đang mở của quán. Được gọi real-time mỗi khi
    /// một ActiveSession chuyển sang <c>Paid</c> (POS pay hoặc player pay qua BVC).
    /// Best-effort: nếu quán chưa mở ca (chưa có open shift) thì skip + log warning,
    /// KHÔNG throw — payment vẫn commit thành công. Nếu ca đã đóng, bỏ qua (doanh thu
    /// đã được chốt tại thời điểm close). [BR-CAFE-SHIFT-01 — real-time totalRevenue/totalSessions]
    /// </summary>
    /// <param name="cafeId">Mã quán đang thanh toán.</param>
    /// <param name="sessionTotalAmount">Số tiền phiên chơi (VND) cần cộng vào <c>TotalRevenue</c>.</param>
    /// <param name="cancellationToken">Token huỷ.</param>
    Task RecordSessionPaymentAsync(Guid cafeId, decimal sessionTotalAmount, CancellationToken cancellationToken = default);
}
