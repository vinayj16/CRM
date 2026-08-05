//using CRM.MasterDb;
//
//namespace CRM.Middleware
//{
//    public class SubdomainMiddleware
//    {
//        private readonly RequestDelegate _next;

//        public SubdomainMiddleware(RequestDelegate next)
//        {
//            _next = next;
//        }

//        public async Task InvokeAsync(HttpContext context)
//        {
//            var host = context.Request.Host.Host.ToLower();

//            // Extract subdomain
//            string? subdomain = null;

//            if (host.Contains(".localhost"))
//            {
//                // Local: proptech.localhost ? "proptech"
//                subdomain = host.Split('.')[0];
//                if (subdomain == "localhost") subdomain = null;
//            }
//            else
//            {
//                // Production: proptech.uproptech.com ? "proptech"
//                var parts = host.Split('.');
//                if (parts.Length >= 3)
//                {
//                    subdomain = parts[0];
//                    if (subdomain == "www" || subdomain == "admin" || subdomain == "mail" || subdomain == "api")
//                        subdomain = null;
//                }
//            }

//            if (!string.IsNullOrEmpty(subdomain))
//            {
//                using var scope = context.RequestServices.CreateScope();
//                var masterDb = scope.ServiceProvider.GetRequiredService<MasterDbContext>();

//                var tenant = await masterDb.Tenants
//                    .AsNoTracking()
//                    .FirstOrDefaultAsync(t => t.Subdomain == subdomain && t.IsActive && !t.IsSuspended);

//                if (tenant != null)
//                {
//                    context.Items["TenantId"] = tenant.TenantId;
//                    context.Items["TenantName"] = tenant.CompanyName;
//                    context.Items["TenantConnectionString"] = tenant.ConnectionString;
//                    context.Items["Subdomain"] = subdomain;
//                }

//                // Also check legacy ChannelPartners for backward compatibility
//                if (tenant == null)
//                {
//                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

//                    var partner = await db.ChannelPartners
//                        .AsNoTracking()
//                        .FirstOrDefaultAsync(p => p.Subdomain == subdomain && p.Status != "Deleted");

//                    if (partner != null)
//                    {
//                        context.Items["SubdomainPartnerId"] = partner.PartnerId;
//                        context.Items["SubdomainPartnerName"] = partner.CompanyName;
//                        context.Items["Subdomain"] = subdomain;
//                    }
//                }
//            }

//            await _next(context);
//        }
//    }

//    public static class SubdomainMiddlewareExtensions
//    {
//        public static IApplicationBuilder UseSubdomainDetection(this IApplicationBuilder builder)
//        {
//            return builder.UseMiddleware<SubdomainMiddleware>();
//        }
//    }
//}
using CRM.MasterDb;

namespace CRM.Middleware
{
    public class SubdomainMiddleware
    {
        private readonly RequestDelegate _next;

        public SubdomainMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var host = context.Request.Host.Host.ToLower();

            // Extract subdomain
            string? subdomain = null;

            if (host.Contains(".localhost"))
            {
                // Local: companya.localhost -> "companya"
                subdomain = host.Split('.')[0];
                if (subdomain == "localhost") subdomain = null;
            }
            else
            {
                // Production: companya.yourcrm.com -> "companya"
                var parts = host.Split('.');
                if (parts.Length >= 3)
                {
                    subdomain = parts[0];
                    if (subdomain == "www" || subdomain == "admin" || subdomain == "mail" || subdomain == "api")
                        subdomain = null;
                }
            }

            if (!string.IsNullOrEmpty(subdomain))
            {
                using var scope = context.RequestServices.CreateScope();
                var masterDb = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
                var tenant = await masterDb.Tenants
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Subdomain == subdomain && t.IsActive && !t.IsSuspended);

                if (tenant != null)
                {
                    context.Items["TenantId"] = tenant.TenantId;
                    context.Items["TenantName"] = tenant.CompanyName;
                    context.Items["TenantConnectionString"] = tenant.ConnectionString;
                    context.Items["Subdomain"] = subdomain;
                }

                // Also check legacy ChannelPartners for backward compatibility
                if (tenant == null)
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var partner = await db.ChannelPartners
                        .AsNoTracking()
                        .FirstOrDefaultAsync(p => p.Subdomain == subdomain && p.Status != "Deleted");

                    if (partner != null)
                    {
                        context.Items["SubdomainPartnerId"] = partner.PartnerId;
                        context.Items["SubdomainPartnerName"] = partner.CompanyName;
                        context.Items["Subdomain"] = subdomain;
                    }
                }
            }

            await _next(context);
        }
    }

    public static class SubdomainMiddlewareExtensions
    {
        public static IApplicationBuilder UseSubdomainDetection(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<SubdomainMiddleware>();
        }
    }
}