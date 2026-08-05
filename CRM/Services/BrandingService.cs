using CRM.Models;

namespace CRM.Services
{
    public class BrandingService
    {
        private readonly AppDbContext _context;

        public BrandingService(AppDbContext context)
        {
            _context = context;
        }

        public BrandingModel GetBrandingData()
        {
            return _context.Branding.FirstOrDefault() ?? new BrandingModel
            {
                CompanyInfo = "Your Company Name<br/>Gachhibowli, Hyderabad<br/>Old Mumbai Highway 500032<br/>+91-9876543210<br/>info@company.com",
                AboutUsText = "We are a leading real estate company committed to providing exceptional service and helping you find your dream property."
            };
        }

        public bool HasSocialMediaLinks()
        {
            var branding = GetBrandingData();
            return !string.IsNullOrEmpty(branding.TwitterUrl) ||
                   !string.IsNullOrEmpty(branding.FacebookUrl) ||
                   !string.IsNullOrEmpty(branding.InstagramUrl) ||
                   !string.IsNullOrEmpty(branding.LinkedInUrl) ||
                   !string.IsNullOrEmpty(branding.WhatsAppNumber);
        }

        public Dictionary<string, string> GetSocialMediaLinks()
        {
            var branding = GetBrandingData();
            var links = new Dictionary<string, string>();

            if (!string.IsNullOrEmpty(branding.TwitterUrl))
                links.Add("twitter", branding.TwitterUrl);
            
            if (!string.IsNullOrEmpty(branding.FacebookUrl))
                links.Add("facebook", branding.FacebookUrl);
            
            if (!string.IsNullOrEmpty(branding.InstagramUrl))
                links.Add("instagram", branding.InstagramUrl);
            
            if (!string.IsNullOrEmpty(branding.LinkedInUrl))
                links.Add("linkedin", branding.LinkedInUrl);
            
            if (!string.IsNullOrEmpty(branding.WhatsAppNumber))
                links.Add("whatsapp", $"https://wa.me/{branding.WhatsAppNumber.Replace("+", "").Replace("-", "").Replace(" ", "")}");

            return links;
        }
    }
}