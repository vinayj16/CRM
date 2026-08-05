using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class LeadHandoverAuditModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int AuditId { get; set; }
        public int LeadId { get; set; }
        public string? FromStatus { get; set; }
        public string ToStatus { get; set; }
        public DateTime HandoverDate { get; set; } = IndianTime.Now;
        public int HandedOverBy { get; set; } // Partner UserId
        public int? AssignedTo { get; set; } // Admin Agent UserId
        public string? Notes { get; set; }
    }
}