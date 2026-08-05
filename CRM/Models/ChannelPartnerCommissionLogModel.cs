using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    public class ChannelPartnerCommissionLogModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int CommissionLogId { get; set; }
        
        [Required]
        public int PartnerId { get; set; }
        
        [Required]
        public int BookingId { get; set; }

        public int? LeadId { get; set; }

        public decimal? CommissionPercentage { get; set; }

        public string? Description { get; set; }

        public string? Status { get; set; }

        [Required]
        public decimal FixedCommissionAmount { get; set; }
        
        [Required]
        public DateTime SaleDate { get; set; }
        
        [Required]
        public string Month { get; set; } // Format: "Nov-2024"
        
        [Required]
        public int Year { get; set; }
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        // Navigation properties
        public ChannelPartnerModel? Partner { get; set; }
        
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