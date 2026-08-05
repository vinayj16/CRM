using CRM.MasterDb.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.Models
{
    [BsonIgnoreExtraElements]
    public class ReferralEarningModel
    {
        [Key]
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        public int TenantId { get; set; }

        [StringLength(20)]
        public string? ReferralCode { get; set; }

        [StringLength(100)]
        public string Type { get; set; } = ""; // "Referrer" or "Joiner"

        public decimal Amount { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        public int? ReferredTenantId { get; set; }

        public bool IsUsed { get; set; } = false;

        public int? UsedInSubscriptionId { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

        // Navigation properties - ignored by MongoDB serializer
        [BsonIgnore]
        public virtual TenantModel? Tenant { get; set; }

        [BsonIgnore]
        public virtual TenantModel? ReferredTenant { get; set; }
    }
}
