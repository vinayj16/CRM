using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class LeadHistoryModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int HistoryId { get; set; }
        public int LeadId { get; set; }
        public string Activity { get; set; }
        public DateTime ActivityDate { get; set; } = IndianTime.Now;
        public int? ExecutiveId { get; set; }
    }
}
