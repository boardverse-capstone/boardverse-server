using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Transactional Outbox (BR-REQUIRED §17.5).
/// Event được ghi vào table này trong CÙNG transaction với domain mutation,
/// background worker sẽ poll và publish ra ngoài (SignalR/push/notification).
///
/// Đảm bảo:
/// - DB đã commit → event chắc chắn sẽ được publish (at-least-once).
/// - Nếu publish fail → retry; idempotency phía consumer xử lý trùng.
/// - Không bao giờ mất event dù SignalR/push fail giữa commit và publish.
/// </summary>
public class OutboxEvent
{
    public Guid Id { get; set; }

    /// <summary>Loại event (xem <see cref="OutboxEventType"/>).</summary>
    public OutboxEventType EventType { get; set; }

    /// <summary>JSON payload (chi tiết event). Deserialize ở publisher.</summary>
    public string Payload { get; set; } = "{}";

    /// <summary>Idempotency key (unique). Trùng key → skip publish.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>FK optional — reservation id (nếu liên quan).</summary>
    public Guid? ReservationId { get; set; }

    /// <summary>FK optional — lobby id (nếu liên quan).</summary>
    public Guid? LobbyId { get; set; }

    /// <summary>FK optional — user id (host/member nhận notification).</summary>
    public Guid? UserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Đã publish thành công hay chưa.</summary>
    public bool Processed { get; set; }

    public DateTime? ProcessedAt { get; set; }

    /// <summary>Số lần retry fail. Sau N lần → move sang DLQ.</summary>
    public int RetryCount { get; set; }

    public string? LastError { get; set; }

    /// <summary>Reservation navigation (optional).</summary>
    public virtual Reservation? Reservation { get; set; }
}