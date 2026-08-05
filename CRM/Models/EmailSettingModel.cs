using CRM.Helpers;
using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class EmailSettingModel
    {
        [Key]
        public int EmailSettingId { get; set; }
        public int UserId { get; set; }
        public string SmtpFrom { get; set; }
        public string SmtpPassword { get; set; }
        public string SmtpHost { get; set; } = "smtp.gmail.com";
        public int SmtpPort { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        public DateTime? UpdatedOn { get; set; }
    }
}
