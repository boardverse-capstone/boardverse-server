using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities
{
    /// <summary>
    /// Transaction từ payment gateway.
    /// </summary>
    public class Transaction
    {
        public Guid Id { get; set; }

        // === Payer / Payee Context ===
        public Guid? UserId { get; set; }
        public Guid? CafeId { get; set; }

        // === Amount ===
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "VND";

        // === Gateway ===
        public string Gateway { get; set; } = string.Empty;
        public string? GatewayTransactionId { get; set; }
        public string? GatewayResponseCode { get; set; }
        public string? GatewayResponseMessage { get; set; }

        // === State ===
        public TransactionStatus Status { get; set; } = TransactionStatus.Pending;

        // === Type ===
        public TransactionType Type { get; set; }

        // === Master Account Flow ===
        public TransactionDirection Direction { get; set; }
        public string? FromAccount { get; set; }
        public string? ToAccount { get; set; }
        public string? Notes { get; set; }

        // === Timestamps ===
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }

        // === Navigation ===
        public virtual User? User { get; set; }
        public virtual Cafe? Cafe { get; set; }
    }
}
