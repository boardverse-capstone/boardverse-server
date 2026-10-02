using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Session;

/// <summary>
/// M2/C2.16 — Request body cho <c>POST /api/v1/sessions/{sessionId}/force-close</c>.
/// Manager force-close session khi còn unpaid members.
/// docs/design/host-deposit-discount-and-bvc-payment-design.md §C2.16 (Gap #33).
/// (2026-10-01)
/// </summary>
public class ForceCloseRequestDto
{
    /// <summary>
    /// Cách xử lý unpaid members: <c>"MarkNoShow"</c> | <c>"MarkAsDebt"</c> | <c>"CompensationByHost"</c>.
    /// <list type="bullet">
    ///   <item><c>MarkNoShow</c>: member bị phạt, không có bill thanh toán (status = NoShow).</item>
    ///   <item><c>MarkAsDebt</c>: ghi nhận công nợ VND, member trả sau.</item>
    ///   <item><c>CompensationByHost</c>: host cover bill cho unpaid members.</item>
    /// </list>
    /// </summary>
    [Required(ErrorMessage = "UnpaidMemberHandling không được rỗng.")]
    [RegularExpression(
        "^(MarkNoShow|MarkAsDebt|CompensationByHost)$",
        ErrorMessage = "UnpaidMemberHandling phải là 'MarkNoShow' | 'MarkAsDebt' | 'CompensationByHost'.")]
    public string UnpaidMemberHandling { get; set; } = string.Empty;

    /// <summary>
    /// Lý do force-close (free-form, log cho audit). Max 500 ký tự.
    /// </summary>
    [Required(ErrorMessage = "Lý do force-close không được rỗng.")]
    [MinLength(5, ErrorMessage = "Lý do force-close phải có ít nhất 5 ký tự.")]
    [MaxLength(500, ErrorMessage = "Lý do force-close không được vượt quá 500 ký tự.")]
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Cho phép unpaid members thanh toán sau (late payment).
    /// <c>true</c> (mặc định): session.Status = UnpaidForced, members vẫn có thể pay sau đó.
    /// <c>false</c>: nếu vẫn còn unpaid sau khi apply handling → throw 409.
    /// </summary>
    public bool AllowLatePayment { get; set; } = true;
}

/// <summary>
/// Response trả về sau force-close — báo cáo kết quả và danh sách unpaid members đã xử lý.
/// </summary>
public class ForceCloseResponseDto
{
    /// <summary>Session đã force-close.</summary>
    public Guid SessionId { get; set; }

    /// <summary>Status cuối cùng của session (Paid nếu hết unpaid, UnpaidForced nếu còn).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Thời điểm force-close.</summary>
    public DateTime ForceClosedAt { get; set; }

    /// <summary>Cách xử lý unpaid members đã áp dụng.</summary>
    public string UnpaidHandling { get; set; } = string.Empty;

    /// <summary>Số lượng unpaid members đã xử lý.</summary>
    public int UnpaidMemberCount { get; set; }

    /// <summary>Danh sách unpaid members đã xử lý (kèm handling).</summary>
    public List<UnpaidMemberDto> UnpaidMembers { get; set; } = [];

    /// <summary>ID của ForceCloseAuditLog row insert (cho audit/trace).</summary>
    public Guid AuditLogId { get; set; }
}

/// <summary>
/// Chi tiết 1 unpaid member đã xử lý trong force-close.
/// </summary>
public class UnpaidMemberDto
{
    public Guid MemberId { get; set; }

    /// <summary>Tên hiển thị (Username hoặc GuestDisplayName).</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Số tiền còn nợ (Subtotal + Penalty - DepositAppliedAmount).</summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Handling applied cho member này:
    /// <c>"MarkedNoShow"</c> | <c>"MarkedAsDebt"</c> | <c>"CoveredByHost"</c>.
    /// </summary>
    public string Handling { get; set; } = string.Empty;

    /// <summary>DebtId (nếu handling = MarkedAsDebt), null nếu các handling khác.</summary>
    public Guid? DebtId { get; set; }
}