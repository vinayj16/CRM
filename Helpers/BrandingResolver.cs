using CRM.Models;

namespace CRM.Helpers
{
    public static class BrandingResolver
    {
        /// <summary>
        /// Resolve the company logo. Settings are scoped by tenant (TenantId) and optionally by
        /// channel partner (ChannelPartnerId). If tenantId is provided (&gt; 0), only that tenant's
        /// setting is used so each company sees its own branding.
        /// </summary>
        public static string? ResolveCompanyLogo(AppDbContext db, int? channelPartnerId = null, int? tenantId = null)
        {
            var setting = FindScopedSetting(db, "CompanyLogo", channelPartnerId, tenantId);
            if (!string.IsNullOrWhiteSpace(setting?.SettingValue))
            {
                return setting.SettingValue;
            }

            var branding = db.Branding.FirstOrDefault();
            return !string.IsNullOrWhiteSpace(branding?.CompanyLogo) ? branding.CompanyLogo : null;
        }

        public static string? ResolveCollapsedLogo(AppDbContext db, int? channelPartnerId = null, int? tenantId = null)
        {
            var setting = FindScopedSetting(db, "CollapsedLogo", channelPartnerId, tenantId);
            return !string.IsNullOrWhiteSpace(setting?.SettingValue) ? setting.SettingValue : null;
        }

        public static string ResolveCompanyName(AppDbContext db, int? channelPartnerId = null, int? tenantId = null, string? fallbackName = null)
        {
            var setting = FindScopedSetting(db, "CompanyName", channelPartnerId, tenantId);
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

        /// <summary>
        /// Look up a setting scoped by tenant first (when a tenant id is known), then by channel
        /// partner. Never leaks another tenant's value: when tenantId is supplied we only match
        /// that tenant (or settings with no tenant assignment as a legacy fallback).
        /// </summary>
        private static SettingsModel? FindScopedSetting(AppDbContext db, string key, int? channelPartnerId, int? tenantId)
        {
            var candidates = db.Settings.Where(s => s.SettingKey == key);

            if (tenantId.HasValue && tenantId.Value > 0)
            {
                // Exact tenant-scoped row first.
                var scoped = candidates.FirstOrDefault(s => s.TenantId == tenantId.Value
                    && (channelPartnerId.HasValue && channelPartnerId.Value > 0 ? s.ChannelPartnerId == channelPartnerId.Value : s.ChannelPartnerId == null));
                if (scoped != null)
                {
                    return scoped;
                }

                // Legacy fallback: an unassigned (TenantId == 0) row of the same scope.
                return candidates.FirstOrDefault(s => s.TenantId == 0
                    && (channelPartnerId.HasValue && channelPartnerId.Value > 0 ? s.ChannelPartnerId == channelPartnerId.Value : s.ChannelPartnerId == null));
            }

            // No tenant context (e.g. pre-login page without a subdomain): behave like before.
            return candidates.FirstOrDefault(s => channelPartnerId.HasValue && channelPartnerId.Value > 0
                ? s.ChannelPartnerId == channelPartnerId.Value
                : s.ChannelPartnerId == null);
        }
    }
}
