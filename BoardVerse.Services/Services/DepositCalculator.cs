using BoardVerse.Core.Constants;
using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Messages;

namespace BoardVerse.Services.Services;

/// <summary>
/// Tính toán cọc theo công thức BR-DEPOSIT-02..04 + BR-NEW-01 + BR-03 (chặn trần 50% × giờ đầu).
///
/// Công thức canonical (BR §IV):
///   perPersonBvc  = clamp(cafeConfig.DepositRatePerPerson,
///                          cafeConfig.MinDepositRatePerPerson,
///                          cafeConfig.MaxDepositRatePerPerson)        // BR-DEPOSIT-03
///   perPersonBvc  = min(perPersonBvc, floor(50% × cafeBasePrice/1000)) // BR-03 (chặn trần)
///   baseDeposit   = perPersonBvc × finalMaxPlayers                     // BR-DEPOSIT-02
///   riskAdjusted  = round(baseDeposit × walletRiskMultiplier)         // BR-DEPOSIT-04 + BR-NEW-10
///   finalDeposit  = max(minDepositByDistance(distance), riskAdjusted) // BR-NEW-01
///
/// Ví dụ:
///   ratePerPerson = 5 BVC, maxPlayers = 8, basePrice = 50.000 VND, riskMultiplier = 1.0
///     → perPersonBvc = min(5, floor(25.000/1000)) = min(5, 25) = 5
///     → baseDeposit = 5 × 8 = 40 BVC
///     → riskAdjusted = 40 × 1.0 = 40
///     → finalDeposit = max(MinDepositSameDay, 40)
/// </summary>
public class DepositCalculator
{
    private const int BufferTooShortMinutes = 60;
    private const int BufferWarningMinutes = 120;
    private const int MaxDaysInFuture = 7;
    private const int BvcPerVnd = 1000; // 1 BVC = 1.000 VND (BR §II.2)
    private const decimal Br03CapPercent = 0.50m; // BR-03: 50% × basePrice

    /// <summary>
    /// Tính quote cho 1 reservation (§21A.2).
    /// Trả về DepositQuoteResult — caller (ReservationService) tự quyết định allow hay throw.
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

        // ===== BR-DEPOSIT-02 (canonical) =====

        // 1. perPersonBvc = cafeConfig.DepositRatePerPerson, clamp [Min, Max] theo BR-DEPOSIT-03.
        var perPersonBvc = Math.Clamp(
            cafeConfig.DepositRatePerPerson,
            cafeConfig.MinDepositRatePerPerson,
            cafeConfig.MaxDepositRatePerPerson);

        // 2. BR-03 (chặn trần): perPersonBvc ≤ floor(50% × cafeBasePrice / 1000).
        //    "Phí đặt cọc ≤ 50% × Mức phí giờ đầu (hoặc Giá vé vào cổng)".
        //    Nếu basePrice <= 0 (chưa cấu hình), BR-03 không áp dụng — giữ ratePerPerson ban đầu.
        decimal br03MaxPerPersonBvc = cafeBasePrice > 0
            ? Math.Floor(cafeBasePrice * Br03CapPercent / BvcPerVnd)
            : perPersonBvc;
        perPersonBvc = Math.Min(perPersonBvc, (long)Math.Max(0, br03MaxPerPersonBvc));

        // 3. baseDeposit = perPersonBvc × finalMaxPlayers (BR-DEPOSIT-02).
        var baseDeposit = perPersonBvc * finalMaxPlayers;

        // 4. riskAdjusted = baseDeposit × walletRiskMultiplier (BR-DEPOSIT-04 + BR-NEW-10 cooling-off ×2).
        var riskAdjusted = (long)Math.Round(
            baseDeposit * (double)walletRiskMultiplier,
            MidpointRounding.AwayFromZero);

        // 5. BR-NEW-01: finalDeposit = max(minDepositByDistance(distance), riskAdjusted).
        var minDepositByDistance = distance switch
        {
            DistanceBucket.SameDay => cafeConfig.MinDepositSameDay,
            DistanceBucket.OneDay => cafeConfig.MinDeposit1Day,
            DistanceBucket.TwoDays => cafeConfig.MinDeposit2Days,
            DistanceBucket.ThreeToFourDays => cafeConfig.MinDeposit3To4Days,
            DistanceBucket.FiveToSevenDays => cafeConfig.MinDeposit5To7Days,
            _ => 0
        };
        var finalDeposit = Math.Max(minDepositByDistance, riskAdjusted);

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
            // BR-DEPOSIT-02/03: perPersonBvc sau khi áp clamp + BR-03 cap.
            DepositPerPerson = perPersonBvc,
            // BR-DEPOSIT-02: baseDeposit = perPersonBvc × finalMaxPlayers (chưa áp riskMultiplier).
            BaseDeposit = baseDeposit,
            // BR-NEW-01: minimum deposit theo khoảng cách playDate (snapshot cho FE debug).
            MinDepositApplied = minDepositByDistance,
            // BR-DEPOSIT-04/BR-NEW-10: riskMultiplier từ wallet (1.0..2.0 cooling-off ×2).
            RiskMultiplier = walletRiskMultiplier,
            // finalDeposit = max(MinDepositApplied, baseDeposit × riskMultiplier) — số BVC phải cọc.
            FinalDeposit = finalDeposit,
            // Raw VND từ Cafe.BasePrice, FE render "Giá vé cơ bản: {CafeBasePriceVnd:N0}đ".
            CafeBasePriceVnd = cafeBasePrice,
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
