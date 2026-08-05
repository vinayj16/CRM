using CRM.Helpers;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.Models
{
    public class RolePermission
    {
        [Key]
        [BsonSerializer(typeof(RolePermissionIdSerializer))]
        public int Id { get; set; }
        public string RoleName { get; set; }

        // ? Add these new permission fields
        public bool CanCreate { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
        public bool CanView { get; set; }

        public string? AllowedModules { get; set; }

        public int? ChannelPartnerId { get; set; }

        public DateTime CreatedAt { get; set; } = IndianTime.Now;
    }
}
