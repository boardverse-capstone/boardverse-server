using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.Messages;

namespace BoardVerse.Services.Services;

/// <summary>
/// Validate đầy đủ BR-USER-LIMIT-* + BR-NEW-* cho việc tạo reservation / member join.
/// Stateless, thuần function — ReservationService layer sẽ chịu trách nhiệm lookup DB
/// và throw với message thân thiện nếu fail.
///
/// Quy tắc:
/// - BR-USER-LIMIT-01: max 1 host lobby + 1 member lobby = 2 tổng.
/// - BR-USER-LIMIT-02: không lịch chồng lấn (+30p buffer).
/// - BR-USER-LIMIT-03: cap tổng heldBalance (500k thường / 1M VIP / 200k risk cao).
/// - BR-USER-LIMIT-04: member → không được host lobby khác.
/// - BR-USER-LIMIT-05: host → không được join lobby khác.
/// - BR-NEW-02: 1 lobby active / playDate / user.
/// - BR-NEW-05: tối đa 5 lần tạo/hủy / playDate.
/// - BR-NEW-08: 1 lobby active / playDate+timeSlot / cafe / user.
/// - BR-NEW-10: cooling-off → chỉ tạo lobby trong ngày.
/// - BR-RISK-04: suspended/banned → chặn tạo lobby.
/// - BR-LOBBY-01a/b: buffer &lt; 60 phút từ chối.
/// - BR-NEW: playDate out of [today, today+7] từ chối.
/// </summary>
public class EligibilityValidator
{
    private const int OverlapBufferMinutes = 30;
    private const int MaxCreateOrCancelPerPlayDate = 5;
    private const long MaxTotalDepositRegular = 500_000;
    private const long MaxTotalDepositVip = 1_000_000;
    private const long MaxTotalDepositRisk = 200_000;

    /// <summary>
    /// Validate Host có thể tạo reservation. Throw với message nghiệp vụ nếu fail.
    /// </summary>
    public void ValidateHostCanCreate(
        HostReservationContext context)
    {
        if (context.IsCoolingOff)
        {
            var daysInFuture = DepositCalculator.GetDaysInFuture(context.PlayDate, context.Now);
            if (daysInFuture > 1)
            {
                throw new ForbiddenException(ApiErrorMessages.Reservation.CoolingOffCannotCreateDistantLobby(
                    context.CoolingOffExpiresAt ?? context.Now.AddDays(30)));
            }
        }

        ValidateAccountStatus(context.IsAccountSuspended, context.IsAccountBanned);

        // BR-USER-LIMIT-01: tổng active = host + member ≤ 2.
        // Check tổng trước để message cụ thể (host vs member vs cả hai).
        var totalActiveLobbies = (context.HasActiveHostLobby ? 1 : 0) + (context.HasActiveMemberLobby ? 1 : 0);
        if (totalActiveLobbies >= 2)
        {
            // Đã đạt tối đa 2 lobby → phải ưu tiên thông báo cross-role cho user hiểu.
            if (context.HasActiveMemberLobby)
            {
                throw new ForbiddenException(ApiErrorMessages.Reservation.MemberCannotCreateLobby);
            }
            throw new ForbiddenException(ApiErrorMessages.Reservation.ActiveLobbyHostLimitReached);
        }

        // BR-USER-LIMIT-04: member → không được host lobby khác.
        if (context.HasActiveMemberLobby)
        {
            throw new ForbiddenException(ApiErrorMessages.Reservation.MemberCannotCreateLobby);
        }

        // BR-USER-LIMIT-01: host đã có 1 lobby → không host thêm.
        if (context.HasActiveHostLobby)
        {
            throw new ForbiddenException(ApiErrorMessages.Reservation.ActiveLobbyHostLimitReached);
        }

        if (context.HasOverlapHostLobby)
        {
            var overlapDeadline = context.OverlapOtherDeadline ?? context.RecruitmentDeadline;
            var overlapStart = context.OverlapOtherStart ?? context.Now;
            throw new ConflictException(ApiErrorMessages.Reservation.OverlappingLobbyExists(
                overlapDeadline, overlapStart));
        }

        if (context.HasActiveLobbyOnPlayDate)
        {
            throw new ConflictException(ApiErrorMessages.Reservation.OverlappingLobbyExists(
                context.Now, context.Now));
        }

        if (context.HasActiveLobbyOnCafeSlot)
        {
            throw new ConflictException(ApiErrorMessages.Reservation.SameCafeSlotLobbyAlreadyActive);
        }

        if (context.HostCreateOrCancelCount >= MaxCreateOrCancelPerPlayDate)
        {
            throw new ConflictException(ApiErrorMessages.Reservation.HostCreatesCancelsLimitReached(MaxCreateOrCancelPerPlayDate));
        }

        var cap = ResolveTotalDepositCap(context);
        var projected = context.WalletHeldBalance + context.FinalDeposit;
        if (projected > cap)
        {
            var userType = context.IsVip ? "VIP" : (context.IsRiskMultiplierHigh ? "risk cao" : "thường");
            throw new ConflictException(ApiErrorMessages.Reservation.HeldDepositCapExceeded(
                context.WalletHeldBalance, cap, userType));
        }
    }

