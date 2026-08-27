using BoardVerse.Core.Constants;
using BoardVerse.Core.DTOs.Reservation;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Messages;

namespace BoardVerse.Services.Services;

/// <summary>
/// Tính toán cọc theo công thức 20% × cafeBasePrice × maxPlayers.
///
/// Công thức (2026-08-27):
/// - baseDeposit (BVC/người) = round(20% × cafeBasePrice / 1000), floor ≥ 1
/// - finalDeposit (BVC tổng) = baseDeposit × maxPlayers
/// - Ví dụ: cafeBasePrice = 50.000 VND, maxPlayers = 8
///   → baseDeposit = 10 BVC/người, finalDeposit = 80 BVC
/// - BR-DEPOSIT-02 (cập nhật 2026-08-27): cọc tỷ lệ với quy mô nhóm.
/// BR-NEW-15 (2026-08-18): BỎ TimeSlot - dùng preferredStartTime/preferredEndTime.
/// </summary>
public class DepositCalculator
{
    private const int BufferTooShortMinutes = 60;
    private const int BufferWarningMinutes = 120;
    private const int MaxDaysInFuture = 7;

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

        // CÔNG THỨC MỚI (2026-08-27):
        // baseDeposit (BVC/người) = 20% × cafeBasePrice (VND) → quy đổi BVC.
        // finalDeposit (BVC tổng) = baseDeposit × maxPlayers.
        // Ví dụ: cafeBasePrice = 50.000 VND, maxPlayers = 8
        //   depositVndPerPerson = 10.000 VND (= 20% × 50.000)
        //   baseDepositPerPerson = 10 BVC (= 10.000 / 1.000)
        //   finalDeposit = 80 BVC (= 10 × 8)
        // BR-DEPOSIT-02 (đã cập nhật): cọc tỷ lệ với quy mô nhóm.
        var depositVndPerPerson = cafeBasePrice * 0.20m;
        var baseDepositPerPerson = Math.Max(1L, RoundToBvc(depositVndPerPerson / 1000m));
        // [2026-08-27] finalDeposit là field duy nhất trả FE — baseDepositPerPerson chỉ dùng nội bộ để tính finalDeposit.
        var finalDeposit = baseDepositPerPerson * finalMaxPlayers;

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
            // baseDeposit = 20% × basePrice (BVC/người), finalDeposit = baseDeposit × maxPlayers (BVC tổng)
            // [2026-08-27 — bỏ hiển thị] DepositPerPerson/BaseDeposit/DepositPercentage: bỏ gán, default = 0.
            // FE chỉ render FinalDeposit + CafeBasePriceVnd.
            MinDepositApplied = 0, // deprecated (BR-NEW-01 không còn dùng với formula mới)
            RiskMultiplier = 1.0m, // deprecated (RiskMultiplier hiện áp dụng nội bộ)
            FinalDeposit = finalDeposit,
            CafeBasePriceVnd = cafeBasePrice, // raw VND từ Cafe.BasePrice, FE render "Giá vé cơ bản: {CafeBasePriceVnd:N0}đ"
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
