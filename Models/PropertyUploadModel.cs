using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class PropertyUploadModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int UploadId { get; set; }
        
        [Required]
        public int PropertyId { get; set; }
        
        public string? FileName { get; set; }
        
        public byte[]? FileBytes { get; set; }
        
        public string? ContentType { get; set; }
        
        public string? FileType { get; set; }
        
        public DateTime UploadedOn { get; set; } = IndianTime.Now;
        
        public int? UploadedBy { get; set; }
    }
}
