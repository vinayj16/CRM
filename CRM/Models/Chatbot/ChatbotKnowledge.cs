using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.Models
{
    [BsonIgnoreExtraElements]
    public class ChatbotKnowledge
    {
        public int TenantId { get; set; } = 0;

        [Key]
        [BsonIgnore]
        public int Id { get; set; }
        
        [Required]
        public string Question { get; set; } = string.Empty;
        
        [Required]
        public string Answer { get; set; } = string.Empty;
        
        [Required]
        public string Category { get; set; } = string.Empty; // CRM, Properties, Leads, Payments, etc.
        
        public string? UserRole { get; set; } // If null, applies to all roles
        
        public string? Keywords { get; set; } // Comma-separated keywords for matching
        
        public int Priority { get; set; } = 1; // Higher priority for specific answers
        
        public bool IsActive { get; set; } = true;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime? UpdatedAt { get; set; }
    }
}
