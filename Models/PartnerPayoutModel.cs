using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    public class PartnerPayoutModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int PayoutId { get; set; }
        
        [Required]
        public int PartnerId { get; set; }
        
        [Required]
        public string? Month { get; set; } // Format: "Nov-2024"
        
        [Required]
        public int Year { get; set; }
        
        public decimal FixedCommissionPerSale { get; set; } = 0;
        
        public int TotalSales { get; set; } = 0;
        
        public int TotalLeads { get; set; } = 0;
        
        public int ConvertedLeads { get; set; } = 0;
        
        public decimal TotalCommission { get; set; } = 0;
        
        public decimal Amount { get; set; } = 0; // Total payout amount
        
        public string? Status { get; set; } = "Pending"; // Pending, Processed, Paid
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        public DateTime? ProcessedOn { get; set; }
        
        // Navigation properties
        public ChannelPartnerModel? Partner { get; set; }
    }
}