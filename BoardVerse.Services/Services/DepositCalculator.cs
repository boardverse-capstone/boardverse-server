using BoardVerse.Core.Constants;
using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Messages;

namespace BoardVerse.Services.Services;

/// <summary>
/// Tính toán cọc theo công thức đơn giản (2026-10-02):
///   perPersonBvc  = max(1, floor(cafeBasePrice × cafeDepositPercentage / 1000))    // % × giá vé cơ bản
///   baseDeposit   = perPersonBvc × finalMaxPlayers                // BR-DEPOSIT-02 simplified
///   riskAdjusted  = round(baseDeposit × walletRiskMultiplier)     // BR-DEPOSIT-04 (giữ để chống abuse)
///   finalDeposit  = riskAdjusted                                  // bỏ minDepositByDistance (BR-NEW-01 floor)
///
/// Ví dụ (user-supplied):
///   cafeBasePrice = 10.000 VND, cafeDepositPercentage = 0.20, maxPlayers = 3, riskMultiplier = 1.0
///     → perPersonBvc = max(1, floor(10.000 × 0.20 / 1000)) = max(1, 2) = 2 BVC
///     → baseDeposit = 2 × 3 = 6 BVC
///     → riskAdjusted = 6 × 1.0 = 6
///     → finalDeposit = 6 BVC
///
/// Lưu ý: CafeConfig.DepositRatePerPerson / Min/MaxDepositRatePerPerson / MinDeposit*Days
/// không còn ảnh hưởng đến cọc nữa (đánh dấu [Obsolete] trong entity).
/// Cọc chỉ phụ thuộc vào (cafeBasePrice, cafeDepositPercentage, maxPlayers, riskMultiplier).
/// </summary>
public class DepositCalculator
{
    private const int BufferTooShortMinutes = 60;
    private const int BufferWarningMinutes = 120;
    private const int MaxDaysInFuture = 7;
    private const int BvcPerVnd = 1000; // 1 BVC = 1.000 VND (BR §II.2)

    /// <summary>
    /// Phần trăm giá vé cơ bản mặc định khi <see cref="CafeConfig.DepositPercentageOfBasePrice"/> = 0/unset.
    /// 2026-10-02: 20% là con số cân bằng giữa "không quá rẻ để spam" và "không quá đắt để user từ bỏ" (gap 1.7).
    /// </summary>
    public const decimal DefaultDepositPercentOfBasePrice = 0.20m;

    /// <summary>
    /// Giới hạn dưới BR-03: % cọc tối thiểu 10% (đủ lớn để chống spam).
    /// </summary>
    public const decimal MinDepositPercentOfBasePrice = 0.10m;

    /// <summary>
    /// Giới hạn trên BR-03: % cọc tối đa 50% giá vé cơ bản (BR-03 cũ).
    /// </summary>
    public const decimal MaxDepositPercentOfBasePrice = 0.50m;

