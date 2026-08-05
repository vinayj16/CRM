using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class MaintenanceLogModel
    {
        [Key]
        public int LogId { get; set; }
        public bool IsEnabled { get; set; }
        public string? Message { get; set; }
        public DateTime StartedOn { get; set; }
        public DateTime? EndedOn { get; set; }
        public string? SetBy { get; set; }
        public DateTime CreatedOn { get; set; }
    }
}
