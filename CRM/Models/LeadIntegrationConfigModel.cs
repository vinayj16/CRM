using CRM.Helpers;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    [Table("LeadIntegrationConfigs")]
    public class LeadIntegrationConfigModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string PlatformName { get; set; } = string.Empty; // GoogleAds, 99Acres, HousingCom, MagicBricks, CommonFloor, Facebook, JustDial, Sulekha, IndiaMART, TradeIndia, OLX

        [StringLength(500)]
        public string? ApiKey { get; set; }

        [StringLength(500)]
        public string? ApiSecret { get; set; }

        [StringLength(500)]
        public string? AccountId { get; set; }

        [StringLength(1000)]
        public string? WebhookUrl { get; set; }

        [StringLength(500)]
        public string? AccessToken { get; set; }

        [StringLength(500)]
        public string? RefreshToken { get; set; }

        [StringLength(500)]
        public string? ProjectId { get; set; }

        [StringLength(500)]
        public string? CampaignId { get; set; }

        [StringLength(2000)]
        public string? ExtraConfig { get; set; } // JSON for any additional platform-specific config

        public bool IsEnabled { get; set; } = false;

        public int PollIntervalMinutes { get; set; } = 5;

        public DateTime? LastSyncedAt { get; set; }

        public int LeadsSynced { get; set; } = 0;

        public int? ChannelPartnerId { get; set; } // null = Admin config

        [StringLength(20)]
        public string ConfigScope { get; set; } = "Admin"; // Admin, Partner

        public DateTime CreatedOn { get; set; } = IndianTime.Now;

        public DateTime? UpdatedOn { get; set; }
    }
}
