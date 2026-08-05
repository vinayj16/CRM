using CRM.Helpers;
using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    /// <summary>
    /// Webhook retry queue for failed webhook deliveries (P2-I2)
    /// </summary>
    public class WebhookRetryQueueModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int QueueId { get; set; }
        
        [Required]
        [StringLength(200)]
        public string WebhookEventId { get; set; } = string.Empty;
        
        [Required]
        public string PayloadJson { get; set; } = string.Empty;
        
        [Required]
        [StringLength(500)]
        public string Endpoint { get; set; } = string.Empty;
        
        public int RetryCount { get; set; } = 0;
        public int MaxRetries { get; set; } = 3;
        
        public DateTime? NextRetryAt { get; set; }
        
        public string? LastError { get; set; }
        
        [StringLength(20)]
        public string Status { get; set; } = "Pending"; // Pending, Processing, Failed, Success
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        public DateTime? ProcessedOn { get; set; }
    }
}
