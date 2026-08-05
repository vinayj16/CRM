using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.MasterDb.Models
{
    public class InquiryModel
    {
        [Key]
        public int InquiryId { get; set; }

        [Required]
        [StringLength(200)]
        public string CompanyName { get; set; } = "";

        [Required]
        [StringLength(200)]
        public string ContactPerson { get; set; } = "";

        [Required]
        [StringLength(200)]
        public string Email { get; set; } = "";

        [StringLength(20)]
        public string? Phone { get; set; }

        [StringLength(2000)]
        public string? Message { get; set; }

        [StringLength(50)]
        public string Status { get; set; } = "New"; // New, Contacted, Converted, Rejected

        [StringLength(1000)]
        public string? Notes { get; set; } // Super Admin internal notes

        [StringLength(100)]
        public string? SelectedPlan { get; set; } // Plan selected by the user on the "Get Started" form

        public int? ConvertedToTenantId { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedOn { get; set; }
        [StringLength(20)]
        public string ReferralCode { get; set; }

        public int? SelectedPlanId { get; set; }
        [StringLength(100)]
        public string? SelectedPlanName { get; set; }

        public virtual TenantModel? ConvertedTenant { get; set; }
    }
}