using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class PropertyModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int PropertyId { get; set; }
        
        [Required]
        public string PropertyName { get; set; } = string.Empty;
        
        [Required]
        public int BuilderId { get; set; }
        
        public string? Developer { get; set; } // Developer/Builder name
        
        public string? FlatNumber { get; set; }
        
        public string? FloorNumber { get; set; }
        
        public string? Unit { get; set; }
        
        public decimal? Price { get; set; }
        
        public string? PropertyGroup { get; set; }
        
        public int? PostedBy { get; set; }
        
        public decimal? AreaSqft { get; set; }
        
        public string? Location { get; set; }
        
        public string? PurchaseType { get; set; }
        
        public byte[]? PropertyImage { get; set; }
        
        public string? Inventory { get; set; }
        
        public int? AssignedTo { get; set; }
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        public int? CreatedBy { get; set; }
        
        public DateTime? UpdatedOn { get; set; }
        
        public int? UpdatedBy { get; set; }
        
        public bool IsActive { get; set; } = true;

    }
}
