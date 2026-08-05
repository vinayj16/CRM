using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.Models.Chatbot
{
    [BsonIgnoreExtraElements]
    public class ChatIntentModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        [BsonIgnore]
        public int IntentId { get; set; }
        
        [Required]
        [StringLength(100)]
        public string IntentName { get; set; } = string.Empty;
        
        public string? Description { get; set; }
        public string? TriggerKeywords { get; set; }
        public string? Action { get; set; }
        public bool IsActive { get; set; } = true;
        public int Priority { get; set; } = 1;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }
    }
}
