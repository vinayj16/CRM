using CRM.Helpers;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.Models
{
    [BsonIgnoreExtraElements]
    public class PartnerSubscriptionModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int SubscriptionId { get; set; }
        
        [Required]
        public int ChannelPartnerId { get; set; }
        
        [Required]
        public int PlanId { get; set; }
        
        [Required]
        [StringLength(20)]
        public string BillingCycle { get; set; } = "Monthly"; // Monthly, Annual
        
        [Required]
        public decimal Amount { get; set; }
        
        [Required]
        public DateTime StartDate { get; set; }
        
        [Required]
        public DateTime EndDate { get; set; }
        
        [StringLength(20)]
        public string Status { get; set; } = "Active"; // Active, Expired, Cancelled, Suspended
        
        public DateTime? CancelledOn { get; set; }
        public string? CancellationReason { get; set; }
        
        public bool AutoRenew { get; set; } = true;
        
        // Payment Information
        [StringLength(100)]
        public string? PaymentTransactionId { get; set; }
        
        [StringLength(50)]
        public string? PaymentMethod { get; set; } = "Razorpay";
        
        public DateTime? LastPaymentDate { get; set; }
        public DateTime? NextPaymentDate { get; set; }
        
        // Usage Tracking
        public int CurrentAgentCount { get; set; } = 0;
        public int CurrentMonthLeads { get; set; } = 0;
        public decimal CurrentStorageUsedGB { get; set; } = 0;
        
        // Audit Fields
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        public DateTime? UpdatedOn { get; set; }
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
        
        // Navigation Properties (ignored by MongoDB serializer)
        [BsonIgnore]
        public virtual ChannelPartnerModel? ChannelPartner { get; set; }
        
        [BsonIgnore]
        public virtual SubscriptionPlanModel? Plan { get; set; }
    }
}