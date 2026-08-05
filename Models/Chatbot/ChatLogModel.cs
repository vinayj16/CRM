using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MongoDB.Bson.Serialization.Attributes;
using CRM.Helpers;

namespace CRM.Models.Chatbot
{
    [BsonIgnoreExtraElements]
    public class ChatLogModel
    {
        public int TenantId { get; set; } = 0;

        [BsonId]
        public MongoDB.Bson.ObjectId Id { get; set; } = MongoDB.Bson.ObjectId.GenerateNewId();
        
        [Key]
        [Column("LogId")]
        [BsonIgnore]
        public int ChatLogId { get; set; }
        
        [Required]
        [StringLength(100)]
        public string SessionId { get; set; } = string.Empty;
        
        public int? UserId { get; set; }
        public string? UserMessage { get; set; }
        public string? AiResponse { get; set; }
        public string? Intent { get; set; }
        public string? Confidence { get; set; }
        public int? GeneratedLeadId { get; set; }
        public bool IsLeadGenerated { get; set; }
        public string? PropertyQuery { get; set; }
        public string? ResponseTime { get; set; }
        public string? ImageData { get; set; }
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
    }
}
