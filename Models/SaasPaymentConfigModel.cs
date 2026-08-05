using System.ComponentModel.DataAnnotations;

namespace CRM.MasterDb.Models
{
    public class SaasPaymentConfigModel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(200)]
        public string RazorpayKeyId { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string RazorpayKeySecret { get; set; } = string.Empty;

        [StringLength(200)]
        public string? RazorpayWebhookSecret { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedOn { get; set; }
    }
}