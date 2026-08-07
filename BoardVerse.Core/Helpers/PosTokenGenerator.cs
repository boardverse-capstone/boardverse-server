using System.Security.Cryptography;

namespace BoardVerse.Core.Helpers;

/// <summary>
/// Sinh token 16 ký tự alphanumeric uppercase cho POS QR check-in (BR §21A.7).
/// Loại 0/1/I/O để tránh nhầm khi in/đọc thủ công.
/// Phân biệt với ReservationCode (8-char) — cùng alphabet nhưng length khác → ReservationCodeDetector.Detect route đúng.
/// </summary>
public static class PosTokenGenerator
{
    private const string Chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // exclude 0/1/I/O
    private const int TokenLength = 16;

    public static string Generate()
    {
        var buffer = new byte[TokenLength];
        RandomNumberGenerator.Fill(buffer);

        var chars = new char[TokenLength];
        for (var i = 0; i < TokenLength; i++)
        {
            chars[i] = Chars[buffer[i] % Chars.Length];
        }
        return new string(chars);
    }
}