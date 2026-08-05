using System;
using System.ComponentModel.DataAnnotations;
using CRM.Helpers;

namespace CRM.Models
{
    /// <summary>
    /// Inventory / unit tracking per project (property).
    /// </summary>
    public class InventoryUnitModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int UnitId { get; set; }
        public int PropertyId { get; set; }
        public string? Block { get; set; }
        public string? Floor { get; set; }
        public string? UnitNumber { get; set; }
        public string? UnitType { get; set; } // Apartment, Villa, Plot, Shop
        public string? BHK { get; set; }
        public decimal? AreaSqft { get; set; }
        public decimal? Price { get; set; }
        public string Facing { get; set; } = string.Empty;
        public string Status { get; set; } = "Available"; // Available, Reserved, Sold, Blocked, UnderConstruction, Completed
        public string? Parking { get; set; }
        public string? Offers { get; set; }
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
    }
}