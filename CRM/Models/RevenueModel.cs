using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class RevenueModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int RevenueId { get; set; }
        public string Type { get; set; } // e.g. Sale, Booking, Rental, Service
        public string? Source { get; set; }
        public string Description { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date { get; set; } = IndianTime.Now;
        public int? ChannelPartnerId { get; set; }
    }
}
