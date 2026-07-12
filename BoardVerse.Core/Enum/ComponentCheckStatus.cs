namespace BoardVerse.Core.Enum
{
    /// <summary>
    /// Trạng thái kiểm kê linh kiện game.
    /// BR-12: Kiểm kê trung gian bắt buộc trước khi xuất hóa đơn.
    /// </summary>
    public enum ComponentCheckStatus
    {
        /// <summary>Chưa kiểm tra.</summary>
        NotChecked = 0,

        /// <summary>Đã kiểm tra - đủ linh kiện.</summary>
        Verified = 1,

        /// <summary>Đã kiểm tra - thiếu linh kiện.</summary>
        MissingComponents = 2
    }
}
