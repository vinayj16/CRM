using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.Models
{
    [BsonIgnoreExtraElements]
    public class ChannelPartnerModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int PartnerId { get; set; }
        
        [Required(ErrorMessage = "Company Name is required")]
        public string CompanyName { get; set; }
        
        [Required(ErrorMessage = "Contact Person is required")]
        public string ContactPerson { get; set; }
        
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        public string Email { get; set; }
        
        [Required(ErrorMessage = "Phone is required")]
        [RegularExpression(@"^[0-9]+$", ErrorMessage = "Phone number must contain only digits")]
        public string Phone { get; set; }
        public string? Address { get; set; }
        public string? CommissionScheme { get; set; }
        public string? Documents { get; set; }
        public string? Status { get; set; } = "Pending";
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        public int? ApprovedBy { get; set; }
        public DateTime? ApprovedOn { get; set; }
        public int? UserId { get; set; } // Link to Users table after approval
        public decimal CommissionPercentage { get; set; } = 5.0m; // Commission percentage (0.5% to 20%)
        public string? SubscriptionPlan { get; set; } = "Basic";

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public int? SelectedPlanId { get; set; } // Basic or Paid

        [System.ComponentModel.DataAnnotations.StringLength(50)]
        public string? Subdomain { get; set; } // For subdomain-based access
    }
}