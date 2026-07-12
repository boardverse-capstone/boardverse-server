using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Payment;

public class RefundDepositRequestDto
{
    [Required]
    public Guid DepositId { get; set; }

    [Required]
    [StringLength(500)]
    public string Reason { get; set; } = string.Empty;
}
