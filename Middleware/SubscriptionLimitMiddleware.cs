using CRM.Helpers;
using CRM.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CRM.Middleware
{
    public class SubscriptionLimitMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<SubscriptionLimitMiddleware> _logger;

        public SubscriptionLimitMiddleware(RequestDelegate next, ILogger<SubscriptionLimitMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, SubscriptionService subscriptionService)
        {
            // Skip middleware for certain paths
            var path = context.Request.Path.Value?.ToLower();
            if (ShouldSkipMiddleware(path))
            {
                await _next(context);
                return;
            }

            // Get user context
            var (userId, role, channelPartnerId) = GetUserContext(context);
            
            // Skip for admin users
            if (role?.ToLower() == "admin")
            {
                await _next(context);
                return;
            }

            // Skip if no partner context
            if (!channelPartnerId.HasValue)
            {
                await _next(context);
                return;
            }

            // P0-S1: Check trial expiration
            var trialCheckResult = await CheckTrialExpiration(context, subscriptionService, channelPartnerId.Value);
            if (!trialCheckResult.allowed)
            {
                await HandleTrialExpired(context, trialCheckResult.message);
                return;
            }

            // Check subscription limits for specific actions
            if (await ShouldCheckLimits(context, path))
            {
                var limitCheckResult = await CheckSubscriptionLimits(context, subscriptionService, channelPartnerId.Value, path);
                if (!limitCheckResult.allowed)
                {
                    await HandleLimitExceeded(context, limitCheckResult.message);
                    return;
                }
            }

            await _next(context);
        }

        private bool ShouldSkipMiddleware(string? path)
        {
            if (string.IsNullOrEmpty(path)) return true;

            var skipPaths = new[]
            {
                "/account/",
                "/home/",
                "/subscription/",
                "/api/",
                "/css/",
                "/js/",
                "/lib/",
                "/favicon.ico",
                "/notification/",
                "/profile/"
            };

            return skipPaths.Any(skipPath => path.StartsWith(skipPath));
        }

        private (int? UserId, string? Role, int? ChannelPartnerId) GetUserContext(HttpContext context)
        {
            var token = context.Request.Cookies["jwtToken"];
            if (string.IsNullOrEmpty(token)) return (null, null, null);

            try
            {
                var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
                var userIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
                var role = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;

                if (!int.TryParse(userIdClaim, out int userId)) return (null, role, null);

                // Get ChannelPartnerId from database (you might want to cache this)
                using var scope = context.RequestServices.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var user = dbContext.Users.FirstOrDefault(u => u.UserId == userId);
                
                return (userId, role, user?.ChannelPartnerId);
            }
            catch
            {
                return (null, null, null);
            }
        }

        private async Task<bool> ShouldCheckLimits(HttpContext context, string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            // Check limits for specific actions
            var method = context.Request.Method.ToUpper();
            
            // Check agent creation
            if (method == "POST" && (path.Contains("/manageusers/adduser") || path.Contains("/agent/onboard")))
                return true;

            // Check lead creation
            if (method == "POST" && path.Contains("/leads/") && !path.Contains("/details/"))
                return true;

            // Check file uploads (storage limit)
            if (method == "POST" && context.Request.HasFormContentType && context.Request.Form.Files.Any())
                return true;

            return false;
        }

        private async Task<(bool allowed, string message)> CheckSubscriptionLimits(
            HttpContext context, 
            SubscriptionService subscriptionService, 
            int channelPartnerId, 
            string? path)
        {
            var method = context.Request.Method.ToUpper();

            try
            {
                // Check agent limit
                if (method == "POST" && (path?.Contains("/manageusers/adduser") == true || path?.Contains("/agent/onboard") == true))
                {
                    var (canAdd, message) = await subscriptionService.CanAddAgentAsync(channelPartnerId);
                    if (!canAdd)
                        return (false, message);
                }

                // Check lead limit
                if (method == "POST" && path?.Contains("/leads/") == true && !path.Contains("/details/"))
                {
                    var (canAdd, message) = await subscriptionService.CanAddLeadAsync(channelPartnerId);
                    if (!canAdd)
                        return (false, message);
                }

                // Check storage limit for file uploads
                if (method == "POST" && context.Request.HasFormContentType && context.Request.Form.Files.Any())
                {
                    var subscription = await subscriptionService.GetActiveSubscriptionAsync(channelPartnerId);
                    if (subscription?.Plan != null && subscription.Plan.MaxStorageGB > 0)
                    {
                        var totalUploadSize = context.Request.Form.Files.Sum(f => f.Length) / (1024.0 * 1024.0 * 1024.0); // Convert to GB
                        var projectedUsage = subscription.CurrentStorageUsedGB + (decimal)totalUploadSize;
                        
                        if (projectedUsage > subscription.Plan.MaxStorageGB)
                        {
                            return (false, $"Storage limit exceeded. Your {subscription.Plan.PlanName} plan allows {subscription.Plan.MaxStorageGB} GB storage. Please upgrade your plan.");
                        }
                    }
                }

                return (true, "");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking subscription limits for partner {ChannelPartnerId}", channelPartnerId);
                return (true, ""); // Allow on error to prevent blocking
            }
        }

        // P0-S1: Check if trial has expired
        private async Task<(bool allowed, string message)> CheckTrialExpiration(
            HttpContext context,
            SubscriptionService subscriptionService,
            int channelPartnerId)
        {
            try
            {
                var subscription = await subscriptionService.GetActiveSubscriptionAsync(channelPartnerId);
                
                // No subscription found - block access
                if (subscription == null)
                {
                    return (false, "No active subscription found. Please subscribe to a plan to continue.");
                }

                // Check if subscription has expired (works for both trial and paid)
                if (subscription.EndDate < IndianTime.Now)
                {
                    var isTrial = subscription.Amount == 0; // Trial subscriptions are free
                    var message = isTrial 
                        ? $"Your trial period has expired on {subscription.EndDate:MMM dd, yyyy}. Please upgrade to a paid plan to continue using the system."
                        : $"Your subscription has expired on {subscription.EndDate:MMM dd, yyyy}. Please renew to continue.";
                    
                    return (false, message);
                }

                // Check if subscription status is not active
                if (subscription.Status != "Active")
                {
                    return (false, $"Your subscription is currently {subscription.Status}. Please contact support or renew your subscription.");
                }

                return (true, "");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking trial expiration for partner {ChannelPartnerId}", channelPartnerId);
                return (true, ""); // Allow on error to prevent blocking
            }
        }

        private async Task HandleTrialExpired(HttpContext context, string message)
        {
            context.Response.StatusCode = 403;

            if (context.Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                // AJAX request - return JSON
                context.Response.ContentType = "application/json";
                var response = new { success = false, message = message, trialExpired = true, redirectUrl = "/Subscription/MyPlan" };
                await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(response));
            }
            else
            {
                // Regular request - redirect to subscription page
                context.Response.Redirect("/Subscription/MyPlan?expired=true");
            }
        }

        private async Task HandleLimitExceeded(HttpContext context, string message)
        {
            context.Response.StatusCode = 403;

            if (context.Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                // AJAX request - return JSON
                context.Response.ContentType = "application/json";
                var response = new { success = false, message = message, limitExceeded = true };
                await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(response));
            }
            else
            {
                // Regular request - redirect or show error page
                context.Response.ContentType = "text/html";
                var html = $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <title>Subscription Limit Exceeded</title>
                    <link href='/lib/bootstrap/dist/css/bootstrap.min.css' rel='stylesheet' />
                    <script src='https://cdn.jsdelivr.net/npm/sweetalert2@11'></script>
                </head>
                <body>
                    <div class='container mt-5'>
                        <div class='row justify-content-center'>
                            <div class='col-md-6'>
                                <div class='card'>
                                    <div class='card-body text-center'>
                                        <i class='fas fa-exclamation-triangle text-warning' style='font-size: 3rem;'></i>
                                        <h4 class='mt-3'>Subscription Limit Exceeded</h4>
                                        <p class='text-muted'>{message}</p>
                                        <a href='/Subscription/MyPlan' class='btn btn-primary'>Upgrade Plan</a>
                                        <a href='javascript:history.back()' class='btn btn-secondary'>Go Back</a>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>
                    <script>
                        Swal.fire({{
                            icon: 'warning',
                            title: 'Subscription Limit Exceeded',
                            text: '{message}',
                            showCancelButton: true,
                            confirmButtonText: 'Upgrade Plan',
                            cancelButtonText: 'Go Back'
                        }}).then((result) => {{
                            if (result.isConfirmed) {{
                                window.location.href = '/Subscription/MyPlan';
                            }} else {{
                                history.back();
                            }}
                        }});
                    </script>
                </body>
                </html>";
                await context.Response.WriteAsync(html);
            }
        }
    }

    // Extension method to register the middleware
    public static class SubscriptionLimitMiddlewareExtensions
    {
        public static IApplicationBuilder UseSubscriptionLimits(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<SubscriptionLimitMiddleware>();
        }
    }
}