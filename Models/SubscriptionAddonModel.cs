using CRM.Helpers;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    public class SubscriptionAddonModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int AddonId { get; set; }
        
        [Required]
        [StringLength(100)]
        public string AddonName { get; set; } = string.Empty;
        
        [StringLength(500)]
        public string? Description { get; set; }
        
        [Required]
        public decimal MonthlyPrice { get; set; }
        
        [Required]
        public decimal YearlyPrice { get; set; }
        
        [StringLength(50)]
        public string AddonType { get; set; } = "Feature"; // Feature, Storage, Agents, API_Calls
        
        // Addon Specifications
        public int? AdditionalAgents { get; set; }
        public int? AdditionalStorageGB { get; set; }
        public int? AdditionalAPICallsPerMonth { get; set; }
        public int? AdditionalLeadsPerMonth { get; set; }
        
        [StringLength(100)]
        public string? FeatureName { get; set; }
        
        public bool IsActive { get; set; } = true;
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        public DateTime? UpdatedOn { get; set; }
        
        public int SortOrder { get; set; } = 0;
    }
    
    public class PartnerSubscriptionAddonModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int PartnerAddonId { get; set; }
        
        [Required]
        public int SubscriptionId { get; set; }
        
        [Required]
        public int AddonId { get; set; }
        
        [Required]
        public decimal Amount { get; set; }
        
        [Required]
        public DateTime StartDate { get; set; }
        
        [Required]
        public DateTime EndDate { get; set; }
        
        [StringLength(20)]
        public string Status { get; set; } = "Active"; // Active, Expired, Cancelled
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        public DateTime? UpdatedOn { get; set; }
        
        // Navigation Properties
        public virtual PartnerSubscriptionModel? Subscription { get; set; }
        
        public virtual SubscriptionAddonModel? Addon { get; set; }
    }
}