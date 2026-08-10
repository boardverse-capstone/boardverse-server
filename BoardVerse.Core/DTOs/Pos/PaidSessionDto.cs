using System.Text.Json.Serialization;
using BoardVerse.Core.DTOs.Session;

namespace BoardVerse.Core.DTOs.Pos;

/// <summary>
/// Phiên chơi đã thanh toán xong (Status = PAID).
/// Dùng cho POS end-of-day report và staff review lịch sử.
/// BR-REVENUE-01: doanh thu quán được ghi nhận khi session PAID.
/// </summary>
public class PaidSessionDto
{
    public Guid Id { get; set; }
    public Guid CafeId { get; set; }
    public Guid HostId { get; set; }
    public string HostName { get; set; } = string.Empty;
    public Guid? LobbyId { get; set; }
    public Guid? CafeTableId { get; set; }
    public string TableName { get; set; } = string.Empty;
    public Guid GameTemplateId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime PaidAt { get; set; }
    public int TotalMinutesPlayed { get; set; }

    /// <summary>Tiền giờ chơi (BR-15, BR-16).</summary>
    public decimal Subtotal { get; set; }

    /// <summary>Phí phạt đền bù linh kiện (BR-12).</summary>
    public decimal PenaltyAmount { get; set; }

    /// <summary>Số BVC deposit đã capture về doanh thu quán (BR-REVENUE-01).</summary>
    /// <remarks>
    /// Trong BR-09 mô hình hiện tại = 0 vì deposit không trừ vào hóa đơn.
    /// Field để forward-compat khi BR-22 per-member deposit được activate.
    /// </remarks>
    public decimal DepositAppliedAmount { get; set; }

    /// <summary>Tổng tiền phiên (BR-15 = Subtotal + Penalty).</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Số thành viên tham gia.</summary>
    public int MemberCount { get; set; }

    /// <summary>Trạng thái capture BVC deposit (BR-REVENUE-01).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BvcCaptureStatus? BvcCaptureStatus { get; set; }
}