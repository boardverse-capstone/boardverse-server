namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái của KarmaShortPlayRecord.
/// </summary>
public enum KarmaRecordStatus
{
    /// <summary>Record đang có hiệu lực.</summary>
    Active = 0,

    /// <summary>Record đã hết hạn (sau 30 ngày không vi phạm).</summary>
    Expired = 1,

    /// <summary>Record đã được xóa bởi admin (appeal upheld).</summary>
    Cleared = 2
}
