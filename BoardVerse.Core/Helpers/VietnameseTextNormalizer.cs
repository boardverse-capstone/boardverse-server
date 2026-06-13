using System.Globalization;
using System.Text;

namespace BoardVerse.Core.Helpers
{
    /// <summary>
    /// Chuẩn hóa chuỗi tiếng Việt để hỗ trợ tìm kiếm gần đúng (bỏ dấu, không phân biệt hoa thường).
    /// </summary>
    public static class VietnameseTextNormalizer
    {
        public static string ToSearchKey(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var normalized = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);

            foreach (var ch in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category == UnicodeCategory.NonSpacingMark)
                    continue;

                builder.Append(ch switch
                {
                    'đ' => 'd',
                    'Đ' => 'd',
                    _ => ch
                });
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
