namespace CRM.ViewModels
{
    public class CrmPlanViewModel
    {
        public SubscriptionInfo? CurrentSubscription { get; set; }
        public List<PlanInfo> AvailablePlans { get; set; } = new();
        public string TenantName { get; set; } = string.Empty;
        public bool HasSubscription => CurrentSubscription != null;
    }

    public class SubscriptionInfo
    {
        public string PlanName { get; set; } = string.Empty;
        public string BillingCycle { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public int PlanId { get; set; }
        public int DaysRemaining => Math.Max(0, (EndDate - DateTime.UtcNow).Days);
        public bool IsExpired => DaysRemaining <= 0;
        public bool IsTrial => BillingCycle == "Trial";
    }

    public class PlanInfo
    {
        public int PlanId { get; set; }
        public string PlanName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal MonthlyPrice { get; set; }
        public decimal YearlyPrice { get; set; }
        public int MaxUsers { get; set; }
        public int MaxAgents { get; set; }
        public int MaxLeadsPerMonth { get; set; }
        public int MaxPartners { get; set; }
        public bool HasWhatsAppIntegration { get; set; }
        public bool HasFacebookIntegration { get; set; }
        public bool HasCustomAPIAccess { get; set; }
        public bool HasPrioritySupport { get; set; }
        public string SupportLevel { get; set; } = "Email";
        public bool IsCurrent { get; set; }
    }
}
