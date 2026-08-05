using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class UserDashboardSetting
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public int UserId { get; set; }

        // Admin Dashboard Toggles
        public bool ShowStatsCards { get; set; } = true;
        public bool ShowLeadGrowthChart { get; set; } = true;
        public bool ShowTrafficSources { get; set; } = true;
        public bool ShowSalesPipeline { get; set; } = true;
        public bool ShowLeadsList { get; set; } = true;
        public bool ShowRevenueExpensesChart { get; set; } = true;
        public bool ShowTransactionsList { get; set; } = true;
        public bool ShowQuickAccess { get; set; } = true;
        public bool ShowPlanUsage { get; set; } = true;

        // Sales Dashboard Toggles
        public bool ShowSalesStats { get; set; } = true;
        public bool ShowSalesChart { get; set; } = true;
        public bool ShowSalesStatus { get; set; } = true;
        public bool ShowSalesBookings { get; set; } = true;

        // Partner Dashboard Toggles
        public bool ShowPartnerStats { get; set; } = true;
        public bool ShowPartnerLeadChart { get; set; } = true;
        public bool ShowPartnerLeadStatus { get; set; } = true;
        public bool ShowPartnerCommissions { get; set; } = true;

        // General
        public bool ShowUpcomingFollowups { get; set; } = true;
        public bool ShowRecentActivities { get; set; } = true;
        public bool ShowTeamPerformance { get; set; } = true;
        public bool ShowTopPerformers { get; set; } = true;

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }
    }
}
