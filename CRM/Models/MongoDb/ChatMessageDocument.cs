using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.Models.MongoDb
{
    public class ChatMessageDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        [BsonElement("conversation_id")]
        public string ConversationId { get; set; } = string.Empty;

        [BsonElement("session_id")]
        public string SessionId { get; set; } = string.Empty;

        [BsonElement("sender_type")]
        public string SenderType { get; set; } = "User"; // User, Bot, Agent, System

        [BsonElement("sender_id")]
        public int? SenderId { get; set; }

        [BsonElement("sender_name")]
        public string SenderName { get; set; } = string.Empty;

        [BsonElement("message_text")]
        public string MessageText { get; set; } = string.Empty;

        [BsonElement("message_type")]
        public string MessageType { get; set; } = "text"; // text, image, lead_capture, property_inquiry

        [BsonElement("image_data")]
        public string? ImageData { get; set; }

        [BsonElement("intent")]
        public string? Intent { get; set; }

        [BsonElement("confidence")]
        public double? Confidence { get; set; }

        [BsonElement("entity_data")]
        public string? EntityData { get; set; }

        [BsonElement("is_read")]
        public bool IsRead { get; set; } = false;

        [BsonElement("read_at")]
        public DateTime? ReadAt { get; set; }

        [BsonElement("sent_at")]
        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        [BsonElement("tenant_id")]
        public int TenantId { get; set; }

        [BsonElement("metadata")]
        public string? Metadata { get; set; }
    }
}
