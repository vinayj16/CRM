using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MongoDB.Bson.Serialization.Attributes;
using CRM.Helpers;

namespace CRM.Models.Chatbot
{
    [BsonIgnoreExtraElements]
    public class ChatMessageMetrics
    {
        public int TenantId { get; set; } = 0;

        [Key]
        [BsonIgnore]
        public int Id { get; set; }
        
        [Required]
        [StringLength(100)]
        public string ConversationId { get; set; } = string.Empty;
        
        public int? AgentId { get; set; }
        
        [BsonIgnore]
        public virtual UserModel? Agent { get; set; }
        
        [Required]
        public DateTime MetricDate { get; set; } = IndianTime.Today;
        
        public int TotalMessages { get; set; } = 0;
        public int UserMessages { get; set; } = 0;
        public int AgentMessages { get; set; } = 0;
        public decimal? AverageResponseTime { get; set; } // in seconds
        public decimal? FirstResponseTime { get; set; } // in seconds
        public int? ConversationDuration { get; set; } // in minutes
        public int? ResolutionTime { get; set; } // in minutes
        public int? CustomerSatisfaction { get; set; } // 1-5 rating
        public bool LeadGenerated { get; set; } = false;
        public decimal? LeadValue { get; set; }
        public DateTime CreatedAt { get; set; } = IndianTime.Now;
    }
}
