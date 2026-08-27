namespace BoardVerse.Core.Helpers;

/// <summary>
/// Parse số bàn từ CafeTable.Name.
/// Name có format "Bàn 5", "Table 5", "Bàn số 5", "T5", "5" → trả về 5.
/// Null/invalid → trả về null.
/// </summary>
public static class TableNumberHelper
{
    /// <summary>
    /// Parse số bàn từ CafeTable.Name.
    /// </summary>
    /// <param name="tableName">CafeTable.Name (VD: "Bàn 5", "Table 5", "T5", "5").</param>
    /// <returns>Số bàn nếu parse thành công, null nếu thất bại.</returns>
    public static int? Parse(string? tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName)) return null;
        var trimmed = tableName.Trim();
        // Strip common prefixes: "Bàn ", "Table ", "Bàn số ", "T"
        var numeric = trimmed
            .Replace("Bàn ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Bàn số ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Table ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("T", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
        if (int.TryParse(numeric, out var result)) return result;
        return null;
    }
}
