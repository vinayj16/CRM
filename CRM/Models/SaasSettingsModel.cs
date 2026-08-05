using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class SaasSettingsModel
    {
        [Key]
        public int SettingId { get; set; }

        [Required]
        public string SettingKey { get; set; }

        public string? SettingValue { get; set; }

        public string? Description { get; set; }

        public string? SettingType { get; set; }

        public int? ChannelPartnerId { get; set; }

        public int? ModifiedBy { get; set; }

        public DateTime? ModifiedOn { get; set; }
    }
}