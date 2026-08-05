using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class PermissionModel
    {
        [Key]
        public int PermissionId { get; set; }
        public string PermissionName { get; set; } // View, Create, Edit, Delete, Export, BulkUpload
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}