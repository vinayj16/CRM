namespace CRM.Models
{
    public class InquiryFormModel
    {
        public string CompanyName { get; set; } = "";
        public string ContactPerson { get; set; } = "";
        public string Email { get; set; } = "";
        public string? Phone { get; set; }
        public string? Message { get; set; }

        public string? SelectedPlan { get; set; }
        public int? SelectedPlanId { get; set; }
        public string? SelectedPlanName { get; set; }

        public string ReferralCode { get; set; }
    }
}