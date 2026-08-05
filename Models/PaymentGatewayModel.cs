using CRM.Helpers;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    public class PaymentGatewayModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        [Column("GatewayId")]
        public int Id { get; set; }
        
        [Required]
        public string GatewayName { get; set; } = string.Empty; // Razorpay, PayPal, etc.
        
        public string? KeyId { get; set; }
        
        public string? KeySecret { get; set; }
        
        public string? WebhookSecret { get; set; }
        
        public bool IsActive { get; set; } = false;
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        public DateTime? UpdatedOn { get; set; }
    }
}