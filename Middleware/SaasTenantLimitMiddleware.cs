using CRM.Services;

namespace CRM.Middleware
{
    public class SaasTenantLimitMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<SaasTenantLimitMiddleware> _logger;

        public SaasTenantLimitMiddleware(RequestDelegate next, ILogger<SaasTenantLimitMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, ITenantService tenantService, AppDbContext appDb)
        {
            var path = context.Request.Path.Value?.ToLower() ?? "";

            if (ShouldSkip(path))
            {
                await _next(context);
                return;
            }

            if (!tenantService.IsResolved())
            {
                await _next(context);
                return;
            }

            var tenantId = tenantService.GetTenantId();
            if (tenantId == 0)
            {
                await _next(context);
                return;
            }

            // Get user role for differentiated messaging
            var userRole = context.User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";

            // Get active subscription for this tenant (MongoDbSet - no .Include() support)
            var subscription = await appDb.TenantSubscriptions
                .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Status == "Active");

            // No subscription - block access
            if (subscription == null)
            {
                if (userRole == "Admin")
                {
                    await RespondBlocked(context, "No active subscription. Please subscribe to a plan to continue.");
                }
                else
                {
                    await RespondContactAdmin(context, "Your organization does not have an active subscription. Please contact your administrator to upgrade the plan.");
                }
                return;
            }

            // Auto-renew expired subscriptions to prevent lockout
            if (subscription.EndDate < DateTime.UtcNow)
            {
                subscription.EndDate = DateTime.UtcNow.AddYears(1);
                subscription.Status = "Active";
                appDb.TenantSubscriptions.Update(subscription);
                await appDb.SaveChangesAsync();
                _logger.LogInformation("Auto-renewed expired subscription for TenantId={TenantId}, new EndDate={EndDate}", tenantId, subscription.EndDate);
            }

            // Look up plan separately (MongoDbSet .Include() is a no-op)
            var plan = await appDb.SaasPlans
                .FirstOrDefaultAsync(p => p.PlanId == subscription.PlanId);

            if (plan == null)
            {
                await RespondBlocked(context, "Your subscription plan was not found. Please contact support.");
                return;
            }

            var method = context.Request.Method.ToUpper();

            // Check limits only on POST (create actions)
            if (method == "POST")
            {
                var (blocked, message) = await CheckLimits(path, plan, appDb);
                if (blocked)
                {
                    await RespondBlocked(context, message);
                    return;
                }
            }

