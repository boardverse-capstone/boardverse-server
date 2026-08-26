using BoardVerse.Core.Common;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Admin;

/// <summary>
/// W-06: Query cho admin list settlement endpoint.
/// Mặc định Status=null = trả mọi status. Dùng <see cref="Core.Enum.CafeSettlementStatus.Failed"/>
/// cho endpoint chuyên list settlement lỗi cần retry/override.
/// </summary>
public class SettlementListQuery : PaginationParams
{
    public CafeSettlementStatus? Status { get; set; }
    public Guid? CafeId { get; set; }
    public Guid? CafeManagerId { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
}
