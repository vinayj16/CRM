using System;
using System.ComponentModel.DataAnnotations;
using CRM.Helpers;

namespace CRM.Models
{
    /// <summary>
    /// AI-style lead score record (0-100) computed from lead attributes.
    /// </summary>
    public class LeadScoreModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int ScoreId { get; set; }

        [Required]
        public int LeadId { get; set; }

        public int Score { get; set; } = 0; // 0-100

        [StringLength(20)]
        public string Grade { get; set; } = "Cold"; // Hot, Warm, Cold

        public string? Reasons { get; set; }

        public DateTime ComputedOn { get; set; } = IndianTime.Now;
    }
}