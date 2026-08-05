using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    public class ChannelPartnerDocumentModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int DocumentId { get; set; }

        public int ChannelPartnerId { get; set; }

        // Some deployed schemas keep both foreign key columns as NOT NULL.
        public int PartnerId { get; set; }

        public string FileName { get; set; }
        public string DocumentName { get; set; }
        public string DocumentType { get; set; }

        // Keep mapped because some environments require this non-null column.
        public int DocumentTypeId { get; set; } = 1;

        public string FilePath { get; set; } = "";

        public byte[] FileContent { get; set; }
        public long FileSize { get; set; }
        public string ContentType { get; set; }
        public DateTime UploadedOn { get; set; }
        
        // P0-D3: Document verification
        public string VerificationStatus { get; set; } = "Pending"; // Pending, Approved, Rejected
        [Column("DocumentStatus")]
        public string? DocumentStatus { get; set; }
        public int? VerifiedBy { get; set; }
        public DateTime? VerifiedOn { get; set; }
        public string? RejectionReason { get; set; }
    }
}