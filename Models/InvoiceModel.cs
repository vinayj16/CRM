using CRM.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    public class InvoiceModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int InvoiceId { get; set; }
        
        [Required]
        public string InvoiceNumber { get; set; } = string.Empty;
        
        [Required]
        public int BookingId { get; set; }
        
        public int? InstallmentId { get; set; }
        
        public DateTime InvoiceDate { get; set; } = IndianTime.Now;
        
        [Required]
        public DateTime DueDate { get; set; }
        
        [Required]
        public decimal Amount { get; set; }
        
        public decimal TaxAmount { get; set; } = 0;
        
        [Required]
        public decimal TotalAmount { get; set; }
        
        public decimal PaidAmount { get; set; } = 0;
        
        public string Status { get; set; } = "Generated"; // Generated, Sent, Paid, Partial, Overdue, Cancelled
        
        public string? Notes { get; set; }
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        public DateTime? ModifiedOn { get; set; }
        
        // Navigation properties
        public BookingModel? Booking { get; set; }
        
        public PaymentInstallmentModel? Installment { get; set; }
        
        // MongoDB: navigation properties will be null - use manual lookups
        [global::MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public LeadModel? Lead { get; set; }
        
        // MongoDB: navigation properties will be null - use manual lookups
        [global::MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public PropertyModel? Property { get; set; }

        [global::MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public PropertyFlatModel? Flat { get; set; }

        public List<InvoiceItemModel>? Items { get; set; }
    }

    public class InvoiceItemModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int ItemId { get; set; }

        [Required]
        public int InvoiceId { get; set; }

        public InvoiceModel Invoice { get; set; }

        [Required]
        public string Description { get; set; } = string.Empty;

        public int Quantity { get; set; } = 1;

        [Required]
        public decimal Rate { get; set; }

        [Required]
        public decimal Amount { get; set; }
    }
}
