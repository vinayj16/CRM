using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MongoDB.Bson.Serialization.Attributes;
using CRM.Helpers;

namespace CRM.Models.Chatbot
{
    [BsonIgnoreExtraElements]
    public class RealTimeChatMessage
    {
        public int TenantId { get; set; } = 0;

        [Key]
        [BsonIgnore]
        public int Id { get; set; }
        
        [Required]
        [StringLength(100)]
        public string SessionId { get; set; } = string.Empty;
        
        [Required]
        [StringLength(100)]
        public string ConversationId { get; set; } = string.Empty;
        
        [Required]
        [StringLength(50)]
        public string MessageType { get; set; } = "User"; // User, Agent, System, Bot
        
        public int? SenderId { get; set; } // User ID for agents, NULL for anonymous users
        
        [BsonIgnore]
        public virtual UserModel? Sender { get; set; }
        
        [Required]
        [StringLength(255)]
        public string SenderName { get; set; } = string.Empty;
        
        [Required]
        public string MessageText { get; set; } = string.Empty;
        
        public string? ImageData { get; set; } // Base64 encoded image data
        
        public DateTime SentAt { get; set; } = IndianTime.Now;
        public bool IsRead { get; set; } = false;
        public DateTime? ReadAt { get; set; }
        
        public int? AssignedAgentId { get; set; }
        
        [BsonIgnore]
        public virtual UserModel? AssignedAgent { get; set; }
        
        [Required]
        [StringLength(20)]
        public string Priority { get; set; } = "Normal"; // Low, Normal, High, Urgent
        
        [Required]
        [StringLength(50)]
        public string Status { get; set; } = "Active"; // Active, Resolved, Transferred, Closed
        
        [StringLength(100)]
        public string? Intent { get; set; } // Detected intent from AI
        
        public decimal? Confidence { get; set; } // AI confidence score
        
        public int? LeadId { get; set; }
        
        [BsonIgnore]
        public virtual LeadModel? RelatedLead { get; set; }
        
        public bool IsLeadGenerated { get; set; } = false;
        
        public int? ParentMessageId { get; set; } // For threaded replies
        
        [BsonIgnore]
        public virtual RealTimeChatMessage? ParentMessage { get; set; }
        
        [BsonIgnore]
        public virtual ICollection<RealTimeChatMessage> Replies { get; set; } = new List<RealTimeChatMessage>();
        
        public string? Metadata { get; set; } // JSON metadata for additional info
        public DateTime CreatedAt { get; set; } = IndianTime.Now;
        public DateTime UpdatedAt { get; set; } = IndianTime.Now;
    }
}
