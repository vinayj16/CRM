using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.Models
{
    public class PageModel
    {
        [Key]
        public int PageId { get; set; }
        public int ModuleId { get; set; }
        public string PageName { get; set; } // Index, Create, Details, etc.
        public string DisplayName { get; set; }
        public string Controller { get; set; }
        public string Action { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        
        [BsonIgnore]
        public virtual ModuleModel? Module { get; set; }
    }
}