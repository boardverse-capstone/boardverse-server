using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.CafeShift;

public class OpenShiftRequestDto
{
    [Required]
    public Guid CafeId { get; set; }

    [Required]
    [Range(0, double.MaxValue)]
    public decimal OpeningCashBalance { get; set; }
}

public class CloseShiftRequestDto
{
    [Required]
    [Range(0, double.MaxValue)]
    public decimal ClosingCashBalance { get; set; }
}
