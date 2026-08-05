using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    public class AgentCommissionLogModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int CommissionLogId { get; set; }
        
        [Required]
        public int AgentId { get; set; }
        
        [Required]
        public int BookingId { get; set; }
        
        [Required]
        public decimal SaleAmount { get; set; }
        
        [Required]
        public decimal CommissionPercentage { get; set; }
        
        [Required]
        public decimal CommissionAmount { get; set; }
        
        [Required]
        public DateTime SaleDate { get; set; }
        
        [Required]
        public string Month { get; set; } // Format: "Nov-2024"
        
        [Required]
        public int Year { get; set; }
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        // Navigation properties
        public AgentModel? Agent { get; set; }
        
        public BookingModel? Booking { get; set; }

        // MongoDB: navigation properties will be null - use manual lookups
        [global::MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public LeadModel? Lead { get; set; }

        [global::MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public PropertyModel? Property { get; set; }

        [global::MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public PropertyFlatModel? Flat { get; set; }
    }
}