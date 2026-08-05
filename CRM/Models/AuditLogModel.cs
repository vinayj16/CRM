using CRM.Helpers;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    /// <summary>
    /// Audit log model for tracking user actions (P2-U4)
    /// </summary>
    public class AuditLogModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public long AuditId { get; set; }
        
        public int? UserId { get; set; }
        
        [Required]
        [StringLength(100)]
        public string Action { get; set; } = string.Empty; // Login, Create, Update, Delete, View
        
        [StringLength(50)]
        public string? EntityType { get; set; } // Lead, Booking, Payment, User, etc.
        
        public int? EntityId { get; set; }
        
        public string? OldValues { get; set; } // JSON
        public string? NewValues { get; set; } // JSON
        
        [StringLength(45)]
        public string? IpAddress { get; set; }
        
        [StringLength(500)]
        public string? UserAgent { get; set; }
        
        public DateTime Timestamp { get; set; } = IndianTime.Now;
        
        // Navigation property
        public virtual UserModel? User { get; set; }
    }
}
