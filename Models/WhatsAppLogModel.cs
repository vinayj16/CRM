using CRM.Helpers;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    [Table("WhatsAppLogs")]
    public class WhatsAppLogModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int LogId { get; set; }

        [Required]
        [StringLength(50)]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required]
        public string Message { get; set; } = string.Empty;

        [StringLength(50)]
        public string MessageType { get; set; } = "text"; // text, template, document, image

        [StringLength(20)]
        public string Status { get; set; } = "Pending"; // Pending, Sent, Delivered, Read, Failed

        public string? ErrorMessage { get; set; }

        public DateTime SentOn { get; set; } = IndianTime.Now;

        public int? LeadId { get; set; }

        public virtual LeadModel? Lead { get; set; }

        public int? UserId { get; set; }

        public virtual UserModel? User { get; set; }
    }
}
