using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class BuilderModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int BuilderId { get; set; }
        
        [Required]
        public string BuilderName { get; set; } = string.Empty;
        
        public string? ContactPerson { get; set; }
        
        public string? Email { get; set; }
        
        public string? Phone { get; set; }
        
        public string? Address { get; set; }
        
        public string? Website { get; set; }
        
        public byte[]? Logo { get; set; }
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        public int? CreatedBy { get; set; }
        
        public bool IsActive { get; set; } = true;
    }
}
