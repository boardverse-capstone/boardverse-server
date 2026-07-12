using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Cafe
{
    public class UpdateSePayConfigRequestDto
    {
        [StringLength(50)]
        public string? SePayBankCode { get; set; }

        [StringLength(50)]
        public string? SePayAccountNumber { get; set; }
    }
}
