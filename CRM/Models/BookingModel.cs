using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.Models
{
    [BsonIgnoreExtraElements]
    public class BookingModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int BookingId { get; set; }
        
        [Required]
        public string BookingNumber { get; set; } = string.Empty;
        
        [Required]
        public int LeadId { get; set; }
        
        [Required]
        public int PropertyId { get; set; }
        
        [Required]
        public int FlatId { get; set; }
        
        public int? QuotationId { get; set; }
        
        public DateTime BookingDate { get; set; } = IndianTime.Now;
        
        [Required]
        public decimal BookingAmount { get; set; }
        
        [Required]
        public decimal TotalAmount { get; set; }

        public decimal RemainingAmount { get; set; }

        [Required]
        public string PaymentType { get; set; } = string.Empty; // FullPayment, EMI
        
        public string Status { get; set; } = "Pending"; // Pending, Confirmed, Cancelled, Completed
        
        public DateTime? AgreementDate { get; set; }
        
        public string? AgreementPath { get; set; }
        
        public DateTime? PossessionDate { get; set; }
        
        public string? Notes { get; set; }
        
        public int? CreatedBy { get; set; }
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        public DateTime? ModifiedOn { get; set; }
        
        public int? ChannelPartnerId { get; set; } // For tracking booking ownership
        
        // P0-D2: Concurrency control to prevent double bookings
        [Timestamp]
        public byte[]? RowVersion { get; set; }
        
        // Navigation properties (ignored by MongoDB serializer)
        [BsonIgnore]
        public LeadModel? Lead { get; set; }
        
        [BsonIgnore]
        public PropertyModel? Property { get; set; }
        
        [BsonIgnore]
        public PropertyFlatModel? Flat { get; set; }
        
        [BsonIgnore]
        public QuotationModel? Quotation { get; set; }
    }
    
    public class BookingDocumentModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int DocumentId { get; set; }
        
        [Required]
        public int BookingId { get; set; }
        
        [Required]
        public string DocumentType { get; set; } = string.Empty;
        
        [Required]
        public string DocumentName { get; set; } = string.Empty;
        
        [Required]
        public string FilePath { get; set; } = string.Empty;
        
        public DateTime UploadedOn { get; set; } = IndianTime.Now;
        
        public int? UploadedBy { get; set; }
    }
}
