using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities
{
    /// <summary>
    /// Tài khoản SePay - dùng chung cho Master Account và per-cafe accounts.
    /// Theo sepay-payment-flow.mdc Section II.1 - Config hierarchy.
    /// </summary>
    public class SePayAccount
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Loại tài khoản: Master (BoardVerse central) hoặc Cafe (per-cafe).
        /// </summary>
        public SePayAccountType AccountType { get; set; }

        /// <summary>
        /// FK đến Cafe. Null nếu là Master Account.
        /// </summary>
        public Guid? CafeId { get; set; }

        /// <summary>
        /// Merchant ID từ SePay dashboard.
        /// </summary>
        public string? MerchantId { get; set; }

        /// <summary>
        /// API Key từ SePay dashboard.
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>
        /// Secret Key dùng để sign webhook.
        /// </summary>
        public string? SecretKey { get; set; }

        /// <summary>
        /// Webhook Token để xác thực webhook từ SePay.
        /// </summary>
        public string? WebhookToken { get; set; }

        /// <summary>
        /// Base URL của SePay API.
        /// Master: https://pay.sepay.vn
        /// Cafe: https://pgapi.sepay.vn
        /// </summary>
        public string? ApiBaseUrl { get; set; }

        /// <summary>
        /// Mã ngân hàng cho VietQR (ví dụ: MBBank, Vietinbank).
        /// </summary>
        public string? BankCode { get; set; }

        /// <summary>
        /// Số tài khoản ngân hàng.
        /// </summary>
        public string? AccountNumber { get; set; }

        /// <summary>
        /// Tên chủ tài khoản.
        /// </summary>
        public string? AccountHolder { get; set; }

        /// <summary>
        /// Redirect URL sau khi thanh toán (SePay redirect về).
        /// </summary>
        public string? ReturnUrl { get; set; }

        /// <summary>
        /// Environment: Production hoặc Sandbox.
        /// </summary>
        public string? Environment { get; set; }

        /// <summary>
        /// Có đang active không.
        /// </summary>
        public bool IsActive { get; set; } = true;

        // === Audit ===
        public Guid? CreatedByUserId { get; set; }
        public Guid? UpdatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // === Navigation ===
        public virtual Cafe? Cafe { get; set; }
    }
}
