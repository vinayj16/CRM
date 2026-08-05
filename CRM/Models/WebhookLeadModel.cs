using CRM.Helpers;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    [Table("WebhookLeads")]
    public class WebhookLeadModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int WebhookLeadId { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(100)]
        [EmailAddress]
        public string? Email { get; set; }

        [Required]
        [StringLength(20)]
        [RegularExpression(@"^[0-9]+$", ErrorMessage = "Contact number must contain only digits")]
        public string Contact { get; set; } = string.Empty;

        [StringLength(100)]
        public string? PreferredLocation { get; set; }

        [StringLength(20)]
        public string? BHK { get; set; }

        [StringLength(50)]
        public string? Budget { get; set; }

        [StringLength(100)]
        public string? CompanyName { get; set; }

        public string? Requirements { get; set; }

        [Required]
        [StringLength(50)]
        public string LeadType { get; set; } = "Express Interest"; // "Express Interest" or "Site Visit"

        [StringLength(100)]
        public string? ProjectName { get; set; }

        public int? PropertyId { get; set; }

        public virtual PropertyModel? Property { get; set; }

        [StringLength(50)]
        public string Status { get; set; } = "Pending"; // Pending, Assigned, Converted, Rejected

        public int? AssignedToUserId { get; set; }

        public virtual UserModel? AssignedToUser { get; set; }

        public DateTime CreatedOn { get; set; } = IndianTime.Now;

        public DateTime? AssignedOn { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }

        [StringLength(100)]
        public string? Source { get; set; } = "Website"; // Website, Landing Page, etc.

        public int? ConvertedToLeadId { get; set; }

        public virtual LeadModel? ConvertedToLead { get; set; }
    }
}
