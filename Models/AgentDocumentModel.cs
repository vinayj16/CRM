using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    public class AgentDocumentModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int DocumentId { get; set; }
        
        public int AgentId { get; set; }
        
        [Required]
        public string FileName { get; set; }
        
        [Required]
        public string DocumentName { get; set; }
        
        public string DocumentType { get; set; } // e.g., "Aadhar", "PAN", "Resume", "Other"
        
        [Required]
        public byte[] FileContent { get; set; }  // Binary file content stored in database
        
        public long FileSize { get; set; }
        
        [Required]
        public string ContentType { get; set; }  // MIME type (e.g., "application/pdf", "image/jpeg")
        
        public DateTime UploadedOn { get; set; } = IndianTime.Now;
        
        public int? UploadedBy { get; set; }
        
        // P0-D3: Document verification
        public string VerificationStatus { get; set; } = "Pending"; // Pending, Approved, Rejected
        
        public int? VerifiedBy { get; set; }
        
        public DateTime? VerifiedOn { get; set; }
        
        public string? RejectionReason { get; set; }
        
        public virtual AgentModel Agent { get; set; }
    }
}
