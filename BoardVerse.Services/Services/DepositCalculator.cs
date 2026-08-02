using BoardVerse.Core.Constants;
using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;

namespace BoardVerse.Services.Services;

/// <summary>
/// Tính toán cọc thuần (BR-DEPOSIT-02..04, BR-NEW-01).
/// Stateless, không phụ thuộc DB — chỉ映射 BR §VIII, §IV, §XI.
///
/// Quy tắc:
/// - BR-DEPOSIT-02: baseDeposit = ratePerPerson × maxPlayers; finalDeposit = base × riskMultiplier.
/// - BR-NEW-01: finalDeposit = max(minDeposit(distance), ratePerPerson × maxPlayers × riskMultiplier).
/// - BR-DEPOSIT-03: rate per person ∈ [1, 100] BVC.
/// - BR-DEPOSIT-04 + BR-RISK-03: riskMultiplier ∈ [1.0, 2.0].
/// - BR-NEW-10: cooling-off × 2 riskMultiplier.
/// - BR-NEW-15: timeSlot cố định 4 khung, dùng StartTime để tính scheduledTime.
/// - BR-NEW-11: lobby > 2 ngày cần cafe duyệt.
/// </summary>
public class DepositCalculator
{
    private const int MaxBvcPerPerson = 100;
    private const int MinBvcPerPerson = 1;
    private const int DefaultLeadTimeMinutes = 20;
    private const int BufferTooShortMinutes = 60;
    private const int BufferWarningMinutes = 120;
    private const int MaxDaysInFuture = 7;
    private const decimal MaxRiskMultiplier = 2.0m;
    private const decimal MinRiskMultiplier = 1.0m;

    /// <summary>
    /// Tính quote cho 1 reservation (§21A.2).
    /// Trả về DepositQuoteResult — caller (ReservationService) tự quyết định allow hay throw.
    /// </summary>
    public DepositQuoteResult Calculate(
        ReservationQuoteRequestDto request,
        CafeConfig cafeConfig,
        decimal walletRiskMultiplier,
        bool isCoolingOff,
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

        if (request.MinPlayers < 2)
        {
            throw new ArgumentException(ApiErrorMessages.Reservation.MinPlayersAtLeastTwo);
        }

        var rate = NormalizeRatePerPerson((int)cafeConfig.DepositRatePerPerson);
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

        var minDeposit = distance switch
        {
            DistanceBucket.SameDay => cafeConfig.MinDepositSameDay,
            DistanceBucket.OneDay => cafeConfig.MinDeposit1Day,
            DistanceBucket.TwoDays => cafeConfig.MinDeposit2Days,
            DistanceBucket.ThreeToFourDays => cafeConfig.MinDeposit3To4Days,
            _ => cafeConfig.MinDeposit5To7Days
        };

        var finalMaxPlayers = Math.Min(request.MaxPlayers, maxAllowed);
        if (finalMaxPlayers > cafeConfig.Capacity)
        {
            throw new ArgumentException(ApiErrorMessages.Reservation.MaxPlayersExceedsCafeCapacity(
                finalMaxPlayers, cafeConfig.Capacity));
        }

        var baseDeposit = checked(rate * finalMaxPlayers);

        var effectiveRiskMultiplier = ClampRiskMultiplier(walletRiskMultiplier);
        if (isCoolingOff)
        {
            effectiveRiskMultiplier = Math.Min(MaxRiskMultiplier, effectiveRiskMultiplier * 2m);
        }

        var adjustedDeposit = RoundToBvc(baseDeposit * effectiveRiskMultiplier);
        var minDepositApplied = minDeposit;
        var finalDeposit = Math.Max(minDepositApplied, adjustedDeposit);

        var scheduledTime = request.PlayDate.ToDateTime(CafeSchedule.GetStartTime(request.TimeSlot));
        var recruitmentDeadline = scheduledTime.AddMinutes(-DefaultLeadTimeMinutes);
        var bufferMinutes = (int)Math.Floor((recruitmentDeadline - now).TotalMinutes);

        var requiresCafeApproval = distance switch
        {
            DistanceBucket.TwoDays => finalMaxPlayers > 10 || cafeConfig.RequireApprovalForDistant,
            DistanceBucket.ThreeToFourDays => true,
            DistanceBucket.FiveToSevenDays => true,
            _ => false
        };

        return new DepositQuoteResult
        {
            BaseDeposit = baseDeposit,
            MinDepositApplied = minDepositApplied,
            RiskMultiplier = effectiveRiskMultiplier,
            FinalDeposit = finalDeposit,
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

    private static int NormalizeRatePerPerson(int rate)
    {
        if (rate < MinBvcPerPerson)
        {
            rate = MinBvcPerPerson;
        }

        if (rate > MaxBvcPerPerson)
        {
            rate = MaxBvcPerPerson;
        }

        return rate;
    }

    private static decimal ClampRiskMultiplier(decimal riskMultiplier)
    {
        if (riskMultiplier < MinRiskMultiplier)
        {
            return MinRiskMultiplier;
        }

        return riskMultiplier > MaxRiskMultiplier ? MaxRiskMultiplier : riskMultiplier;
    }

    /// <summary>
    /// Round to BVC integer (1 BVC = 1.000 VND, BR § II.2).
    /// </summary>
    private static long RoundToBvc(decimal value)
    {
        return (long)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}
