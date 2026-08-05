using System;
using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class AgentAttendanceModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int AttendanceId { get; set; }
        public int AgentId { get; set; }
        public DateTime Date { get; set; }
        public DateTime? LoginTime { get; set; }
        public DateTime? LogoutTime { get; set; }
        public string Status { get; set; } // Present, Absent, Holiday, Pending
        public bool CorrectionRequested { get; set; } = false;
        public string? CorrectionReason { get; set; }
        public string? CorrectionStatus { get; set; } // Pending, Approved, Rejected
        public int? ApprovedBy { get; set; }
        public DateTime? ApprovedOn { get; set; }
    }
}