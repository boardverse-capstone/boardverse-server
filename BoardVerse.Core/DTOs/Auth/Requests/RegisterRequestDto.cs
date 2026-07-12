using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;
using BoardVerse.Core.Validation;

namespace BoardVerse.Core.DTOs.Auth.Requests
{
    public class RegisterRequestDto
    {
        [Required(ErrorMessage = ApiErrorMessages.Validation.UsernameRequired)]
        [StringLength(100, MinimumLength = 3, ErrorMessage = ApiErrorMessages.Validation.UsernameLength3To100)]
        public required string Username { get; set; }

        [Required(ErrorMessage = ApiErrorMessages.Validation.EmailRequired)]
        [EmailAddress(ErrorMessage = ApiErrorMessages.Validation.EmailInvalid)]
        [StringLength(256, ErrorMessage = ApiErrorMessages.Validation.EmailMaxLength)]
        public required string Email { get; set; }

        [Phone(ErrorMessage = ApiErrorMessages.Validation.PhoneInvalid)]
        [StringLength(50, ErrorMessage = ApiErrorMessages.Validation.PhoneMax50)]
        public string? PhoneNumber { get; set; }

        [Required(ErrorMessage = ApiErrorMessages.Validation.PasswordRequired)]
        [StringLength(100, MinimumLength = 8, ErrorMessage = ApiErrorMessages.Validation.PasswordLength8To100)]
        public required string Password { get; set; }

        /// <summary>
        /// BR-11: Ngày sinh để xác minh tuổi >= 13. Bắt buộc để tuân thủ quy định bảo vệ trẻ em.
        /// </summary>
        [Required(ErrorMessage = "Ngày sinh là bắt buộc để xác minh độ tuổi.")]
        [MinimumAge(13, ErrorMessage = "Ngày sinh phải cho thấy bạn từ 13 tuổi trở lên.")]
        public DateOnly DateOfBirth { get; set; }
    }
}
