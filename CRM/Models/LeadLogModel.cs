using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class LeadLogModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int LogId { get; set; }
        public int LeadId { get; set; }
        public string LogText { get; set; }
        public DateTime LogDate { get; set; } = IndianTime.Now;
        public int? ExecutiveId { get; set; }
    }
}
