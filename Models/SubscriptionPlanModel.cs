using CRM.Helpers;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.Models
{
    [BsonIgnoreExtraElements]
    public class SubscriptionPlanModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int PlanId { get; set; }

        [Required]
        [StringLength(100)]
        public string PlanName { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        [Required]
        public decimal MonthlyPrice { get; set; }

        [Required]
        public decimal YearlyPrice { get; set; }

        // Feature Limits
        public int MaxAgents { get; set; } = 2; // -1 for unlimited
        public int MaxLeadsPerMonth { get; set; } = 500; // -1 for unlimited
        public int MaxStorageGB { get; set; } = 1; // -1 for unlimited

        // Feature Flags
        public bool HasWhatsAppIntegration { get; set; } = true;
        public bool HasFacebookIntegration { get; set; } = false;
        public bool HasEmailIntegration { get; set; } = false;
        public bool HasCustomAPIAccess { get; set; } = false;
        public bool HasAdvancedReports { get; set; } = false;
        public bool HasCustomReports { get; set; } = false;
        public bool HasDataExport { get; set; } = false;
        public bool HasPrioritySupport { get; set; } = false;
        public bool HasPhoneSupport { get; set; } = false;
        public bool HasDedicatedmanager { get; set; } = false;

        [StringLength(50)]
        public string SupportLevel { get; set; } = "Email"; // Email, Chat, Phone, Dedicated

        public bool IsActive { get; set; } = true;
        public DateTime CreatedDate { get; set; } = IndianTime.Now;
        public DateTime? UpdatedOn { get; set; }

        [StringLength(20)]
        public string PlanType { get; set; } = "Basic"; // Basic, Standard, Enterprise

        public int SortOrder { get; set; } = 0;
    }
}