using CRM.Models;

namespace CRM.Helpers
{
    public static class BrandingResolver
    {
        public static string? ResolveCompanyLogo(AppDbContext db, int? channelPartnerId = null)
        {
            var setting = db.Settings.FirstOrDefault(s => s.SettingKey == "CompanyLogo" && s.ChannelPartnerId == channelPartnerId);
            if (!string.IsNullOrWhiteSpace(setting?.SettingValue))
            {
                return setting.SettingValue;
            }

            var branding = db.Branding.FirstOrDefault();
            return !string.IsNullOrWhiteSpace(branding?.CompanyLogo) ? branding.CompanyLogo : null;
        }

        public static string? ResolveCollapsedLogo(AppDbContext db, int? channelPartnerId = null)
        {
            var setting = db.Settings.FirstOrDefault(s => s.SettingKey == "CollapsedLogo" && s.ChannelPartnerId == channelPartnerId);
            return !string.IsNullOrWhiteSpace(setting?.SettingValue) ? setting.SettingValue : null;
        }

        public static string ResolveCompanyName(AppDbContext db, int? channelPartnerId = null, string? fallbackName = null)
        {
            var setting = db.Settings.FirstOrDefault(s => s.SettingKey == "CompanyName" && s.ChannelPartnerId == channelPartnerId);
            if (!string.IsNullOrWhiteSpace(setting?.SettingValue))
            {
                return setting.SettingValue;
            }

            // Partner without a CompanyName setting: fall back to the partner's own company name
            if (channelPartnerId is > 0)
            {
                var partner = db.ChannelPartners.FirstOrDefault(cp => cp.PartnerId == channelPartnerId);
                if (partner != null && !string.IsNullOrWhiteSpace(partner.CompanyName))
                {
                    return partner.CompanyName;
                }
            }

            return !string.IsNullOrWhiteSpace(fallbackName) ? fallbackName : "PropTech CRM";
        }
    }
}
