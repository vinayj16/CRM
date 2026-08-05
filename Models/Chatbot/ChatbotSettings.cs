using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.Models.Chatbot
{
    [BsonIgnoreExtraElements]
    public class ChatbotSettings
    {
        public int TenantId { get; set; } = 0;

        [Key]
        [BsonIgnore]
        public int SettingId { get; set; }
        
        [Required]
        [StringLength(100)]
        public string SettingKey { get; set; } = string.Empty;
        
        [StringLength(500)]
        public string SettingValue { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }
    }
}
