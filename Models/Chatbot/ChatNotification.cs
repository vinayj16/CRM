using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MongoDB.Bson.Serialization.Attributes;
using CRM.Helpers;

namespace CRM.Models.Chatbot
{
    [BsonIgnoreExtraElements]
    public class ChatNotification
    {
        public int TenantId { get; set; } = 0;

        [Key]
        [BsonIgnore]
        public int Id { get; set; }
        
        [Required]
        public int AgentId { get; set; }
        
        [BsonIgnore]
        public virtual UserModel Agent { get; set; } = null!;
        
        [Required]
        [StringLength(50)]
        public string NotificationType { get; set; } = string.Empty; // NewMessage, Assignment, Transfer, Escalation, System
        
        [Required]
        [StringLength(255)]
        public string Title { get; set; } = string.Empty;
        
        [Required]
        public string Message { get; set; } = string.Empty;
        
        [StringLength(100)]
        public string? RelatedConversationId { get; set; }
        
        public int? RelatedMessageId { get; set; }
        
        [BsonIgnore]
        public virtual RealTimeChatMessage? RelatedMessage { get; set; }
        
        public int? RelatedLeadId { get; set; }
        
        [BsonIgnore]
        public virtual LeadModel? RelatedLead { get; set; }
        
        public bool IsRead { get; set; } = false;
        public DateTime? ReadAt { get; set; }
        public DateTime CreatedAt { get; set; } = IndianTime.Now;
        public DateTime? ExpiresAt { get; set; }
        public bool ActionRequired { get; set; } = false;
        
        [StringLength(50)]
        public string? ActionType { get; set; } // Accept, Transfer, Escalate, Resolve
        
        [StringLength(500)]
        public string? ActionUrl { get; set; }
    }
}