    /// <summary>
    /// Tính quote cho 1 reservation (§21A.2).
    /// Trả về DepositQuoteResult — caller (ReservationService) tự quyết định allow hay throw.
    ///
    /// <paramref name="cafeDepositPercentage"/>: % cọc theo giá vé cơ bản (gap 1.2).
    /// Lấy từ <see cref="CafeConfig.DepositPercentageOfBasePrice"/> (mặc định 0.20 = 20% nếu 0/unset).
    /// Bị clamp [0.10, 0.50] theo BR-03 (giới hạn an toàn hệ thống).
    ///
    /// <paramref name="isCoolingOff"/>: hiện no-op — multiplier (×2 khi cooling-off) đã được
    /// áp dụng sẵn tại <paramref name="walletRiskMultiplier"/> bởi <see cref="WalletService"/>
    /// / <see cref="CoolingOffService"/>. Parameter giữ để backward-compat với callers cũ và
    /// để dễ debug "lobby cooling-off ×2" (gap 1.6).
    /// </summary>
    public DepositQuoteResult Calculate(
        ReservationQuoteRequestDto request,
        CafeConfig cafeConfig,
        decimal cafeBasePrice,
        decimal walletRiskMultiplier,
        bool isCoolingOff,
        bool isPrivateLobby,
        DateTime now)
    {
        if (request.PlayDate < DateOnly.FromDateTime(now.Date))
        {
            throw new ArgumentException(ApiErrorMessages.Reservation.PlayDateOutOfRange(MaxDaysInFuture));
        }

        if (request.MaxPlayers < request.MinPlayers)
        {
            throw new ArgumentException(ApiErrorMessages.Reservation.MinGreaterThanMax(request.MinPlayers, request.MaxPlayers));
        }

        if (request.MinPlayers < 1)
        {
            throw new ArgumentException(ApiErrorMessages.Reservation.MinPlayersAtLeastTwo);
        }

        var distance = MapDistanceBucket(request.PlayDate, now);
        var maxAllowed = (cafeConfig.MaxPlayersPerLobbySameDay, cafeConfig.MaxPlayersPerLobby1Day,
                                  cafeConfig.MaxPlayersPerLobby2Days, cafeConfig.MaxPlayersPerLobby3To4Days,
                                  cafeConfig.MaxPlayersPerLobby5To7Days) switch
        {
            var t when distance == DistanceBucket.SameDay => cafeConfig.MaxPlayersPerLobbySameDay,
            var t when distance == DistanceBucket.OneDay => cafeConfig.MaxPlayersPerLobby1Day,
            var t when distance == DistanceBucket.TwoDays => cafeConfig.MaxPlayersPerLobby2Days,
            var t when distance == DistanceBucket.ThreeToFourDays => cafeConfig.MaxPlayersPerLobby3To4Days,
            _ => cafeConfig.MaxPlayersPerLobby5To7Days
        };

        var finalMaxPlayers = Math.Min(request.MaxPlayers, maxAllowed);
        if (finalMaxPlayers > cafeConfig.Capacity)
        {
            throw new ArgumentException(ApiErrorMessages.Reservation.MaxPlayersExceedsCafeCapacity(
                finalMaxPlayers, cafeConfig.Capacity));
        }

        // ===== Công thức đơn giản (2026-10-02) =====

        // 0. Resolve % cọc theo giá vé cơ bản (gap 1.2).
        //    Lấy từ CafeConfig.DepositPercentageOfBasePrice (default 0.20).
        //    Clamp [0.10, 0.50] theo BR-03 cap 50% (an toàn hệ thống).
        //    Nếu cafeConfig.DepositPercentageOfBasePrice <= 0 → dùng default 0.20.
        var rawDepositPercent = cafeConfig.DepositPercentageOfBasePrice > 0
            ? cafeConfig.DepositPercentageOfBasePrice
            : DefaultDepositPercentOfBasePrice;
        var depositPercent = Math.Clamp(rawDepositPercent,
            MinDepositPercentOfBasePrice, MaxDepositPercentOfBasePrice);

        // 1. perPersonBvc = max(1, floor(cafeBasePrice × depositPercent / 1000)).
        //    - depositPercent × basePrice quy đổi sang BVC (1 BVC = 1.000 VND).
        //    - floor để giữ BVC nguyên (BR §II.2: "Nguyên - không tạo 0.5 / 1.25 BVC").
        //    - max(1) để tránh perPersonBvc = 0 khi basePrice quá thấp (< 5.000 VND).
        //    - Bỏ BR-03 cap 50% ngay tại đây (đã clamp ở trên), BR-DEPOSIT-03 clamp [1,100] — không cần nữa.
        //    - Bỏ CafeConfig.DepositRatePerPerson — cọc suy ra trực tiếp từ basePrice × depositPercent.
        var rawPerPersonBvc = cafeBasePrice > 0
            ? (long)Math.Floor(cafeBasePrice * depositPercent / BvcPerVnd)
            : 0L;
        var perPersonBvc = Math.Max(1L, rawPerPersonBvc);

        // 2. baseDeposit = perPersonBvc × finalMaxPlayers (BR-DEPOSIT-02 simplified).
        var baseDeposit = perPersonBvc * finalMaxPlayers;

        // 3. riskAdjusted = baseDeposit × walletRiskMultiplier (BR-RISK-03).
        //    2026-10-02: Cooling-off KHÔNG còn nhân RiskMultiplier lên cọc (đơn giản hóa theo yêu cầu).
        //      - BR-NEW-10 §XI.2 "cọc ×2" và "cọc ×3" đã bỏ.
        //      - CoolingOffService.ActivateCoolingOffAsync KHÔNG set RiskMultiplier nữa.
        //      - CoolingOffService.EscalateAsync KHÔNG nhân RiskMultiplier nữa.
        //    → walletRiskMultiplier đến từ risk score recompute job (BR-RISK-03 mapping 1.0 + riskScore/100).
        //    → isCoolingOff parameter không còn ảnh hưởng cọc, chỉ giữ cho debug/audit.
        var riskAdjusted = (long)Math.Round(
            baseDeposit * (double)walletRiskMultiplier,
            MidpointRounding.AwayFromZero);

        // 4. finalDeposit = riskAdjusted (bỏ BR-NEW-01 minDepositByDistance — không còn áp dụng).
        var finalDeposit = riskAdjusted;

        // Calculate buffer từ preferredStartTime
        var scheduledTime = request.PlayDate.ToDateTime(request.PreferredStartTime);
        const int DefaultLeadTimeMinutes = 20;
        var recruitmentDeadline = scheduledTime.AddMinutes(-DefaultLeadTimeMinutes);
        var bufferMinutes = (int)Math.Floor((recruitmentDeadline - now).TotalMinutes);

        // BR-NEW-11: Private lobby không cần cafe duyệt
        var requiresCafeApproval = !isPrivateLobby && (distance switch
        {
            DistanceBucket.TwoDays => finalMaxPlayers > 10 || cafeConfig.RequireApprovalForDistant,
            DistanceBucket.ThreeToFourDays => true,
            DistanceBucket.FiveToSevenDays => true,
            _ => false
        });

        return new DepositQuoteResult
        {
            // 2026-10-02: perPersonBvc = max(1, floor(basePrice × depositPercent / 1000)).
            DepositPerPerson = perPersonBvc,
            // 2026-10-02: baseDeposit = perPersonBvc × finalMaxPlayers.
            BaseDeposit = baseDeposit,
            // 2026-10-02: minDepositByDistance đã bỏ, giữ field = 0 để backward compat FE.
            MinDepositApplied = 0,
            // BR-DEPOSIT-04/BR-NEW-10: riskMultiplier từ wallet (1.0..2.0 cooling-off ×2, 3.0 là ×3 escalate).
            RiskMultiplier = walletRiskMultiplier,
            // finalDeposit = baseDeposit × riskMultiplier.
            FinalDeposit = finalDeposit,
            // Raw VND từ Cafe.BasePrice, FE render "Giá vé cơ bản: {CafeBasePriceVnd:N0}đ".
            CafeBasePriceVnd = cafeBasePrice,
            // 2026-10-02 (gap 1.2): % cọc đọc từ CafeConfig.DepositPercentageOfBasePrice, clamp [0.10, 0.50].
            // Default 0.20 nếu config = 0. Stakeholder confirm 2026-03 (gap 1.7).
            DepositPercentage = depositPercent,
            Distance = distance,
            MaxPlayersApplied = finalMaxPlayers,
            BufferMinutes = bufferMinutes,
            BufferWarning = bufferMinutes is >= BufferTooShortMinutes and < BufferWarningMinutes,
            RequiresCafeApproval = requiresCafeApproval
        };
    }

