using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    public class PropertyDocumentModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int DocumentId { get; set; }
        
        [Required]
        public int PropertyId { get; set; }
        
        public string? DocumentType { get; set; }
        
        public string? FileName { get; set; }
        
        public byte[]? FileBytes { get; set; }
        
        public string? ContentType { get; set; }
        
        public DateTime UploadedOn { get; set; } = IndianTime.Now;
        
        public int? UploadedBy { get; set; }
        
        public virtual PropertyModel Property { get; set; }
    }
}
