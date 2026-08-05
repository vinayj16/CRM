using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.Models.MongoDb
{
    public class ChatSessionDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        [BsonElement("session_guid")]
        public string SessionGuid { get; set; } = string.Empty;

        [BsonElement("user_id")]
        public int? UserId { get; set; }

        [BsonElement("user_name")]
        public string? UserName { get; set; }

        [BsonElement("user_phone")]
        public string? UserPhone { get; set; }

        [BsonElement("user_email")]
        public string? UserEmail { get; set; }

        [BsonElement("user_role")]
        public string UserRole { get; set; } = "Public";

        [BsonElement("status")]
        public string Status { get; set; } = "Active";

        [BsonElement("message_count")]
        public int MessageCount { get; set; }

        [BsonElement("last_intent")]
        public string LastIntent { get; set; } = string.Empty;

        [BsonElement("is_lead_generated")]
        public bool IsLeadGenerated { get; set; }

        [BsonElement("generated_lead_id")]
        public int? GeneratedLeadId { get; set; }

        [BsonElement("assigned_agent_id")]
        public int? AssignedAgentId { get; set; }

        [BsonElement("started_at")]
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("ended_at")]
        public DateTime? EndedAt { get; set; }

        [BsonElement("last_activity_at")]
        public DateTime? LastActivityAt { get; set; }

        [BsonElement("tenant_id")]
        public int TenantId { get; set; }
    }
}
