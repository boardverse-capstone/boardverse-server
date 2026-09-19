using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Payment;

/// <summary>
/// Request DTOs cho <c>DebugSePayController</c>.
/// </summary>
public class DebugSePayGenerateSignatureRequestDto
{
    [Required]
    public string OrderInvoiceNumber { get; set; } = string.Empty;

    [Required]
    [Range(1, double.MaxValue)]
    public decimal OrderAmount { get; set; }

    public string? OrderDescription { get; set; }

    public string? Currency { get; set; }

    public string? CustomerId { get; set; }
}

/// <summary>
/// Request DTO cho <c>DebugSePayController.PreviewCheckout</c>.
/// </summary>
public class DebugSePayPreviewCheckoutRequestDto
{
    [Required]
    [Range(1, double.MaxValue)]
    public decimal Amount { get; set; }

    public string? Description { get; set; }
}
