using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;
using CRM.Helpers;

namespace CRM.Models.Chatbot
{
    [BsonIgnoreExtraElements]
    public class ChatSessionModel
    {
        public int TenantId { get; set; } = 0;

        [BsonId]
        public MongoDB.Bson.ObjectId Id { get; set; } = MongoDB.Bson.ObjectId.GenerateNewId();
        
        public int SessionId { get; set; }
        
        [BsonElement("session_guid")]
        public string SessionGuid { get; set; } = string.Empty;
        
        public int? UserId { get; set; }
        public string? UserName { get; set; }
        public string? UserPhone { get; set; }
        public string? UserEmail { get; set; }
        public DateTime StartedAt { get; set; } = IndianTime.Now;
        public DateTime? EndedAt { get; set; }
        public string Status { get; set; } = "Active";
        public int MessageCount { get; set; }
        public string LastIntent { get; set; } = string.Empty;
        public bool IsLeadGenerated { get; set; }
        public int? GeneratedLeadId { get; set; }
        public int? AssignedAgentId { get; set; }
        
        // Navigation property
        public LeadModel? GeneratedLead { get; set; }
    }
}