            await _next(context);
        }

        private async Task<(bool blocked, string message)> CheckLimits(string path, MasterDb.Models.SaasSubscriptionPlanModel plan, AppDbContext appDb)
        {
            // Check user/agent creation (ManageUsers, Agent onboard, Register)
            if (path.Contains("/manageusers/adduser") || path.Contains("/manageusers/register") ||
                path.Contains("/agent/onboard") || path.Contains("/account/register"))
            {
                if (plan.MaxUsers > 0)
                {
                    var currentUsers = await appDb.Users.CountAsync();
                    if (currentUsers >= plan.MaxUsers)
                    {
                        return (true, $"User limit reached ({plan.MaxUsers}). Upgrade your plan to add more users.");
                    }
                }

                if (plan.MaxAgents > 0)
                {
                    var currentAgents = await appDb.Users.CountAsync(u => u.Role == "Sales" || u.Role == "Agent");
                    if (currentAgents >= plan.MaxAgents)
                    {
                        return (true, $"Agent limit reached ({plan.MaxAgents}). Upgrade your plan to add more agents.");
                    }
                }
            }

            // Check Lead creation (manual add, bulk upload, import, webhook)
            if (path.Contains("/leads/savelead") || path.Contains("/leads/create") ||
                path.Contains("/leads/add") || path.Contains("/leads/import") ||
                path.Contains("/leads/bulkupload") || path.Contains("/webhookleads/assign"))
            {
                if (plan.MaxLeadsPerMonth > 0)
                {
                    var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                    var leadsThisMonth = await appDb.Leads.CountAsync(l => l.CreatedOn >= monthStart);
                    if (leadsThisMonth >= plan.MaxLeadsPerMonth)
                    {
                        return (true, $"Monthly lead limit reached ({plan.MaxLeadsPerMonth}). Upgrade your plan for more leads.");
                    }
                }
            }

            // Check partner creation
            if (path.Contains("/manageusers/createpartner") || path.Contains("/manageusers/partnerapproval/approve"))
            {
                if (plan.MaxPartners > 0)
                {
                    var currentPartners = await appDb.ChannelPartners.CountAsync();
                    if (currentPartners >= plan.MaxPartners)
                    {
                        return (true, $"Partner limit reached ({plan.MaxPartners}). Upgrade your plan to add more partners.");
                    }
                }
            }

            // Check Settings changes (branding, logo upload)
            if (path.Contains("/settings/save") || path.Contains("/settings/uploadlogo") ||
                path.Contains("/settings/uploadcompanylogo"))
            {
                if (!plan.HasCustomBranding)
                {
                    return (true, $"Custom branding requires an upgraded plan. Your current plan does not include this feature.");
                }
            }

            // ===== NEW FEATURE FLAG CHECKS =====

            // Check Quotation creation - requires HasQuotationManagement and respects MaxQuotationsPerMonth
            if (path.Contains("/quotations/create") || path.Contains("/quotations/save") ||
                path.Contains("/quotations/add") || path.Contains("/quotations/generate"))
            {
                if (!plan.HasQuotationManagement)
                {
                    return (true, "Quotation management is not included in your current plan. Upgrade to a higher plan.");
                }
                if (plan.MaxQuotationsPerMonth > 0)
                {
                    var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                    var thisMonthCount = await appDb.Quotations.CountAsync(q => q.CreatedOn >= monthStart);
                    if (thisMonthCount >= plan.MaxQuotationsPerMonth)
                    {
                        return (true, $"Monthly quotation limit reached ({plan.MaxQuotationsPerMonth}). Upgrade your plan.");
                    }
                }
            }

            // Check Property creation - requires feature and respects MaxProperties
            if (path.Contains("/properties/create") || path.Contains("/properties/save") ||
                path.Contains("/properties/add") || path.Contains("/properties/bulkupload"))
            {
                if (!plan.HasInventoryManagement)
                {
                    return (true, "Inventory/property management is not included in your current plan. Upgrade to a higher plan.");
                }
                if (plan.MaxProperties > 0)
                {
                    var currentCount = await appDb.Properties.CountAsync();
                    if (currentCount >= plan.MaxProperties)
                    {
                        return (true, $"Property limit reached ({plan.MaxProperties}). Upgrade your plan for more properties.");
                    }
                }
            }

            // Check Site Visit creation
            if (path.Contains("/sitevisits/create") || path.Contains("/sitevisits/schedule") ||
                path.Contains("/sitevisits/save") || path.Contains("/bookings/sitevisit"))
            {
                if (!plan.HasSiteVisitManagement)
                {
                    return (true, "Site visit management is not included in your current plan. Upgrade to a higher plan.");
                }
                if (plan.MaxSiteVisitsPerMonth > 0)
                {
                    var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                    var thisMonthCount = await appDb.Bookings.CountAsync(b => b.CreatedOn >= monthStart && b.Status == "Site Visit");
                    if (thisMonthCount >= plan.MaxSiteVisitsPerMonth)
                    {
                        return (true, $"Monthly site visit limit reached ({plan.MaxSiteVisitsPerMonth}). Upgrade your plan.");
                    }
                }
            }

            // Check Document uploads
            if (path.Contains("/documents/upload") || path.Contains("/documents/create") ||
                path.Contains("/documents/save") || path.Contains("/documents/add"))
            {
                if (!plan.HasDocumentManagement)
                {
                    return (true, "Document management is not included in your current plan. Upgrade to a higher plan.");
                }
                if (plan.MaxDocuments > 0)
                {
                    var currentCount = await appDb.AgentDocuments.CountAsync();
                    if (currentCount >= plan.MaxDocuments)
                    {
                        return (true, $"Document limit reached ({plan.MaxDocuments}). Upgrade your plan.");
                    }
                }
            }

            // Check Campaign creation (email campaigns)
            if (path.Contains("/campaigns/create") || path.Contains("/campaigns/send") ||
                path.Contains("/email/sendcampaign") || path.Contains("/email/sendbulk"))
            {
                if (!plan.HasCampaignManagement)
                {
                    return (true, "Campaign management is not included in your current plan. Upgrade to a higher plan.");
                }
                if (plan.MaxEmailCampaigns > 0)
                {
                    var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                    var thisMonthCount = await appDb.EmailLogs.CountAsync(e => e.SentOn >= monthStart);
                    if (thisMonthCount >= plan.MaxEmailCampaigns)
                    {
                        return (true, $"Monthly campaign/send limit reached ({plan.MaxEmailCampaigns}). Upgrade your plan.");
                    }
                }
            }

            // Check Invoice creation - requires HasInvoiceAutomation
            if (path.Contains("/invoices/create") || path.Contains("/invoices/save") ||
                path.Contains("/invoices/generate") || path.Contains("/invoices/pay"))
            {
                if (!plan.HasInvoiceAutomation)
                {
                    return (true, "Invoice automation is not included in your current plan. Upgrade to a higher plan.");
                }
            }

            // Check Workflow Automation feature
            if (path.Contains("/workflows/create") || path.Contains("/workflows/save") ||
                path.Contains("/automation/rules/create") || path.Contains("/automation/rules/save"))
            {
                if (!plan.HasWorkflowAutomation)
                {
                    return (true, "Workflow automation is not included in your current plan. Upgrade to a higher plan.");
                }
            }

            // Check Two-Factor Auth setting
            if (path.Contains("/account/enable2fa") || path.Contains("/account/setup2fa") ||
                path.Contains("/settings/twofactor") || path.Contains("/profile/enable2fa"))
            {
                if (!plan.HasTwoFactorAuth)
                {
                    return (true, "Two-factor authentication is not included in your current plan. Upgrade to a higher plan.");
                }
            }

            // Check Call Integration feature
            if (path.Contains("/calls/log") || path.Contains("/calls/create") ||
                path.Contains("/calls/save") || path.Contains("/twilio/"))
            {
                if (!plan.HasCallIntegration)
                {
                    return (true, "Call integration is not included in your current plan. Upgrade to a higher plan.");
                }
            }

            // Check SMS Integration feature
            if (path.Contains("/sms/send") || path.Contains("/sms/create") ||
                path.Contains("/sms/save") || path.Contains("/twilio/sms"))
            {
                if (!plan.HasSmsIntegration)
                {
                    return (true, "SMS integration is not included in your current plan. Upgrade to a higher plan.");
                }
            }

            // Check Lead Scoring feature (used when manually scoring or viewing AI scores)
            if (path.Contains("/leads/score") || path.Contains("/leads/updatescore") ||
                path.Contains("/leads/savescore") || path.Contains("/leads/aiscore"))
            {
                if (!plan.HasLeadScoring)
                {
                    return (true, "Lead scoring is not included in your current plan. Upgrade to a higher plan.");
                }
            }

            // Check AI Scoring feature
            if (path.Contains("/leads/aiscoring") || path.Contains("/leads/ai/score") ||
                path.Contains("/ai/scoring") || path.Contains("/ai/predict"))
            {
                if (!plan.HasAIScoring)
                {
                    return (true, "AI scoring is not included in your current plan. Upgrade to a higher plan.");
                }
            }

            // Check AI Chatbot feature
            if (path.Contains("/chatbot/") || path.Contains("/ai/chat"))
            {
                if (!plan.HasAIChatbot)
                {
                    return (true, "AI chatbot is not included in your current plan. Upgrade to a higher plan.");
                }
            }

            return (false, "");
        }

        private bool ShouldSkip(string path)
        {
            // Only skip specific account paths (login, forgot password, logout, keepalive) — NOT register
            var skipPaths = new[]
            {
                "/account/login", "/account/forgotpassword", "/account/resetpassword", "/account/logout", "/account/keepalive",
                "/saassubscription/", "/subscription/",
                "/superadmin/", "/crmplan/", "/api/", "/css/", "/js/", "/lib/",
                "/favicon.ico", "/home/", "/search/"
            };

            return skipPaths.Any(s => path.StartsWith(s));
        }

        private async Task RespondBlocked(HttpContext context, string message)
        {
            if (context.Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                context.Request.Headers["Accept"].ToString().Contains("application/json"))
            {
                context.Response.StatusCode = 403;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    System.Text.Json.JsonSerializer.Serialize(new { success = false, message, limitExceeded = true })
                );
            }
            else
            {
                context.Response.Redirect($"/SaasSubscription/MyPlan?limit=true&msg={Uri.EscapeDataString(message)}");
            }
        }

        private async Task RespondContactAdmin(HttpContext context, string message)
        {
            if (context.Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                context.Request.Headers["Accept"].ToString().Contains("application/json"))
            {
                context.Response.StatusCode = 403;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    System.Text.Json.JsonSerializer.Serialize(new { success = false, message, contactAdmin = true, redirectUrl = "/Home/ContactAdmin" })
                );
            }
            else
            {
                context.Response.Redirect($"/Home/ContactAdmin?msg={Uri.EscapeDataString(message)}");
            }
        }
    }

    public static class SaasTenantLimitMiddlewareExtensions
    {
        public static IApplicationBuilder UseSaasTenantLimits(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<SaasTenantLimitMiddleware>();
        }
    }
}