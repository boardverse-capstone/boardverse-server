using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.Validation
{
    /// <summary>
    /// BR-11: Validates that the date of birth indicates the user is at least 13 years old.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class MinimumAgeAttribute : ValidationAttribute
    {
        public int MinimumAge { get; }

        public MinimumAgeAttribute(int minimumAge = 13)
        {
            MinimumAge = minimumAge;
            ErrorMessage = $"Ngày sinh phải cho thấy bạn từ {MinimumAge} tuổi trở lên.";
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is not DateOnly dateOfBirth)
            {
                return ValidationResult.Success;
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var age = today.Year - dateOfBirth.Year;

            if (dateOfBirth > today.AddYears(-age))
            {
                age--;
            }

            if (age < MinimumAge)
            {
                return new ValidationResult(ErrorMessage);
            }

            return ValidationResult.Success;
        }
    }
}
