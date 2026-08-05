using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class PartnerCommissionModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int CommissionId { get; set; }
        public int PartnerId { get; set; }
        public int LeadId { get; set; }
        public int? BookingId { get; set; }
        public decimal BookingAmount { get; set; }
        public decimal CommissionPercentage { get; set; }
        public decimal CommissionAmount { get; set; }
        public string Status { get; set; } = "Pending"; // Pending, Approved, Paid
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        public int? ApprovedBy { get; set; }
        public DateTime? ApprovedOn { get; set; }
        public DateTime? PaidOn { get; set; }
        public string? PaymentReference { get; set; }
    }
}