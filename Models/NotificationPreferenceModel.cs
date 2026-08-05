using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    /// <summary>
    /// Notification preferences for users (P2-N5)
    /// </summary>
    public class NotificationPreferenceModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int PreferenceId { get; set; }
        
        [Required]
        public int UserId { get; set; }
        
        public bool EmailNotifications { get; set; } = true;
        public bool PushNotifications { get; set; } = true;
        public bool WhatsAppNotifications { get; set; } = false;
        public bool SMSNotifications { get; set; } = false;
        
        // Notification type preferences
        public bool LeadAssignmentNotif { get; set; } = true;
        public bool BookingNotif { get; set; } = true;
        public bool PaymentNotif { get; set; } = true;
        public bool TaskNotif { get; set; } = true;
        public bool FollowUpNotif { get; set; } = true;
        public bool SubscriptionNotif { get; set; } = true;
        
        // Navigation property
        public virtual UserModel? User { get; set; }
    }
}
