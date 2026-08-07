using System.Text.RegularExpressions;

namespace BoardVerse.Core.Helpers;

/// <summary>
/// BR §21A.7: POS scan QR có thể là 3 loại mã:
/// - ReservationCode (8-char alphanumeric, exclude 0/1/I/O) — player cầm QR của reservation.
/// - BookingCode "BV{N}" (10-char) — flow VND cũ, backward compat.
/// - PosToken (16-char alphanumeric, exclude 0/1/I/O) — QR POS hiển thị cho player scan (token mới).
///
/// Helper phân biệt để route đúng flow:
/// - 8-char → ReservationService.CheckInAsync (BVC flow).
/// - 10-char BV{N} → BookingDeposit flow (legacy).
/// - 16-char → PlayerCheckInService (POS QR 2-chiều).
///
/// Nếu không match 3 pattern → trả Unknown để fail ra 404 rõ ràng.
/// </summary>
public static class ReservationCodeDetector
{
    private static readonly Regex ReservationPattern = new(@"^[A-Z2-9]{8}$", RegexOptions.Compiled);
    private static readonly Regex BookingPattern = new(@"^BV\d{8}$", RegexOptions.Compiled);
    private static readonly Regex PosTokenPattern = new(@"^[A-Z2-9]{16}$", RegexOptions.Compiled);

    public enum CodeType
    {
        Unknown = 0,
        Reservation = 1,
        BookingLegacy = 2,
        PosToken = 3
    }

    public static CodeType Detect(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return CodeType.Unknown;
        }

        var normalized = code.Trim().ToUpperInvariant();
        if (PosTokenPattern.IsMatch(normalized))
        {
            return CodeType.PosToken;
        }
        if (ReservationPattern.IsMatch(normalized))
        {
            return CodeType.Reservation;
        }
        if (BookingPattern.IsMatch(normalized))
        {
            return CodeType.BookingLegacy;
        }
        return CodeType.Unknown;
    }

    public static bool IsReservationCode(string? code)
        => Detect(code) == CodeType.Reservation;

    public static bool IsPosToken(string? code)
        => Detect(code) == CodeType.PosToken;
}
