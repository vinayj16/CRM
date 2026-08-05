using System;
using System.ComponentModel.DataAnnotations;
using CRM.Helpers;

namespace CRM.Models
{
    /// <summary>
    /// Marketing campaign (SMS, Email, WhatsApp, Facebook, Google Ads, etc.).
    /// </summary>
    public class CampaignModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int CampaignId { get; set; }

        [Required]
        public string? CampaignName { get; set; }

        public string? Channel { get; set; } // Email, SMS, WhatsApp, Facebook, GoogleAds

        public string? Status { get; set; } = "Draft"; // Draft, Active, Paused, Completed

        public DateTime? StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public decimal? Budget { get; set; }

        public int? Clicks { get; set; } = 0;

        public int? LeadsGenerated { get; set; } = 0;

        public int? Conversions { get; set; } = 0;

        public decimal? CostPerLead { get; set; }

        public decimal? ROI { get; set; }

        public string? AudienceFilter { get; set; } // JSON filter definition

        public string? MessageTemplate { get; set; }

        public int? CreatedBy { get; set; }

        public DateTime CreatedOn { get; set; } = IndianTime.Now;

        public DateTime? UpdatedOn { get; set; }
    }
}