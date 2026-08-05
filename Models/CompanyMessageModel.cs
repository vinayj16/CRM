using CRM.Helpers;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.Models
{
    /// <summary>
    /// Internal company messaging model - messages between team members within the same company/tenant.
    /// Separate from chatbot messages - this is for employee-to-employee communication.
    /// </summary>
    [BsonIgnoreExtraElements]
    public class CompanyMessageModel
    {
        /// <summary>MongoDB auto-generated _id, exposed as a string for frontend use.</summary>
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        public int TenantId { get; set; } = 0;

        public int SenderId { get; set; }

        public string SenderName { get; set; } = string.Empty;

        public string SenderRole { get; set; } = string.Empty;

        /// <summary>0 means broadcast to all company members.</summary>
        public int RecipientId { get; set; }

        public string RecipientName { get; set; } = string.Empty;

        public string MessageText { get; set; } = string.Empty;

        public string? FileName { get; set; }

        public string? FilePath { get; set; }

        public string? FileType { get; set; }

        public long? FileSize { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTime? ReadAt { get; set; }

        public DateTime SentAt { get; set; } = IndianTime.Now;

        public bool IsDeleted { get; set; } = false;
    }
}
