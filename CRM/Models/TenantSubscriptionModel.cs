using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.MasterDb.Models
{
    [BsonIgnoreExtraElements]
    public class TenantSubscriptionModel
    {
        [BsonId]
        public MongoDB.Bson.ObjectId Id { get; set; } = MongoDB.Bson.ObjectId.GenerateNewId();

        public int SubscriptionId { get; set; }

        [Required]
        public int TenantId { get; set; }

        [Required]
        public int PlanId { get; set; }

        [Required]
        [StringLength(20)]
        public string BillingCycle { get; set; } = "Monthly"; // Monthly, Annual, Trial

        [Required]
        public decimal Amount { get; set; }

        [Required]
        public DateTime StartDate { get; set; }

        [Required]
        public DateTime EndDate { get; set; }

        [Required]
        [StringLength(20)]
        public string Status { get; set; } = "Active"; // Active, Trial, Expired, Cancelled, Suspended

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

        // Audit Fields
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedOn { get; set; }
        public int? CreatedBy { get; set; }

                // Navigation Properties (not used with MongoDB - kept for code compatibility)
        [BsonIgnore]
        public virtual TenantModel? Tenant { get; set; }

        [BsonIgnore]
        public virtual SaasSubscriptionPlanModel? Plan { get; set; }
    }
}