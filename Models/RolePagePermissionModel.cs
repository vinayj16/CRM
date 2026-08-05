using CRM.Helpers;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.Models
{
    public class RolePagePermissionModel
    {
        [Key]
        public int Id { get; set; }
        public string RoleName { get; set; }
        public int PageId { get; set; }
        public int PermissionId { get; set; }
        public bool IsGranted { get; set; }
        public DateTime CreatedDate { get; set; } = IndianTime.Now;
        public string CreatedBy { get; set; }
        public int? ChannelPartnerId { get; set; } // null = Admin permissions, value = Partner-specific permissions
        
        [BsonIgnore]
        public virtual PageModel? Page { get; set; }
        [BsonIgnore]
        public virtual PermissionModel? Permission { get; set; }
    }
}