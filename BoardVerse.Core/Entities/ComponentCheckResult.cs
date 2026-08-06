using BoardVerse.Core.Messages;

namespace BoardVerse.Core.Entities;

/// <summary>
/// BR-12: Kết quả kiểm kê linh kiện chi tiết theo từng component, lưu vĩnh viễn.
/// Mỗi lần <c>POST /sessions/component-check</c> thành công sẽ insert một bộ dòng
/// (mỗi component template một dòng) vào bảng này — kể cả khi staff bấm
/// <c>markAllValid=true</c> (lúc đó <see cref="ActualQuantity"/> = <see cref="ExpectedQuantity"/>).
/// <para>
/// Mục đích: audit trail cho phép admin phân biệt staff kiểm kê thật vs bấm "tất cả hợp lệ"
/// để ẩu, đồng thời lưu bằng chứng nếu khách khiếu nại.
/// </para>
/// <para>
/// Idempotent qua <c>(ActiveSessionGameId, GameComponentTemplateId)</c>: 1 session game
/// chỉ có tối đa 1 dòng kết quả cho mỗi component. Nếu staff bấm reset → xóa hết dòng cũ
/// rồi insert lại.
/// </para>
/// </summary>
public class ComponentCheckResult
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK: ActiveSessionGame được kiểm kê.</summary>
    public Guid ActiveSessionGameId { get; set; }

    /// <summary>FK: Component template đang kiểm (GameComponentTemplate).</summary>
    public Guid GameComponentTemplateId { get; set; }

    private int _expectedQuantity = 1;
    /// <summary>Số lượng theo template tại thời điểm kiểm (snapshot).</summary>
    public int ExpectedQuantity
    {
        get => _expectedQuantity;
        set
        {
            if (value < 1)
                throw new ArgumentException(ApiErrorMessages.Entity.DefaultQuantityMustBePositive);
            _expectedQuantity = value;
        }
    }

    /// <summary>Số lượng thực tế nhân viên đếm được (hoặc = ExpectedQuantity nếu markAllValid).</summary>
    public int ActualQuantity { get; set; }

    /// <summary>Phí phạt cho linh kiện này (per-unit × số thiếu). 0 nếu đủ hoặc markAllValid.</summary>
    public decimal PenaltyFee { get; set; }

    /// <summary>Staff thực hiện kiểm kê (FK User). Snapshot từ ActiveSessionGame.CheckedByStaffId.</summary>
    public Guid StaffId { get; set; }

    /// <summary>Thời điểm ghi nhận (snapshot từ ActiveSessionGame.CheckedAt).</summary>
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

    // === Navigation ===
    public virtual ActiveSessionGame ActiveSessionGame { get; set; } = null!;
    public virtual GameComponentTemplate GameComponentTemplate { get; set; } = null!;
    public virtual User Staff { get; set; } = null!;
}