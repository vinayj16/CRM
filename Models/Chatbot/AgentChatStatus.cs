using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MongoDB.Bson.Serialization.Attributes;
using CRM.Helpers;

namespace CRM.Models.Chatbot
{
    [BsonIgnoreExtraElements]
    public class AgentChatStatus
    {
        public int TenantId { get; set; } = 0;

        [Key]
        [BsonIgnore]
        public int Id { get; set; }
        
        [Required]
        public int AgentId { get; set; }
        
        [BsonIgnore]
        public virtual UserModel Agent { get; set; } = null!;
        
        public bool IsOnline { get; set; } = false;
        
        [Required]
        [StringLength(50)]
        public string CurrentStatus { get; set; } = "Offline"; // Online, Busy, Away, Offline, InCall
        
        public int MaxConcurrentChats { get; set; } = 5;
        public int CurrentChatCount { get; set; } = 0;
        public DateTime LastActivityAt { get; set; } = IndianTime.Now;
        public DateTime? LastMessageAt { get; set; }
        public decimal? AverageResponseTime { get; set; } // in seconds
        public int TotalChatsHandled { get; set; } = 0;
        public int TotalMessagesSent { get; set; } = 0;
        public decimal? AverageRating { get; set; }
        public TimeSpan? ShiftStart { get; set; }
        public TimeSpan? ShiftEnd { get; set; }
        public string? Specializations { get; set; } // JSON array of specializations
        public string? DeviceToken { get; set; } // For push notifications
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = IndianTime.Now;
        public DateTime UpdatedAt { get; set; } = IndianTime.Now;
    }
}
