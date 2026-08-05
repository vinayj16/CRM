using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class PropertyFlatModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int FlatId { get; set; }
        
        [Required]
        public int PropertyId { get; set; }
        
        public string? BlockName { get; set; }
        
        public string? FloorName { get; set; }
        
        [Required]
        public string FlatName { get; set; } = string.Empty;
        
        public string? BHK { get; set; }
        
        public string? PropertyType { get; set; }
        
        public string? PropertyGroup { get; set; }
        
        public decimal? AreaSqft { get; set; }
        
        public string? Location { get; set; }
        
        public int? BedroomCount { get; set; }
        
        public int? BathroomCount { get; set; }
        
        public bool ParkingAvailable { get; set; } = false;
        
        public string FlatStatus { get; set; } = "Available";
        
        // Additional properties for compatibility
        public string? Status { get; set; } // Alias for FlatStatus
        public string? Area { get; set; } // Area description (e.g., "1200 sq.ft")
        public string? FloorNumber { get; set; } // Floor number description
        
        public decimal? Price { get; set; }
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        public int? CreatedBy { get; set; }
        
        public DateTime? UpdatedOn { get; set; }
        
        public int? UpdatedBy { get; set; }
        
        public bool IsActive { get; set; } = true;
    }
}
