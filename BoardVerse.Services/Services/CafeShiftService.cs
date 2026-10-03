using BoardVerse.Core.DTOs.CafeShift;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Core.Messages;
using BoardVerse.Services.IServices;
using Microsoft.Extensions.Logging;

namespace BoardVerse.Services.Services;

public class CafeShiftService : ICafeShiftService
{
    private readonly ICafeShiftRepository _shiftRepository;
    private readonly ICafeRepository _cafeRepository;
    private readonly ILogger<CafeShiftService> _logger;

    public CafeShiftService(
        ICafeShiftRepository shiftRepository,
        ICafeRepository cafeRepository,
        ILogger<CafeShiftService> logger)
    {
        _shiftRepository = shiftRepository;
        _cafeRepository = cafeRepository;
        _logger = logger;
    }

    /// <summary>
    /// Backward-compatible constructor dùng cho unit tests cũ không pass <c>ILogger</c>.
    /// Production code path sử dụng constructor 3 params phía trên.
    /// </summary>
    public CafeShiftService(ICafeShiftRepository shiftRepository, ICafeRepository cafeRepository)
        : this(shiftRepository, cafeRepository, logger: null!)
    {
    }

    public async Task<CafeShiftResponseDto> OpenShiftAsync(Guid cafeId, Guid userId, decimal openingCashBalance, CancellationToken cancellationToken = default)
    {
        var cafe = await _cafeRepository.GetByIdAsync(cafeId);
        if (cafe == null)
            throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));

        var existingShift = await _shiftRepository.GetCurrentOpenShiftAsync(cafeId);
        if (existingShift != null)
            throw new ConflictException(ApiErrorMessages.CafeShift.ShiftAlreadyOpen(existingShift.Id));

        var shift = new CafeShift
        {
            CafeId = cafeId,
            OpenedByUserId = userId,
            OpenedAt = DateTime.UtcNow,
            OpeningCashBalance = openingCashBalance,
            ClosingCashBalance = 0,
            TotalRevenue = 0,
            TotalSessions = 0,
            Status = ShiftStatus.Open
        };

        await _shiftRepository.AddAsync(shift);
        await _shiftRepository.SaveChangesAsync();

        return MapToDto(shift);
    }

    public async Task<CafeShiftResponseDto> CloseShiftAsync(Guid shiftId, Guid userId, decimal closingCashBalance, CancellationToken cancellationToken = default)
    {
        var shift = await _shiftRepository.GetByIdAsync(shiftId);
        if (shift == null)
            throw new NotFoundException(ApiErrorMessages.CafeShift.ShiftNotFound(shiftId));

        if (shift.Status == ShiftStatus.Closed)
            throw new ConflictException(ApiErrorMessages.CafeShift.ShiftAlreadyClosed(shiftId));

        shift.ClosedByUserId = userId;
        shift.ClosedAt = DateTime.UtcNow;
        shift.ClosingCashBalance = closingCashBalance;
        shift.Status = ShiftStatus.Closed;

        await _shiftRepository.UpdateAsync(shift);
        await _shiftRepository.SaveChangesAsync();

        return MapToDto(shift);
    }

    // P0-Fix-#6: validate caller có quyền xem shift của cafe hay không.
    // Admin có thể xem tất cả. Manager/CafeStaff phải thuộc cafe đó.
    private async Task EnsureCallerCanReadCafeShiftsAsync(Guid cafeId, Guid callerUserId, bool isAdmin)
    {
        if (isAdmin) return;

        // Manager: cafe.ManagerId == callerUserId
        var cafe = await _cafeRepository.GetByIdAsync(cafeId);
        if (cafe == null)
        {
            throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));
        }

        if (cafe.ManagerId == callerUserId)
        {
            return;
        }

        // CafeStaff: có dòng trong CafeStaff với cafeId + staffUserId
        var isStaff = await _cafeRepository.IsStaffMemberExistsAsync(cafeId, callerUserId);
        if (isStaff)
        {
            return;
        }

        throw new ForbiddenException(ApiErrorMessages.Cafe.ManagerForbidden(cafeId));
    }

    public async Task<CafeShiftResponseDto?> GetCurrentShiftAsync(Guid cafeId, Guid callerUserId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await EnsureCallerCanReadCafeShiftsAsync(cafeId, callerUserId, isAdmin);

        var shift = await _shiftRepository.GetCurrentOpenShiftAsync(cafeId);
        return shift == null ? null : MapToDto(shift);
    }

    public async Task<CafeShiftHistoryResponseDto> GetShiftHistoryAsync(Guid cafeId, int page, int pageSize, Guid callerUserId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await EnsureCallerCanReadCafeShiftsAsync(cafeId, callerUserId, isAdmin);

        var shifts = await _shiftRepository.GetHistoryAsync(cafeId, page, pageSize);
        var totalCount = await _shiftRepository.GetHistoryCountAsync(cafeId);

        return new CafeShiftHistoryResponseDto
        {
            Shifts = shifts.Select(MapToDto).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    private static CafeShiftResponseDto MapToDto(CafeShift shift) => new()
    {
        Id = shift.Id,
        CafeId = shift.CafeId,
        OpenedByUserId = shift.OpenedByUserId,
        ClosedByUserId = shift.ClosedByUserId,
        OpenedAt = shift.OpenedAt,
        ClosedAt = shift.ClosedAt,
        OpeningCashBalance = shift.OpeningCashBalance,
        ClosingCashBalance = shift.ClosingCashBalance,
        TotalRevenue = shift.TotalRevenue,
        TotalSessions = shift.TotalSessions,
        Status = shift.Status
    };

    /// <summary>
    /// Cộng doanh thu + 1 phiên vào ca đang mở của quán. [BR-CAFE-SHIFT-01]
    /// Được gọi từ <c>ActiveSessionService.PaySessionCoreAsync</c> (POS pay) và
    /// <c>ActiveSessionService.PlayerPaySessionAsync</c> (player pay BVC) ngay
    /// sau khi <c>ActiveSession.Status = GroupSessionStatus.Paid</c> đã commit.
    ///
    /// Best-effort: nếu quán chưa mở ca (chưa có open shift) thì skip + log warning,
    /// KHÔNG throw — payment vẫn commit thành công. Lý do: doanh thu chỉ là metric
    /// hiển thị cho staff calendar, không được phép fail payment.
    ///
    /// Idempotency: caller (PaySessionCoreAsync) đã có re-check
    /// <c>Status != Unpaid</c> bên trong transaction, nên chỉ session pay đầu tiên
    /// mới đi tới method này. Webhook retry / double-click sẽ fail guard trước đó.
    /// </summary>
    public async Task RecordSessionPaymentAsync(
        Guid cafeId,
        decimal sessionTotalAmount,
        CancellationToken cancellationToken = default)
    {
        var openShift = await _shiftRepository.GetCurrentOpenShiftAsync(cafeId, cancellationToken);
        if (openShift == null)
        {
            // Không có ca đang mở → staff chưa mở ca hoặc đã đóng. Không throw để
            // tránh fail payment của khách; chỉ log để admin biết reconcile.
            _logger.LogWarning(
                "RecordSessionPayment: cafe {CafeId} has no open shift, skipping revenue update for amount {Amount}",
                cafeId, sessionTotalAmount);
            return;
        }

        // Capture snapshot trước khi mutate để log diff.
        var prevRevenue = openShift.TotalRevenue;
        var prevSessions = openShift.TotalSessions;

        openShift.TotalRevenue += sessionTotalAmount;
        openShift.TotalSessions += 1;

        await _shiftRepository.UpdateAsync(openShift, cancellationToken);
        await _shiftRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "RecordSessionPayment: shift {ShiftId} totals updated. Revenue {Prev} → {Next} (+{Delta}); Sessions {PrevSessions} → {NextSessions} (+1)",
            openShift.Id, prevRevenue, openShift.TotalRevenue, sessionTotalAmount, prevSessions, openShift.TotalSessions);
    }

    /// <summary>
    /// BR-CAFE-SHIFT-01 reconciliation: tính lại <c>TotalRevenue</c> + <c>TotalSessions</c>
    /// cho ca đã cho bằng cách SUM TotalAmount + COUNT các ActiveSession đã Paid (Status=3)
    /// thuộc quán trong cửa sổ [shift.OpenedAt, shift.ClosedAt ?? now]. Ghi đè totals
    /// hiện tại với giá trị tính lại — KHÔNG đụng OpeningCashBalance, ClosingCashBalance,
    /// ClosedAt, ClosedByUserId.
    ///
    /// Use cases:
    ///   1. Backfill: ca đã Open trước khi áp dụng fix <c>RecordSessionPaymentAsync</c> → totals
    ///      bị lệch. Admin gọi endpoint này để tính lại.
    ///   2. Drift detection: <c>ShiftDriftDetectionJob</c> chạy hàng giờ so sánh shift totals
    ///      với SUM(ActiveSession.TotalAmount) → nếu khác → auto-reconcile bằng method này.
    ///   3. Idempotency: gọi nhiều lần vẫn cho cùng kết quả (SUM là deterministic).
    /// </summary>
    public async Task<CafeShiftResponseDto> RecalculateShiftTotalsAsync(Guid shiftId, Guid callerUserId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var shift = await _shiftRepository.GetByIdAsync(shiftId, cancellationToken);
        if (shift == null)
        {
            throw new NotFoundException(ApiErrorMessages.CafeShift.ShiftNotFound(shiftId));
        }

        // P0-Fix-#6 (Recalculate): validate ownership — Manager/CafeStaff chỉ recalc shift của cafe mình.
        // Admin bypass. Tương tự EnsureCallerCanReadCafeShiftsAsync (private) nhưng áp dụng được vì
        // ta đã load được shift → biết cafeId. Load cafe theo cafeId (cần cho check ManagerId).
        await EnsureCallerCanMutateCafeShiftsAsync(shift.CafeId, callerUserId, isAdmin, cancellationToken);

        // Window: [OpenedAt, ClosedAt ?? UtcNow]. Inclusive ở cả 2 đầu.
        // Lý do inclusive: PaidAt == OpenedAt thì vẫn tính (edge case session vừa mở ca đã paid ngay).
        // Lý do inclusive end: shift đã đóng, session paid trước khi đóng vẫn tính.
        var windowStart = shift.OpenedAt;
        var windowEnd = shift.ClosedAt ?? DateTime.UtcNow;

        var (paidCount, paidRevenue) = await _shiftRepository.SumPaidSessionsByCafeInRangeAsync(
            shift.CafeId, windowStart, windowEnd, cancellationToken);

        var prevRevenue = shift.TotalRevenue;
        var prevSessions = shift.TotalSessions;

        // Chỉ ghi đè 2 field totals; các field khác (cash balance, status, time) giữ nguyên.
        shift.TotalRevenue = paidRevenue;
        shift.TotalSessions = paidCount;

        await _shiftRepository.UpdateAsync(shift, cancellationToken);
        await _shiftRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "RecalculateShiftTotals: shift {ShiftId} totals recomputed from {PrevRevenue} → {NewRevenue} (count {PrevCount} → {NewCount}) for cafe {CafeId} window [{WindowStart}, {WindowEnd}] (caller {CallerId}, admin {IsAdmin})",
            shift.Id, prevRevenue, shift.TotalRevenue, prevSessions, shift.TotalSessions, shift.CafeId, windowStart, windowEnd, callerUserId, isAdmin);

        return MapToDto(shift);
    }

    /// <summary>
    /// P0-Fix-#6 (Recalculate): validate caller có quyền mutate shift (recalculate) của cafe hay không.
    /// Admin bypass. Manager: cafe.ManagerId == callerUserId. CafeStaff: có dòng trong CafeStaff.
    /// Tách riêng với <c>EnsureCallerCanReadCafeShiftsAsync</c> để trong tương lai có thể nới 'read' ra rộng hơn
    /// 'recalculate' (vd: Admin/Support có thể đọc nhưng không được recalc).
    /// </summary>
    private async Task EnsureCallerCanMutateCafeShiftsAsync(Guid cafeId, Guid callerUserId, bool isAdmin, CancellationToken cancellationToken)
    {
        if (isAdmin) return;

        var cafe = await _cafeRepository.GetByIdAsync(cafeId, cancellationToken);
        if (cafe == null)
        {
            throw new NotFoundException(ApiErrorMessages.Cafe.NotFound(cafeId));
        }

        if (cafe.ManagerId == callerUserId)
        {
            return;
        }

        var isStaff = await _cafeRepository.IsStaffMemberExistsAsync(cafeId, callerUserId);
        if (isStaff)
        {
            return;
        }

        throw new ForbiddenException(ApiErrorMessages.Cafe.ManagerForbidden(cafeId));
    }
}
