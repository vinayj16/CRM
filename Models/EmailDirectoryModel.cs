using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace CRM.MasterDb.Models
{
    public class EmailDirectoryModel
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        [Required]
        [StringLength(200)]
        public string Email { get; set; } = "";

        [Required]
        public int TenantId { get; set; }

        public virtual TenantModel? Tenant { get; set; }
    }
}