    /// <summary>
    /// BR-LOBBY-01a/b/c: Validate buffer. Trả về tuple (ok, warning).
    /// Caller throw nếu !ok.
    /// </summary>
    public static (bool IsAllowed, bool NeedsWarning) EvaluateBuffer(int bufferMinutes)
    {
        if (bufferMinutes < BufferTooShortMinutes)
        {
            return (false, false);
        }

        if (bufferMinutes < BufferWarningMinutes)
        {
            return (true, true);
        }

        return (true, false);
    }

    /// <summary>
    /// Tính số ngày từ now → playDate.
    /// </summary>
    public static int GetDaysInFuture(DateOnly playDate, DateTime now)
    {
        var today = DateOnly.FromDateTime(now.Date);
        return playDate.DayNumber - today.DayNumber;
    }

    /// <summary>
    /// Mapping BR-NEW-01 §VIII.
    /// </summary>
    public static DistanceBucket MapDistanceBucket(DateOnly playDate, DateTime now)
    {
        var days = GetDaysInFuture(playDate, now);
        return days switch
        {
            < 0 => DistanceBucket.OutOfRange,
            0 => DistanceBucket.SameDay,
            1 => DistanceBucket.OneDay,
            2 => DistanceBucket.TwoDays,
            3 or 4 => DistanceBucket.ThreeToFourDays,
            >= 5 and <= MaxDaysInFuture => DistanceBucket.FiveToSevenDays,
            _ => DistanceBucket.OutOfRange
        };
    }

    /// <summary>
    /// Round to BVC integer (1 BVC = 1.000 VND, BR § II.2).
    /// </summary>
    private static long RoundToBvc(decimal value)
    {
        return (long)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}
