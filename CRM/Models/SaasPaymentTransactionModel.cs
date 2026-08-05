using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.MasterDb.Models
{
    [BsonIgnoreExtraElements]
    public class SaasPaymentTransactionModel
    {
        [Key]
        public int TransactionId { get; set; }

        [Required]
        public int TenantId { get; set; }

        public int? SubscriptionId { get; set; }

        [Required]
        [StringLength(100)]
        public string TransactionReference { get; set; } = string.Empty;

        [StringLength(100)]
        public string? RazorpayPaymentId { get; set; }

        [StringLength(100)]
        public string? RazorpayOrderId { get; set; }

        [StringLength(100)]
        public string? RazorpaySignature { get; set; }

        [StringLength(200)]
        public string? WebhookEventId { get; set; }

        [Required]
        public decimal Amount { get; set; }

        [Required]
        [StringLength(10)]
        public string Currency { get; set; } = "INR";

        [Required]
        [StringLength(20)]
        public string TransactionType { get; set; } = "Payment"; // Payment, Refund, Upgrade, Cancellation

        [Required]
        [StringLength(20)]
        public string Status { get; set; } = "Pending"; // Pending, Success, Failed, Cancelled, Refunded, Authorized

        [StringLength(50)]
        public string PaymentMethod { get; set; } = "Razorpay";

        public DateTime TransactionDate { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedDate { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        [StringLength(1000)]
        public string? FailureReason { get; set; }

        [StringLength(100)]
        public string? PlanName { get; set; }

        [StringLength(500)]
        public string? BillingCycle { get; set; }

        [StringLength(500)]
        public string? InvoiceNumber { get; set; }

        public decimal? NetAmount { get; set; }

        public DateTime? InvoiceDate { get; set; }

        public decimal? TaxAmount { get; set; }

        public decimal? DiscountAmount { get; set; }

        [StringLength(20)]
        public string? CardType { get; set; }

        [StringLength(50)]
        public string? CardNetwork { get; set; }

        [StringLength(10)]
        public string? CardLast4 { get; set; }

        [StringLength(50)]
        public string? BankName { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedOn { get; set; }

        // Navigation Properties
        public virtual TenantModel? Tenant { get; set; }

        public virtual TenantSubscriptionModel? Subscription { get; set; }

        // MongoDB: navigation properties will be null - use manual lookups
        [global::MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public virtual SaasSubscriptionPlanModel? Plan { get; set; }


        public string? RefundStatus { get; set; }
        public string? RefundId { get; set; }
        public DateTime? RefundDate { get; set; }
    }
}