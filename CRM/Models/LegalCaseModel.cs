using System;
using System.ComponentModel.DataAnnotations;
using CRM.Helpers;

namespace CRM.Models
{
    /// <summary>
    /// Legal case / agreement verification tracking for bookings.
    /// </summary>
    public class LegalCaseModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int CaseId { get; set; }

        [Required]
        public int BookingId { get; set; }

        public int? LeadId { get; set; }

        public string? CaseType { get; set; } // Agreement, Registration, Verification, Compliance

        public string? Title { get; set; }

        public string? Status { get; set; } = "Pending"; // Pending, InReview, Approved, Rejected

        public string? AssignedTo { get; set; } // Legal team member name/id

        public string? DocumentRefs { get; set; } // comma separated document references

        public string? Notes { get; set; }

        public DateTime? DueDate { get; set; }

        public DateTime CreatedOn { get; set; } = IndianTime.Now;

        public DateTime? UpdatedOn { get; set; }
    }
}