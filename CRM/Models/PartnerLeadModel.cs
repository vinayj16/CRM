using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    public class PartnerLeadModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int LeadId { get; set; }
        public int PartnerId { get; set; }
        public string? LeadName { get; set; }
        public string? Contact { get; set; }
        public string? Email { get; set; }
        public string? Location { get; set; }
        public string? Stage { get; set; }
        public string Status { get; set; } = "New";
        public string? Source { get; set; }
        public string? Type { get; set; }
        public string? PropertyInterest { get; set; }
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        public bool ConvertedToSale { get; set; } = false;
        public decimal? CommissionAmount { get; set; }
        
        // Navigation properties
        public LeadModel? Lead { get; set; }
        
        public ChannelPartnerModel? Partner { get; set; }
    }
}