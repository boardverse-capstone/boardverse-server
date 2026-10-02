namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái xử lý nợ của member trong force-close flow (M2/C2.16).
/// Track trong <c>MemberDebt</c> entity.
/// (2026-10-01)
/// </summary>
public enum DebtStatus
{
    /// <summary>Nợ mới tạo, chưa thanh toán / chưa xử lý.</summary>
    Pending = 0,

    /// <summary>Member đã trả hết nợ (cash hoặc Manager write-off).</summary>
    Resolved = 1,

    /// <summary>Manager/Cafe quyết định xóa nợ (write-off), không thu hồi nữa.</summary>
    WrittenOff = 2
}