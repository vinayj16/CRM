using CRM.Helpers;
using CRM.MasterDb;

namespace CRM.Middleware
{
    public class MaintenanceModeMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<MaintenanceModeMiddleware> _logger;

        public MaintenanceModeMiddleware(RequestDelegate next, ILogger<MaintenanceModeMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, MasterDbContext masterDb)
        {
            var path = context.Request.Path.Value?.ToLower() ?? "";

            // Always allow static files, login, and maintenance settings pages
            if (ShouldSkip(path))
            {
                await _next(context);
                return;
            }

            // Check if maintenance mode is enabled in SaasSetting
            var maintenanceSetting = await masterDb.SaasSetting
                .FirstOrDefaultAsync(s => s.SettingKey == "MaintenanceMode");

            if (maintenanceSetting == null || maintenanceSetting.SettingValue != "true")
            {
                await _next(context);
                return;
            }

            // Get maintenance message and schedule
            var maintenanceMsgSetting = await masterDb.SaasSetting
                .FirstOrDefaultAsync(s => s.SettingKey == "MaintenanceMessage");
            var startDateSetting = await masterDb.SaasSetting
                .FirstOrDefaultAsync(s => s.SettingKey == "MaintenanceStartDate");
            var endDateSetting = await masterDb.SaasSetting
                .FirstOrDefaultAsync(s => s.SettingKey == "MaintenanceEndDate");

            var message = maintenanceMsgSetting?.SettingValue ?? "System is under maintenance. Please check back later.";

            // Check if within scheduled window (if dates are set)
            bool withinSchedule = true;
            var now = IndianTime.Now;
            if (!string.IsNullOrEmpty(startDateSetting?.SettingValue) && DateTime.TryParse(startDateSetting.SettingValue, out var startDt))
            {
                if (now < startDt)
                {
                    withinSchedule = false; // Maintenance hasn't started yet
                }
            }
            if (!string.IsNullOrEmpty(endDateSetting?.SettingValue) && DateTime.TryParse(endDateSetting.SettingValue, out var endDt))
            {
                if (now > endDt)
                {
                    withinSchedule = false; // Maintenance has ended
                }
            }

            if (!withinSchedule)
            {
                await _next(context);
                return;
            }

            // Check if user is SuperAdmin - allow access during maintenance
            var userRole = context.User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            if (userRole == "SuperAdmin")
            {
                await _next(context);
                return;
            }

            // For AJAX/API requests, return maintenance response
            if (context.Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                context.Request.Headers["Accept"].ToString().Contains("application/json"))
            {
                context.Response.StatusCode = 503;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        success = false,
                        message = "System is under maintenance. Please try again later.",
                        maintenance = true,
                        maintenanceMessage = message
                    })
                );
                return;
            }

            // Redirect all users to the maintenance page
            context.Response.Redirect($"/Home/Maintenance?message={Uri.EscapeDataString(message)}");
        }

        private bool ShouldSkip(string path)
        {
            var skipPaths = new[]
            {
                "/account/",
                "/home/maintenance",
                "/home/statuscode",
                "/saassetting/",
                "/css/",
                "/js/",
                "/lib/",
                "/favicon.ico",
                "/chathub",
                "/api/"
            };

            return skipPaths.Any(s => path.StartsWith(s));
        }
    }

    public static class MaintenanceModeMiddlewareExtensions
    {
        public static IApplicationBuilder UseMaintenanceMode(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<MaintenanceModeMiddleware>();
        }
    }
}
