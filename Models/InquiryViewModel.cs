using CRM.MasterDb.Models;

namespace CRM.Models
{
    public class InquiryViewModel
    {
        public InquiryModel Inquiry { get; set; }
        public string ReferralCompany { get; set; }
        public string SelectedPlanDisplay => Inquiry?.SelectedPlanName ?? Inquiry?.SelectedPlan ?? "Not specified";
    }

}
