using CRM.Helpers;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    /// <summary>
    /// Email template model for dynamic email content (P2-N2)
    /// </summary>
    public class EmailTemplateModel
    {
        [Key]
        public int TemplateId { get; set; }
        
        [Required]
        [StringLength(100)]
        public string TemplateName { get; set; } = string.Empty;
        
        [Required]
        [StringLength(200)]
        public string Subject { get; set; } = string.Empty;
        
        [Required]
        public string BodyHtml { get; set; } = string.Empty;
        
        [StringLength(500)]
        public string? Variables { get; set; } // Comma-separated: {Name}, {Link}, etc.
        
        public bool IsActive { get; set; } = true;
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        public DateTime UpdatedOn { get; set; } = IndianTime.Now;
    }
}
