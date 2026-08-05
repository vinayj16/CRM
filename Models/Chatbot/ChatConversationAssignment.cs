using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MongoDB.Bson.Serialization.Attributes;
using CRM.Helpers;

namespace CRM.Models.Chatbot
{
    [BsonIgnoreExtraElements]
    public class ChatConversationAssignment
    {
        public int TenantId { get; set; } = 0;

        [Key]
        [BsonIgnore]
        public int Id { get; set; }
        
        [Required]
        [StringLength(100)]
        public string ConversationId { get; set; } = string.Empty;
        
        public int? AssignedAgentId { get; set; }
        
        [BsonIgnore]
        public virtual UserModel? AssignedAgent { get; set; }
        
        public int? AssignedByAgentId { get; set; }
        
        [BsonIgnore]
        public virtual UserModel? AssignedByAgent { get; set; }
        
        public DateTime AssignedAt { get; set; } = IndianTime.Now;
        
        [Required]
        [StringLength(50)]
        public string Status { get; set; } = "Unassigned"; // Unassigned, Assigned, InProgress, Resolved, Transferred
        
        [Required]
        [StringLength(20)]
        public string Priority { get; set; } = "Normal"; // Low, Normal, High, Urgent
        
        public string? Notes { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public int? ResolvedByAgentId { get; set; }
        
        [BsonIgnore]
        public virtual UserModel? ResolvedByAgent { get; set; }
        
        public int TransferCount { get; set; } = 0;
        public DateTime? LastAgentActivityAt { get; set; }
        public int EscalationLevel { get; set; } = 0;
    }
}
