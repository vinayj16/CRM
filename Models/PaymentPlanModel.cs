using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    public class PaymentPlanModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int PlanId { get; set; }
        
        [Required]
        public int BookingId { get; set; }
        
        [Required]
        public decimal TotalAmount { get; set; }
        
        public decimal PaidAmount { get; set; } = 0;
        
        [Required]
        public decimal OutstandingAmount { get; set; }
        
        [Required]
        public string PlanType { get; set; } = string.Empty; // Milestone, Monthly, Custom
        
        public string? PlanStructure { get; set; }
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        // Navigation property
        public BookingModel? Booking { get; set; }
    }
    
    public class PaymentInstallmentModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int InstallmentId { get; set; }
        
        [Required]
        public int PlanId { get; set; }
        
        [Required]
        public int InstallmentNumber { get; set; }
        
        [Required]
        public string MilestoneName { get; set; } = string.Empty;
        
        [Required]
        public DateTime DueDate { get; set; }
        
        [Required]
        public decimal Amount { get; set; }
        
        public decimal PaidAmount { get; set; } = 0;
        
        public string Status { get; set; } = "Pending"; // Pending, Partial, Paid, Overdue
        
        public DateTime? PaidDate { get; set; }

        // MongoDB: navigation properties will be null - use manual lookups
        [global::MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public BookingModel? Booking { get; set; }
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        // Navigation property
        public PaymentPlanModel? PaymentPlan { get; set; }
    }
}
