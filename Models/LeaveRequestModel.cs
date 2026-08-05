using CRM.Helpers;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    /// <summary>
    /// Leave request model for agent leave management (P1-AT3)
    /// </summary>
    public class LeaveRequestModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int LeaveRequestId { get; set; }
        
        [Required]
        public int AgentId { get; set; }
        
        [Required]
        [StringLength(50)]
        public string LeaveType { get; set; } = string.Empty; // Sick, Casual, Emergency, Vacation
        
        [Required]
        public DateTime FromDate { get; set; }
        
        [Required]
        public DateTime ToDate { get; set; }
        
        [Required]
        [StringLength(500)]
        public string Reason { get; set; } = string.Empty;
        
        [StringLength(20)]
        public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected
        
        public DateTime RequestedOn { get; set; } = IndianTime.Now;
        
        public int? ApprovedBy { get; set; }
        public DateTime? ApprovedOn { get; set; }
        
        [StringLength(500)]
        public string? RejectionReason { get; set; }
        
        // Navigation properties
        public virtual UserModel? Agent { get; set; }
        
        public virtual UserModel? Approver { get; set; }
        
        // Calculated property
        [NotMapped]
        public int TotalDays => (ToDate - FromDate).Days + 1;
    }
}
