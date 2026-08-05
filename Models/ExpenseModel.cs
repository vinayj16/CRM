using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class ExpenseModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int ExpenseId { get; set; }
        public string Type { get; set; } // e.g. Land, Construction, Legal, Marketing, Agent, Tax, Maintenance
        public string? Category { get; set; }
        public string Description { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date { get; set; } = IndianTime.Now;
        public int? ChannelPartnerId { get; set; }
    }
}
