using System;
using System.ComponentModel.DataAnnotations;
using CRM.Helpers;

namespace CRM.Models
{
    public class SiteVisitModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int SiteVisitId { get; set; }
        public int LeadId { get; set; }
        public string? LeadName { get; set; }
        public int? ExecutiveId { get; set; }
        public string? ExecutiveName { get; set; }
        public int? PropertyId { get; set; }
        public string? PropertyName { get; set; }
        public DateTime ScheduledDate { get; set; } = IndianTime.Now;
        public string? TimeSlot { get; set; } // e.g. "10:00 AM"
        public string Status { get; set; } = "Scheduled"; // Scheduled, Completed, Cancelled, No-Show
        public string? Vehicle { get; set; }
        public string? DriverName { get; set; }
        public string? CheckInLocation { get; set; } // GPS
        public string? CheckOutLocation { get; set; }
        public DateTime? CheckInTime { get; set; }
        public DateTime? CheckOutTime { get; set; }
        public string? Feedback { get; set; }
        public int? Rating { get; set; } // 1-5
        public string? Photos { get; set; } // comma separated urls/base64
        public string? Notes { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        public DateTime? UpdatedOn { get; set; }
    }
}