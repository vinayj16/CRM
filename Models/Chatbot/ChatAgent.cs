using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;
using CRM.Helpers;

namespace CRM.Models.Chatbot
{
    [BsonIgnoreExtraElements]
    public class ChatAgent
    {
        public int TenantId { get; set; } = 0;

        [Key]
        [BsonIgnore]
        public int AgentId { get; set; }
        public int UserId { get; set; }
        public bool IsAvailable { get; set; } = true;
        public bool IsOnline { get; set; } = false;
        public int MaxConcurrentChats { get; set; } = 5;
        public int CurrentChatCount { get; set; } = 0;
        public DateTime? LastActiveAt { get; set; }
        public TimeSpan? ShiftStart { get; set; }
        public TimeSpan? ShiftEnd { get; set; }
        public string? Specializations { get; set; }
        public decimal Rating { get; set; } = 0.0m;
        public int TotalChatsHandled { get; set; } = 0;
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        public DateTime? ModifiedOn { get; set; }
    }
}
