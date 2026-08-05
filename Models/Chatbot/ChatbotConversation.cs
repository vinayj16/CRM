using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.Models
{
    [BsonIgnoreExtraElements]
    public class ChatbotConversation
    {
        public int TenantId { get; set; } = 0;

        [Key]
        [BsonIgnore]
        public int Id { get; set; }
        
        [Required]
        public string SessionId { get; set; } = string.Empty;
        
        public int? UserId { get; set; }
        
        [BsonIgnore]
        public virtual UserModel? User { get; set; }
        
        [Required]
        public string UserRole { get; set; } = "Public"; // Public, Agent, ChannelPartner, Admin
        
        [Required]
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime? LastActivityAt { get; set; } = DateTime.UtcNow;
        
        public bool IsActive { get; set; } = true;
        
        public int? CapturedLeadId { get; set; }
        
        [BsonIgnore]
        public virtual LeadModel? CapturedLead { get; set; }
        
        public string? VisitorName { get; set; }
        
        public string? VisitorPhone { get; set; }
        
        public string? VisitorEmail { get; set; }
        
        // Navigation property
        [BsonIgnore]
        public virtual ICollection<ChatbotMessage> Messages { get; set; } = new List<ChatbotMessage>();
    }
}
