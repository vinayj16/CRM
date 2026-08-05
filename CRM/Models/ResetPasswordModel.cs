using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class ResetPasswordModel
    {
        public int TenantId { get; set; } = 0;

        public string? Username { get; set; }
        public string? Token { get; set; }

        [Required(ErrorMessage = "Current password is required.")]
        [DataType(DataType.Password)]
        public string oldPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "New password is required.")]
        [DataType(DataType.Password)]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{6,}$",
            ErrorMessage = "Password must contain uppercase, lowercase, number, and special character (min 6 chars).")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirm password is required.")]
        [DataType(DataType.Password)]
        [Compare("NewPassword", ErrorMessage = "New password and confirm password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
