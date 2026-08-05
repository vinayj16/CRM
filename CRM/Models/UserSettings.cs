using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class UserSettings
    {
        public int TenantId { get; set; } = 0;

        public int Id { get; set; }

        [Required]
        public string Username { get; set; }

        // Profile
        public byte[]? ProfileImage { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? Country { get; set; }
        public string? State { get; set; }
        public string? City { get; set; }
        public string? PostalCode { get; set; }

        // Security
        public DateTime? PasswordLastChanged { get; set; }
        public DateTime? AccountDeletedAt { get; set; }

        // Notifications
        public string? Notifications { get; set; }

        // Connected Apps
        public string? ConnectedApps { get; set; } // Store JSON of apps and statuses
    }
}
