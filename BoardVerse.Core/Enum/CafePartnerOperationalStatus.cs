namespace BoardVerse.Core.Enum
{
    public enum CafePartnerOperationalStatus
    {
        DataBlank,
        Active,
        /// <summary>Quán ngừng kinh doanh — không còn hoạt động.</summary>
        Inactive,
        /// <summary>Admin cấm hoạt động do vi phạm chính sách.</summary>
        Banned
    }
}
