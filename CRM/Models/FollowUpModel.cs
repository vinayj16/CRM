using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class FollowUpModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int FollowUpId { get; set; }
        public int LeadId { get; set; }
        public string? Stage { get; set; }
        public string? Status { get; set; }
        public DateTime? FollowUpDate { get; set; }
        public string? FollowUpTime { get; set; }
        public string Comments { get; set; } = string.Empty;
        public int ExecutiveId { get; set; }
        public int? PropertyId { get; set; } // New field - which property was visited
        public string? InterestStatus { get; set; } // New field - Interested/Not Interested/Cold
        public string? Rating { get; set; } // 1-5 star rating
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        // P1-L6: Follow-up completion tracking
        public DateTime? CompletedOn { get; set; }
        public int? CompletedBy { get; set; }
        [StringLength(500)]
        public string? CompletionNotes { get; set; }
        
        // Notification tracking
        public bool? IsNotificationRead { get; set; } = false;
    }
}
