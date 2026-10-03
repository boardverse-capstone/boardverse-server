using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Payment;

public class SePayAccountDto
{
    public Guid Id { get; set; }
    public SePayAccountType AccountType { get; set; }
    public Guid? CafeId { get; set; }
    public string? CafeName { get; set; }
    public string? MerchantId { get; set; }
    public string? ApiBaseUrl { get; set; }
    public string? BankCode { get; set; }
    public string? MaskedAccountNumber { get; set; }
    public string? AccountHolder { get; set; }
    public string? ReturnUrl { get; set; }
    public string? Environment { get; set; }
    public SePayWebhookAuthType WebhookAuthType { get; set; }
    public bool IsActive { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// [ADMIN] Tạo SePay account đầy đủ (Master hoặc Cafe).
/// Manager KHÔNG dùng DTO này — Manager dùng <see cref="CreateCafePaymentAccountRequestDto"/> (4 field, không cần đăng ký SePay).
///
/// Flow khuyến nghị cho Cafe (Cách 2 trong sepay-payment-flow.md):
/// Manager gọi <c>POST /api/sepay-accounts/my-cafe</c> với DTO đơn giản,
/// admin BoardVerse vào SePay dashboard link TK ngân hàng của cafe vào company master.
/// </summary>
public class CreateSePayAccountRequestDto
{
    public SePayAccountType AccountType { get; set; }
    public Guid? CafeId { get; set; }
    public string? MerchantId { get; set; }
    public string? ApiKey { get; set; }
    public string? SecretKey { get; set; }
    public string? WebhookToken { get; set; }
    public SePayWebhookAuthType WebhookAuthType { get; set; } = SePayWebhookAuthType.None;
    public string? ApiBaseUrl { get; set; }
    public string? BankCode { get; set; }
    public string? AccountNumber { get; set; }
    public string? AccountHolder { get; set; }
    public string? ReturnUrl { get; set; }
    public string? Environment { get; set; }
}

/// <summary>
/// [MANAGER] Tạo payment account cho cafe của mình.
///
/// **2 chế độ:**
/// - **Cơ bản** (Cách A): chỉ cần BankCode + AccountNumber + AccountHolder. SePay Master
///   của BoardVerse sẽ tự detect giao dịch qua webhook (bank_mode=all) — KHÔNG cần đăng ký
///   SePay merchant cho cafe.
/// - **Cá nhân (SePay Personal)** (Cách B): Manager đăng ký merchant trên my.sepay.vn, lấy
///   SecretKey rồi nhập vào DTO này. Mỗi cafe tự quản lý SePay riêng, webhook verify bằng
///   SecretKey riêng của cafe đó. Phù hợp khi muốn tiền vào TK cafe trực tiếp, không qua
///   BoardVerse master.
///
/// Field SePay (SecretKey/WebhookToken/WebhookAuthType/MerchantId/ApiBaseUrl) là OPTIONAL.
/// Nếu KHÔNG nhập → dùng chế độ Cơ bản. Nếu nhập SecretKey → BoardVerse tự set
/// WebhookAuthType=HmacSha256 (khuyến nghị). Nếu nhập WebhookToken (không có SecretKey) →
/// BoardVerse tự set WebhookAuthType=ApiKey.
/// </summary>
public class CreateCafePaymentAccountRequestDto
{
    /// <summary>Mã ngân hàng (VD: MBBank, VietinBank, Vietcombank). Bắt buộc.</summary>
    public string BankCode { get; set; } = null!;

    /// <summary>Số tài khoản ngân hàng thật của cafe. Bắt buộc.</summary>
    public string AccountNumber { get; set; } = null!;

    /// <summary>Tên chủ tài khoản (in hoa, không dấu). Bắt buộc để hiển thị QR chính xác.</summary>
    public string AccountHolder { get; set; } = null!;

    /// <summary>
    /// Môi trường SePay cho cafe account (Test/Production). Mặc định 'Production'.
    /// Hầu hết cafe KHÔNG cần đụng field này.
    /// </summary>
    public string? Environment { get; set; }

    // === SePay Personal per-cafe fields (OPTIONAL — chỉ cần khi cafe muốn SePay riêng) ===

    /// <summary>
    /// Secret Key từ my.sepay.vn → Merchant settings → Secret Key.
    /// Nếu nhập field này, BoardVerse tự set <see cref="WebhookAuthType"/> = HmacSha256.
    /// </summary>
    public string? SecretKey { get; set; }

    /// <summary>
    /// Webhook Token từ my.sepay.vn → Webhook settings → API Key.
    /// Chỉ dùng khi <see cref="WebhookAuthType"/> = ApiKey (không dùng HMAC).
    /// </summary>
    public string? WebhookToken { get; set; }

    /// <summary>
    /// Loại xác thực webhook cho cafe này. Mặc định None (dùng SePay Master company).
    /// Khuyến nghị production nên set HmacSha256 (kèm SecretKey) hoặc ApiKey (kèm WebhookToken).
    /// </summary>
    public SePayWebhookAuthType? WebhookAuthType { get; set; }

    /// <summary>
    /// Merchant ID từ my.sepay.vn. Optional — chỉ cần khi cafe muốn gọi SePay API
    /// (transfer/checkout) chứ không chỉ nhận webhook.
    /// </summary>
    public string? MerchantId { get; set; }

    /// <summary>
    /// Base URL của SePay API cho cafe. Mặc định https://pgapi.sepay.vn khi dùng SePay Personal.
    /// </summary>
    public string? ApiBaseUrl { get; set; }
}

/// <summary>
/// [MANAGER] Kết quả preview QR cho cafe payment account.
/// Manager scan QR này trên app ngân hàng để verify VietQR render đúng + SePay detect được giao dịch.
/// </summary>
public class CafePaymentQrPreviewDto
{
    /// <summary>URL QR image (VietQR format) — paste vào browser hoặc hiển thị trên UI.</summary>
    public string QrUrl { get; set; } = null!;

    /// <summary>Số tiền test cố định (10.000 VND) để Manager CK thử.</summary>
    public decimal TestAmount { get; set; }

    /// <summary>Nội dung CK Manager cần nhập đúng khi test (SePay sẽ detect qua content).</summary>
    public string TestTransferContent { get; set; } = null!;

    /// <summary>Bank info echo lại để UI hiển thị confirm.</summary>
    public string BankCode { get; set; } = null!;
    public string MaskedAccountNumber { get; set; } = null!;
    public string AccountHolder { get; set; } = null!;

    /// <summary>Hướng dẫn test cho Manager (tiếng Việt).</summary>
    public string Instructions { get; set; } = null!;
}

public class UpdateSePayAccountRequestDto
{
    public string? MerchantId { get; set; }
    public string? ApiKey { get; set; }
    public string? SecretKey { get; set; }
    public string? WebhookToken { get; set; }
    public SePayWebhookAuthType? WebhookAuthType { get; set; }
    public string? ApiBaseUrl { get; set; }
    public string? BankCode { get; set; }
    public string? AccountNumber { get; set; }
    public string? AccountHolder { get; set; }
    public string? ReturnUrl { get; set; }
    public string? Environment { get; set; }
    public bool? IsActive { get; set; }
}

public class SePayAccountQuery
{
    public SePayAccountType? AccountType { get; set; }
    public Guid? CafeId { get; set; }
    public bool? IsActive { get; set; }
}

public class SetEnvironmentRequestDto
{
    public string Environment { get; set; } = null!;
}

/// <summary>
/// Admin lookup: Tra cứu BookingDeposit theo SePayTransactionId.
/// Bao gồm thông tin deposit + booking + cafe + status để debug webhook mismatch.
/// </summary>
public class SePayTransactionLookupDto
{
    public Guid DepositId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public Guid? BookingId { get; set; }
    public Guid? ActiveSessionId { get; set; }
    public Guid CafeId { get; set; }
    public string? CafeName { get; set; }
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? SePayTransactionId { get; set; }
    public string? SePayTransferId { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? RefundedAt { get; set; }
    public DateTime? ForfeitedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
