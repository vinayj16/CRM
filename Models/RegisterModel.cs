using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace CRM.Models
{
    public class RegisterModel
    {
        public int TenantId { get; set; } = 0;

        public string Username { get; set; }

        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address with '@' symbol.")]
        public string Email { get; set; }

        public string? Phone { get; set; }

        [Required(ErrorMessage = "Password is required.")]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{6,}$",
            ErrorMessage = "Password must contain uppercase, lowercase, number, and special character (min 6 chars).")]
        public string Password { get; set; }
        public string Role { get; set; }

        // Agent documents
        public IFormFile? AgentAadhar { get; set; }
        public IFormFile? AgentPAN { get; set; }
        public IFormFile? AgentResume { get; set; }
        public IFormFile? AgentExperienceLetter { get; set; }

        // Partner documents
        public IFormFile? PartnerBusinessReg { get; set; }
        public IFormFile? PartnerTaxCert { get; set; }
        public IFormFile? PartnerIDProof { get; set; }
        public IFormFile? PartnerAadhar { get; set; }
        public IFormFile? PartnerPAN { get; set; }
        public IFormFile? PartnerResume { get; set; }
        public IFormFile? PartnerExperienceLetter { get; set; }
        public string? CompanyName { get; set; }
    }
}
