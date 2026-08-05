using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.MasterDb.Models
{
    [BsonIgnoreExtraElements]
    public class SuperAdminModel
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        [BsonIgnoreIfNull]
        public ObjectId? MongoObjectId { get; set; }

        [Key]
        [BsonIgnore]
        public int Id
        {
            get => SuperAdminId ?? 0;
            set => SuperAdminId = value;
        }

        [BsonElement("SuperAdminId")]
        public int? SuperAdminId { get; set; }

        [Required]
        [StringLength(200)]
        public string Email { get; set; } = "";

        [Required]
        [StringLength(500)]
        public string PasswordHash { get; set; } = "";

        [Required]
        [StringLength(200)]
        public string FullName { get; set; } = "";

        public byte[]? ProfileImage { get; set; }

        public string? ProfileImagePath { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime? LastLoginOn { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

        public string Role { get; set; } = "SuperAdmin";
    }
}