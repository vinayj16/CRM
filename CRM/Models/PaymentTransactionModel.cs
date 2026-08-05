using CRM.Helpers;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    public class PaymentTransactionModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int TransactionId { get; set; }
        
        [Required]
        public int ChannelPartnerId { get; set; }
        
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
        public string? WebhookEventId { get; set; } // For idempotency - prevent duplicate webhook processing
        
        [Required]
        public decimal Amount { get; set; }
        
        [Required]
        [StringLength(10)]
        public string Currency { get; set; } = "INR";
        
        [Required]
        [StringLength(20)]
        public string TransactionType { get; set; } = "Payment"; // Payment, Refund, Upgrade, Downgrade
        
        [Required]
        [StringLength(20)]
        public string Status { get; set; } = "Pending"; // Pending, Success, Failed, Cancelled, Refunded
        
        [StringLength(50)]
        public string PaymentMethod { get; set; } = "Razorpay";
        
        public DateTime TransactionDate { get; set; } = IndianTime.Now;
        
        public DateTime? CompletedDate { get; set; }
        
        [StringLength(500)]
        public string? Description { get; set; }
        
        [StringLength(1000)]
        public string? FailureReason { get; set; }
        
        // Plan Information at time of transaction
        [StringLength(100)]
        public string? PlanName { get; set; }
        
        [StringLength(20)]
        public string? BillingCycle { get; set; }
        
        // Invoice Information
        [StringLength(50)]
        public string? InvoiceNumber { get; set; }
        
        public DateTime? InvoiceDate { get; set; }
        
        // Tax Information
        public decimal? TaxAmount { get; set; } = 0;
        public decimal? DiscountAmount { get; set; } = 0;
        public decimal NetAmount { get; set; }
        
        // Card Information (from Razorpay payment details)
        [StringLength(20)]
        public string? CardType { get; set; } // credit, debit, prepaid
        
        [StringLength(50)]
        public string? CardNetwork { get; set; } // Visa, Mastercard, RuPay, etc.
        
        [StringLength(10)]
        public string? CardLast4 { get; set; } // Last 4 digits
        
        [StringLength(50)]
        public string? BankName { get; set; } // Issuing bank
        
        // Audit Fields
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        public DateTime? UpdatedOn { get; set; }
        
        // Navigation Properties
        public virtual ChannelPartnerModel? ChannelPartner { get; set; }
        
        public virtual PartnerSubscriptionModel? Subscription { get; set; }

        // MongoDB: navigation properties will be null - use manual lookups
        [global::MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public virtual SubscriptionPlanModel? Plan { get; set; }
    }
}