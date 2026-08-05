using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class PropertyHistoryModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int HistoryId { get; set; }
        
        [Required]
        public int PropertyId { get; set; }
        
        public string? Activity { get; set; }
        
        public DateTime ActivityDate { get; set; } = IndianTime.Now;
        
        public int? ExecutiveId { get; set; }
    }
}
