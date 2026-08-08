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
    public string? ApiBaseUrl { get; set; }
    public string? BankCode { get; set; }
    public string? AccountNumber { get; set; }
    public string? AccountHolder { get; set; }
    public string? ReturnUrl { get; set; }
    public string? Environment { get; set; }
}

/// <summary>
/// [MANAGER] Tạo payment account cho cafe của mình.
/// CHỈ 4 field bắt buộc — KHÔNG cần MerchantId/ApiKey/SecretKey/WebhookToken.
/// Manager không cần đăng ký SePay merchant. BoardVerse sẽ tự detect giao dịch
/// vào TK ngân hàng thật của cafe qua SePay webhook (bank_mode=all).
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
