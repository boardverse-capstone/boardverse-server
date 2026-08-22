using System.Text.RegularExpressions;

namespace BoardVerse.Core.Helpers;

/// <summary>
/// GAP-R6-WAL-PII Fix: Centralized PII masking cho các DTO fields chứa user input.
/// GDPR / Vietnamese PDPD compliance — không leak email local-part / phone digits vào
/// response payload có thể log/cached.
/// </summary>
public static partial class PiiMasker
{
    /// <summary>Mask email: <c>john.doe@example.com</c> → <c>j***@example.com</c>.</summary>
    [GeneratedRegex(@"([a-zA-Z0-9._%+-]+)@([a-zA-Z0-9.-]+\.[a-zA-Z]{2,})", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    /// <summary>Vietnamese phone: 10-11 digits, optional +84/0 prefix.</summary>
    [GeneratedRegex(@"(\+?84|0)\s?\d{1,2}[\s.-]?\d{3}[\s.-]?\d{3,4}", RegexOptions.Compiled)]
    private static partial Regex PhoneRegex();

    /// <summary>
    /// Mask tất cả email/phone pattern trong 1 string. Trả về input nếu null/empty.
    /// </summary>
    public static string MaskNote(string? note)
    {
        if (string.IsNullOrEmpty(note))
        {
            return string.Empty;
        }

        var masked = EmailRegex().Replace(note, m =>
        {
            var local = m.Groups[1].Value;
            var domain = m.Groups[2].Value;
            var firstChar = local.Length > 0 ? local[0].ToString() : "*";
            return $"{firstChar}***@{domain}";
        });

        masked = PhoneRegex().Replace(masked, m =>
        {
            var prefix = m.Value.Length >= 4 ? m.Value[..4] : m.Value;
            return $"{prefix}***";
        });

        return masked;
    }
}
