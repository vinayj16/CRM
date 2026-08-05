using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace CRM.Models
{
    [BsonIgnoreExtraElements]
    public class ChatbotMessage
    {
        public int TenantId { get; set; } = 0;

        [Key]
        [BsonIgnore]
        public int Id { get; set; }
        
        [Required]
        public int ConversationId { get; set; }
        
        [JsonIgnore]
        [BsonIgnore]
        public virtual ChatbotConversation Conversation { get; set; } = null!;
        
        [Required]
        public string MessageText { get; set; } = string.Empty;
        
        [Required]
        public string SenderType { get; set; } = "User"; // User, Bot
        
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        
        public string? MessageType { get; set; } // text, lead_capture, property_inquiry, etc.
        
        public bool IsRead { get; set; } = true;
        
        public string? Intent { get; set; } // Detected intent of the message
        
        public string? EntityData { get; set; } // JSON data for extracted entities
    }
}
