using CRM.Helpers;
using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class EmailLogModel
    {
        [Key]
        public int LogId { get; set; }
        public string? ToEmail { get; set; }
        public string? Subject { get; set; }
        public string? BodyPreview { get; set; }
        public string? TemplateName { get; set; }
        public int? UserId { get; set; }
        public int? TenantId { get; set; }
        public string? SentByUser { get; set; }
        public string? SentByRole { get; set; }
        public string Status { get; set; } = "Sent";
        public string? ErrorMessage { get; set; }
        public DateTime SentOn { get; set; } = IndianTime.Now;
        public string? Category { get; set; }
    }
}
