namespace BoardVerse.Core.Enum
{
    /// <summary>
    /// Player tier được map từ KarmaPoints (xem <see cref="BoardVerse.Core.DTOs.User.KarmaLeaderboardEntryDto.GamerTier"/>).
    /// Tier mới chỉ cần thêm giá trị cuối enum — không reorder, không xóa giá trị cũ
    /// (vì <c>HasConversion&lt;string&gt;()</c> + <c>HasMaxLength(50)</c> trong BoardVerseDbContext
    /// lưu chuỗi, migration cũ vẫn đọc được dữ liệu cũ).
    /// </summary>
    public enum GamerTier
    {
        Bronze = 0,
        Silver = 1,
        Gold = 2,
        Platinum = 3,
        Diamond = 4,
        Master = 5,
        Grandmaster = 6
    }
}