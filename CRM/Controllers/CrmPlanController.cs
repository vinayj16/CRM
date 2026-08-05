using CRM.Models.MongoDb;
using CRM.Services;
using CRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace CRM.Controllers
{
    [Authorize]
    public class CrmPlanController : Controller
    {
        private readonly MongoDbContext _mongoDb;
        private readonly ITenantService _tenantService;
        private readonly ILogger<CrmPlanController> _logger;

        public CrmPlanController(MongoDbContext mongoDb, ITenantService tenantService, ILogger<CrmPlanController> logger)
        {
            _mongoDb = mongoDb;
            _tenantService = tenantService;
            _logger = logger;
        }

        [HttpGet]
        [HttpGet]
        public IActionResult Index()
        {
            return RedirectToAction("MyPlan");
        }

        [HttpGet]
        public async Task<IActionResult> MyPlan()
        {
            if (!_tenantService.IsResolved())
                return View("NoPlan");

            try
            {
                var tenantId = _tenantService.GetTenantId();
                var tenantName = _tenantService.GetTenantName();

                // Fetch subscription data from MongoDB
                var subscriptionsCollection = _mongoDb.GetCollection<TenantSubscriptionDocument>("tenant_subscriptions");
                var subscription = await subscriptionsCollection
                    .Find(s => s.TenantId == tenantId && (s.Status == "Active" || s.Status == "Trial"))
                    .SortByDescending(s => s.StartDate)
                    .FirstOrDefaultAsync();

                // Fetch all active plans from MongoDB
                var plans = await _mongoDb.SaasPlans
                    .Find(p => p.IsActive)
                    .SortBy(p => p.SortOrder)
                    .ToListAsync();

                // Get plan name for current subscription
                string subscriptionPlanName = "Unknown Plan";
                if (subscription != null && subscription.PlanId > 0)
                {
                    var plan = await _mongoDb.SaasPlans
                        .Find(p => p.PlanId == subscription.PlanId)
                        .FirstOrDefaultAsync();
                    subscriptionPlanName = plan?.PlanName ?? "Unknown Plan";
                }

                var viewModel = new CrmPlanViewModel
                {
                    TenantName = tenantName,
                    CurrentSubscription = subscription != null ? new SubscriptionInfo
                    {
                        PlanName = subscriptionPlanName,
                        BillingCycle = subscription.Status == "Trial" ? "Trial" : "Monthly",
                        Amount = subscription.AmountPaid,
                        StartDate = subscription.StartDate,
                        EndDate = subscription.EndDate,
                        Status = subscription.Status,
                        PlanId = subscription.PlanId
                    } : null,
                    AvailablePlans = plans.Select(p => new PlanInfo
                    {
                        PlanId = p.PlanId,
                        PlanName = p.PlanName,
                        Description = p.Description,
                        MonthlyPrice = p.MonthlyPrice,
                        YearlyPrice = p.YearlyPrice,
                        MaxUsers = p.MaxUsers,
                        MaxAgents = p.MaxAgents,
                        MaxLeadsPerMonth = p.MaxLeadsPerMonth,
                        MaxPartners = p.MaxPartners,
                        HasWhatsAppIntegration = p.HasWhatsAppIntegration,
                        HasFacebookIntegration = p.HasFacebookIntegration,
                        HasCustomAPIAccess = p.HasCustomAPIAccess,
                        HasPrioritySupport = p.HasPrioritySupport,
                        SupportLevel = p.SupportLevel,
                        IsCurrent = subscription != null && p.PlanId == subscription.PlanId
                    }).ToList()
                };

                return View(viewModel);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error loading MyPlan for tenant {TenantId}", _tenantService.GetTenantId());
                return View(new CrmPlanViewModel());
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetMySubscription()
        {
            if (!_tenantService.IsResolved())
                return Json(new { success = false, message = "Tenant not resolved" });

            int tenantId = 0;
            try
            {
                tenantId = _tenantService.GetTenantId();
                var subscriptionsCollection = _mongoDb.GetCollection<TenantSubscriptionDocument>("tenant_subscriptions");
                var subscription = await subscriptionsCollection
                    .Find(s => s.TenantId == tenantId && (s.Status == "Active" || s.Status == "Trial"))
                    .SortByDescending(s => s.StartDate)
                    .FirstOrDefaultAsync();

                if (subscription == null)
                    return Json(new { success = true, hasSubscription = false });

                var planName = subscription.PlanId > 0
                    ? (await _mongoDb.SaasPlans
                        .Find(p => p.PlanId == subscription.PlanId)
                        .FirstOrDefaultAsync())?.PlanName ?? "Unknown"
                    : "Unknown";

                var daysRemaining = (subscription.EndDate - DateTime.UtcNow).Days;

                return Json(new
                {
                    success = true,
                    hasSubscription = true,
                    planName,
                    billingCycle = subscription.Status == "Trial" ? "Trial" : "Monthly",
                    amount = subscription.AmountPaid,
                    startDate = subscription.StartDate.ToString("yyyy-MM-dd"),
                    endDate = subscription.EndDate.ToString("yyyy-MM-dd"),
                    daysRemaining = Math.Max(0, daysRemaining),
                    status = daysRemaining <= 0 ? "Expired" : subscription.Status,
                    isTrial = subscription.Status == "Trial"
                });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error fetching subscription for tenant {TenantId}", tenantId);
                return Json(new { success = false, message = "Error fetching subscription" });
            }
        }
    }
}