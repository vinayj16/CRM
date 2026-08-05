using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class LeadUploadModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int UploadId { get; set; }

        public int? LeadId { get; set; }

        public string? FilePath { get; set; }
        public string? FileType { get; set; }
        public int? UploadedBy { get; set; }

        public DateTime UploadedOn { get; set; } = IndianTime.Now;

        public byte[]? FileBytes { get; set; }
        public string? FileName { get; set; }
        public string? ContentType { get; set; }
    }

}
