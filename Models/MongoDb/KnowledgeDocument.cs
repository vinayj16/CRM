using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.Models.MongoDb
{
    public class KnowledgeDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        [BsonElement("question")]
        public string Question { get; set; } = string.Empty;

        [BsonElement("answer")]
        public string Answer { get; set; } = string.Empty;

        [BsonElement("category")]
        public string Category { get; set; } = string.Empty;

        [BsonElement("keywords")]
        public string? Keywords { get; set; }

        [BsonElement("user_role")]
        public string? UserRole { get; set; }

        [BsonElement("priority")]
        public int Priority { get; set; } = 1;

        [BsonElement("is_active")]
        public bool IsActive { get; set; } = true;

        [BsonElement("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("updated_at")]
        public DateTime? UpdatedAt { get; set; }

        [BsonElement("tenant_id")]
        public int TenantId { get; set; }
    }
}
