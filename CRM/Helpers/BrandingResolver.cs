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

        public static string ResolveCompanyName(AppDbContext db, int? channelPartnerId = null, string? fallbackName = null)
        {
            var setting = db.Settings.FirstOrDefault(s => s.SettingKey == "CompanyName" && s.ChannelPartnerId == channelPartnerId);
            if (!string.IsNullOrWhiteSpace(setting?.SettingValue))
            {
                return setting.SettingValue;
            }

            return !string.IsNullOrWhiteSpace(fallbackName) ? fallbackName : "PropTech CRM";
        }
    }
}
