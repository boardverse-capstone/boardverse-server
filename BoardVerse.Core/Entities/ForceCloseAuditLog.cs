namespace BoardVerse.Core.Entities
{
    /// <summary>
    /// Audit log cho force-close session (M2/C2.16 — Gap #33).
    /// Mỗi lần Manager force-close session, một row được insert
    /// kèm danh sách unpaid members và cách xử lý.
    /// <para>
    /// Append-only (KHÔNG update/delete) — theo BR §XVII.6.
    /// </para>
    /// (2026-10-01)
    /// </summary>
    public class ForceCloseAuditLog
    {
        public Guid Id { get; set; }

        /// <summary>Session bị force-close.</summary>
        public Guid SessionId { get; set; }

        /// <summary>User (staff/manager) đã trigger force-close.</summary>
        public Guid TriggeredByUserId { get; set; }

        /// <summary>
        /// Cách xử lý unpaid members: "MarkNoShow" | "MarkAsDebt" | "CompensationByHost".
        /// Max 30 ký tự. Có CHECK constraint whitelist ở DB (xem <c>sql/m2_force_close_schema.sql</c>).
        /// </summary>
        public string UnpaidHandling { get; set; } = null!;

        /// <summary>Lý do force-close (free-form, từ request DTO). Max 500 ký tự.</summary>
        public string Reason { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Danh sách unpaid member IDs tại thời điểm force-close.
        /// Dùng Postgres array <c>UUID[]</c> — query bằng ANY(@ids).
        /// </summary>
        public List<Guid> UnpaidMemberIds { get; set; } = new();

        /// <summary>
        /// True nếu force-close đã flip session.Status (Paid hoặc UnpaidForced)
        /// sau khi xử lý unpaid members. False nếu vẫn còn unpaid và chưa flip.
        /// </summary>
        public bool SessionClosedAfter { get; set; }

        // === Navigation ===
        public virtual ActiveSession Session { get; set; } = null!;
        public virtual User TriggeredByUser { get; set; } = null!;
    }
}