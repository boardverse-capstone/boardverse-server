using System.Text.RegularExpressions;

namespace BoardVerse.Core.Helpers;

/// <summary>
/// BR mới (§21A.7): POS scan QR có thể là `ReservationCode` (8-char alphanumeric uppercase)
/// hoặc `BookingCode` cũ (`BV{N}`). Helper phân biệt để route đúng flow.
///
/// Quy ước:
/// - ReservationCode: 8 ký tự alphanumeric uppercase (Base32-style).
///   Pattern: ^[A-Z2-9]{8}$ (loại 0/1/I/O để tránh nhầm).
/// - BookingCode cũ: "BV" + 8 chữ số (OrderId).
///   Pattern: ^BV\d{8}$.
///
/// Nếu không match 2 pattern trên → trả Reservation (để fail ra 404 rõ ràng).
/// </summary>
public static class ReservationCodeDetector
{
    private static readonly Regex ReservationPattern = new(@"^[A-Z2-9]{8}$", RegexOptions.Compiled);
    private static readonly Regex BookingPattern = new(@"^BV\d{8}$", RegexOptions.Compiled);

    public enum CodeType
    {
        Unknown = 0,
        Reservation = 1,
        BookingLegacy = 2
    }

    public static CodeType Detect(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return CodeType.Unknown;
        }

        var normalized = code.Trim().ToUpperInvariant();
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
}
