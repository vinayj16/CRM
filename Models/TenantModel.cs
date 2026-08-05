using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.MasterDb.Models
{
    [BsonIgnoreExtraElements]
    public class TenantModel
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        [BsonIgnoreIfNull]
        public ObjectId? MongoObjectId { get; set; }

        public int TenantId { get; set; }

        [Required]
        [StringLength(200)]
        public string CompanyName { get; set; } = "";

        [StringLength(200)]
        public string Email { get; set; }

        [StringLength(200)]
        public string ContactPerson { get; set; }

        [StringLength(10)]
        public string Phone { get; set; }

        [StringLength(100)]
        public string? Subdomain { get; set; }

        [Required]
        [StringLength(500)]
        public string ConnectionString { get; set; } = "";

        [StringLength(50)]
        public string Plan { get; set; } = "Basic";

        public int MaxUsers { get; set; } = 50;

        public bool IsActive { get; set; } = true;

        public bool IsSuspended { get; set; } = false;

        [StringLength(500)]
        public string? SuspendedReason { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedOn { get; set; }

        [StringLength(20)]
        public string Referral { get; set; }
    }
}