using CRM.Helpers;
using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class UserModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int UserId { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public string Role { get; set; } // Admin, managerr, Sales
        public string? ResetToken { get; set; }
        public DateTime? ResetTokenExpiry { get; set; } // Token expires after 1 hour
        [RegularExpression(@"^[0-9]*$", ErrorMessage = "Phone number must contain only digits")]
        public string? Phone { get; set; }   // ? new field for contact
        public bool IsActive { get; set; } = true; // ? new field for status

        public DateTime CreatedDate { get; set; } = IndianTime.Now; // ? new
        public DateTime? LastActivity { get; set; } // ? new
        public int? ChannelPartnerId { get; set; } // For linking agents to channel partners
        public string? DeviceToken { get; set; } // FCM device token for push notifications
    }
}
