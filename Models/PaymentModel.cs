using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    public class PaymentModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int PaymentId { get; set; }
        
        [Required]
        public string ReceiptNumber { get; set; } = string.Empty;
        
        [Required]
        public int InvoiceId { get; set; }
        
        [Required]
        public int BookingId { get; set; }
        
        public int? InstallmentId { get; set; }
        
        public DateTime PaymentDate { get; set; } = IndianTime.Now;
        
        [Required]
        public decimal Amount { get; set; }
        
        [Required]
        public string PaymentMethod { get; set; } = string.Empty; // Cash, Cheque, UPI, NEFT, RTGS, Card
        
        public string? TransactionReference { get; set; }
        
        public string? BankName { get; set; }
        
        public string? ChequeNumber { get; set; }
        
        public DateTime? ChequeDate { get; set; }
        
        public string? Notes { get; set; }

        public string? Status { get; set; }

        public string? Description { get; set; }

        public int? ReceivedBy { get; set; }
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        // Navigation properties
        public InvoiceModel? Invoice { get; set; }
        
        public BookingModel? Booking { get; set; }
        
        public PaymentInstallmentModel? Installment { get; set; }
        
        // MongoDB: navigation properties will be null - use manual lookups
        [global::MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public PropertyModel? Property { get; set; }

        [global::MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public PropertyFlatModel? Flat { get; set; }
        
        // MongoDB: navigation properties will be null - use manual lookups
        [global::MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public LeadModel? Lead { get; set; }
    }
}
