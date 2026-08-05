using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.MasterDb.Models
{
    [BsonIgnoreExtraElements]
    public class SaasSubscriptionPlanModel
    {
        [BsonId]
        public MongoDB.Bson.ObjectId Id { get; set; } = MongoDB.Bson.ObjectId.GenerateNewId();

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

        // Usage Limits
        public int MaxUsers { get; set; } = 5; // -1 for unlimited
        public int MaxAgents { get; set; } = 2; // -1 for unlimited
        public int MaxLeadsPerMonth { get; set; } = 500; // -1 for unlimited
        public int MaxPartners { get; set; } // -1 for unlimited
        public int MaxStorageGB { get; set; } = -1; // -1 for unlimited (hidden for now)

        // Feature Flags
        public bool HasWhatsAppIntegration { get; set; } = false;
        public bool HasFacebookIntegration { get; set; } = false;
        public bool HasEmailIntegration { get; set; } = true;
        public bool HasCustomAPIAccess { get; set; } = false;
        public bool HasAdvancedReports { get; set; } = false;
        public bool HasCustomBranding { get; set; } = false;
        public bool HasPrioritySupport { get; set; } = false;
        public bool HasImpersonation { get; set; }

        // New Feature Flags for existing functionality
        public bool HasLeadScoring { get; set; } = false;
        public bool HasSiteVisitManagement { get; set; } = false;
        public bool HasDocumentManagement { get; set; } = false;
        public bool HasInventoryManagement { get; set; } = false;
        public bool HasCampaignManagement { get; set; } = false;
        public bool HasLegalManagement { get; set; } = false;
        public bool HasInvoiceAutomation { get; set; } = false;
        public bool HasQuotationManagement { get; set; } = false;
        public bool HasWorkflowAutomation { get; set; } = false;
        public bool HasCustomerPortal { get; set; } = false;
        public bool HasAIScoring { get; set; } = false;
        public bool HasAIChatbot { get; set; } = false;
        public bool HasMobileApp { get; set; } = false;
        public bool HasTwoFactorAuth { get; set; } = false;
        public bool HasCallIntegration { get; set; } = false;
        public bool HasSmsIntegration { get; set; } = false;
        public bool HasMultiLanguage { get; set; } = false;
        public bool HasGpsTracking { get; set; } = false;

        // Usage Limits
        public int MaxSiteVisitsPerMonth { get; set; } = 0;
        public int MaxEmailCampaigns { get; set; } = 0;
        public int MaxDocuments { get; set; } = 0;
        public int MaxProperties { get; set; } = 0;
        public int MaxQuotationsPerMonth { get; set; } = 0;

        [StringLength(50)]
        public string SupportLevel { get; set; } = "Email"; // Email, Chat, Phone, Dedicated

        [StringLength(20)]
        public string PlanType { get; set; } = "Basic"; // Basic, Standard, Enterprise, Premium

        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; } = 0;

        public bool? ShowOnLandingPage { get; set; } = true;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedOn { get; set; }
    }
}