    /// <summary>
    /// Validate Member có thể join lobby. Throw với message nghiệp vụ nếu fail.
    /// </summary>
    public void ValidateMemberCanJoin(MemberJoinContext context)
    {
        // BR-RISK-04: Banned → chặn vĩnh viễn. Suspended → chặn tạm thời.
        if (context.IsAccountBanned)
        {
            throw new ForbiddenException(ApiErrorMessages.Reservation.BannedCannotCreateLobby);
        }
        if (context.IsAccountSuspended)
        {
            throw new ForbiddenException(ApiErrorMessages.Reservation.SuspendedCannotCreateLobby);
        }

        // BR-USER-LIMIT-01: tổng active = host + member ≤ 2.
        // Check tổng trước để message cụ thể (host vs member vs cả hai).
        var totalActiveLobbies = (context.HasActiveHostLobby ? 1 : 0) + context.ActiveMemberLobbyCount;
        if (totalActiveLobbies >= 2)
        {
            // Đã đạt tối đa 2 lobby → báo tổng quá (ưu tiên thông báo cross-role).
            if (context.HasActiveHostLobby)
            {
                throw new ForbiddenException(ApiErrorMessages.Reservation.HostCannotJoinLobby);
            }
            throw new ForbiddenException(ApiErrorMessages.Reservation.ActiveLobbyMemberLimitReached);
        }

        // BR-USER-LIMIT-05: host → không được join lobby khác.
        if (context.HasActiveHostLobby)
        {
            throw new ForbiddenException(ApiErrorMessages.Reservation.HostCannotJoinLobby);
        }

        // BR-USER-LIMIT-01: member → không join thêm (đã đếm 1 ở trên).
        if (context.ActiveMemberLobbyCount >= 1)
        {
            throw new ForbiddenException(ApiErrorMessages.Reservation.ActiveLobbyMemberLimitReached);
        }

        if (context.HasOverlapMemberLobby)
        {
            throw new ConflictException(ApiErrorMessages.Reservation.OverlappingLobbyExists(
                context.Now, context.Now));
        }

        if (context.ActiveMemberLobbyOnPlayDateCount >= 1)
        {
            throw new ConflictException(ApiErrorMessages.Reservation.OverlappingLobbyExists(
                context.Now, context.Now));
        }
    }

    private static void ValidateAccountStatus(bool suspended, bool banned)
    {
        // Banned → cấm vĩnh viễn. Suspended → cấm tạm thời.
        // Phải tách rõ để user không nhận nhầm thông báo "cấm vĩnh viễn" khi chỉ bị tạm khóa.
        if (banned)
        {
            throw new ForbiddenException(ApiErrorMessages.Reservation.BannedCannotCreateLobby);
        }
        if (suspended)
        {
            throw new ForbiddenException(ApiErrorMessages.Reservation.SuspendedCannotCreateLobby);
        }
    }

    private static long ResolveTotalDepositCap(HostReservationContext context)
    {
        if (context.IsVip)
        {
            return MaxTotalDepositVip;
        }

        if (context.IsRiskMultiplierHigh)
        {
            return MaxTotalDepositRisk;
        }

        return MaxTotalDepositRegular;
    }
}

/// <summary>
/// Snapshot read-only data cho Host create reservation.
/// ReservationService build struct này từ DB lookup rồi gọi Validator.
/// </summary>
public class HostReservationContext
{
    public Guid HostId { get; set; }
    public Guid CafeId { get; set; }
    public DateOnly PlayDate { get; set; }
    public TimeSlot TimeSlot { get; set; }
    public DateTime RecruitmentDeadline { get; set; }
    public DateTime Now { get; set; }

    // BR-RESV-02: Preferred times dùng cho overlap check chính xác hơn slot window.
    public DateTime PreferredScheduledStart { get; set; }
    public DateTime PreferredScheduledEnd { get; set; }

    public bool IsVip { get; set; }
    public bool IsRiskMultiplierHigh { get; set; }
    public bool IsCoolingOff { get; set; }
    public bool IsAccountSuspended { get; set; }
    public bool IsAccountBanned { get; set; }

    public long WalletHeldBalance { get; set; }
    public long FinalDeposit { get; set; }

    public bool HasActiveHostLobby { get; set; }
    public bool HasActiveMemberLobby { get; set; }
    public bool HasOverlapHostLobby { get; set; }
    public bool HasActiveLobbyOnPlayDate { get; set; }
    public bool HasActiveLobbyOnCafeSlot { get; set; }
    public int HostCreateOrCancelCount { get; set; }

    /// <summary>Để message lỗi overlap informative (deadline / start của lobby khác).</summary>
    public DateTime? OverlapOtherDeadline { get; set; }
    public DateTime? OverlapOtherStart { get; set; }

    /// <summary>Để message lỗi cooling-off informative (BR-NEW-10).</summary>
    public DateTime? CoolingOffExpiresAt { get; set; }
}

/// <summary>
/// Snapshot read-only data cho Member join lobby.
/// </summary>
public class MemberJoinContext
{
    public Guid UserId { get; set; }
    public Guid CafeId { get; set; }
    public DateOnly PlayDate { get; set; }
    public TimeSlot TimeSlot { get; set; }
    public DateTime RecruitmentDeadline { get; set; }
    public DateTime Now { get; set; }

    public bool IsAccountSuspended { get; set; }
    public bool IsAccountBanned { get; set; }

    public bool HasActiveHostLobby { get; set; }
    public bool HasOverlapMemberLobby { get; set; }
    public int ActiveMemberLobbyCount { get; set; }
    public int ActiveMemberLobbyOnPlayDateCount { get; set; }
}
