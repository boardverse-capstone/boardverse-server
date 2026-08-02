using System.Text.Json.Serialization;

namespace BoardVerse.Core.DTOs.Payment;

/// <summary>
/// Webhook payload từ SePay.
/// SePay docs dùng snake_case (order_id, gateway_transaction_id, paid_at, ...).
/// ASP.NET Core binding dùng camelCase mặc định — dùng custom converter (xem SnakeOrCamelConverter)
/// để chấp nhận cả snake_case lẫn camelCase trong cùng 1 payload.
/// </summary>
[JsonConverter(typeof(SnakeOrCamelConverter<SePayWebhookDto>))]
public class SePayWebhookDto
{
    public string Id { get; set; } = string.Empty;

    public string Gateway { get; set; } = "SePay";

    public string? GatewayTransactionId { get; set; }

    public string OrderId { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Currency { get; set; } = "VND";

    public string Status { get; set; } = string.Empty;

    public string? ReferenceCode { get; set; }

    public string? BankCode { get; set; }

    public string? BankAccount { get; set; }

    public string? Note { get; set; }

    public DateTime? PaidAt { get; set; }

    public string? Signature { get; set; }

    public Guid? SessionId { get; set; }
}
