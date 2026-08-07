using CRM.Attributes;
using CRM.Helpers;
using CRM.MasterDb;
using CRM.MasterDb.Models;
using CRM.Models;
using CRM.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;


namespace CRM.Controllers
{
    [RoleAuthorize("Admin", "Partner")]
    public class SaasSubscriptionController : Controller
    {
        private readonly MasterDbContext _masterDb;
        //
        private readonly RazorpayService _razorpayService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<SaasSubscriptionController> _logger;
        private readonly IConfiguration _config;
        private readonly ITenantService _tenantService;


        public SaasSubscriptionController(
            MasterDbContext masterDb,
            IConfiguration config,
            RazorpayService razorpayService,
            IHttpContextAccessor httpContextAccessor,
            ILogger<SaasSubscriptionController> logger, ITenantService tenantService)
        {
            _masterDb = masterDb;
            //
            _config = config;
            _razorpayService = razorpayService;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
            _tenantService = tenantService;
        }

        private (int? UserId, string? Role, int? TenantId) GetCurrentUserContext()
        {
            var token = _httpContextAccessor.HttpContext?.Request.Cookies["jwtToken"];
            if (string.IsNullOrEmpty(token)) return (null, null, null);

            var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);
            var userIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            var role = jwt.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role)?.Value;

            if (!int.TryParse(userIdClaim, out int userId)) return (null, null, null);

            var resolvedTenantId = _tenantService.GetTenantId();
            return (userId, role, resolvedTenantId > 0 ? resolvedTenantId : (int?)null);


        }

        // --- Subscription Plans Management (Admin) ---

        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> Plans()
        {
            var plans = await _masterDb.SaasPlans
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.MonthlyPrice)
                
                .ToListAsync();
            return View(plans);
        }

        [HttpGet]
        public async Task<IActionResult> GetPlans()
        {
            var plans = await _masterDb.SaasPlans
                .Where(p => p.IsActive)
                .OrderBy(p => p.SortOrder)
                
                .Select(p => new
                {
                    planId = p.PlanId,
                    planName = p.PlanName,
                    monthlyPrice = p.MonthlyPrice,
                    yearlyPrice = p.YearlyPrice,
                    maxAgents = p.MaxAgents,
                    maxLeadsPerMonth = p.MaxLeadsPerMonth,
                    maxStorageGB = p.MaxStorageGB
                })
                .ToListAsync();

            return Json(plans);
        }

        [HttpGet]
        [RoleAuthorize("Admin")]
        public IActionResult CreatePlan()
        {
            return View(new SaasSubscriptionPlanModel());
        }

        [HttpPost]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> CreatePlan(SaasSubscriptionPlanModel model)
        {
            if (ModelState.IsValid)
            {
                model.CreatedOn = IndianTime.Now;
                _masterDb.SaasPlans.Add(model);
                await _masterDb.SaveChangesAsync();

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Json(new { success = true, message = "Plan created successfully!" });

                return RedirectToAction(nameof(Plans));
            }
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { success = false, errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage) });

            return View(model);
        }

        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> EditPlan(int id)
        {
            var plan = await _masterDb.SaasPlans.FindAsync(id);
            if (plan == null) return NotFound();
            return View(plan);
        }


        [HttpPost]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> UpdatePlan(SaasSubscriptionPlanModel model)
        {
            if (ModelState.IsValid)
            {
                var existingPlan = await _masterDb.SaasPlans.FindAsync(model.PlanId);
                if (existingPlan == null) return NotFound();

                existingPlan.PlanName = model.PlanName;
                existingPlan.Description = model.Description;
                existingPlan.MonthlyPrice = model.MonthlyPrice;
                existingPlan.YearlyPrice = model.YearlyPrice;
                existingPlan.MaxAgents = model.MaxAgents;
                existingPlan.MaxLeadsPerMonth = model.MaxLeadsPerMonth;
                existingPlan.MaxStorageGB = model.MaxStorageGB;
                existingPlan.HasWhatsAppIntegration = model.HasWhatsAppIntegration;
                existingPlan.HasFacebookIntegration = model.HasFacebookIntegration;
                existingPlan.HasEmailIntegration = model.HasEmailIntegration;
                existingPlan.HasCustomAPIAccess = model.HasCustomAPIAccess;
                existingPlan.HasAdvancedReports = model.HasAdvancedReports;
                existingPlan.HasPrioritySupport = model.HasPrioritySupport;




                existingPlan.SupportLevel = model.SupportLevel;
                existingPlan.IsActive = model.IsActive;
                existingPlan.PlanType = model.PlanType;
                existingPlan.SortOrder = model.SortOrder;
                existingPlan.UpdatedOn = IndianTime.Now;

                _masterDb.SaasPlans.Update(existingPlan);
                await _masterDb.SaveChangesAsync();

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Json(new { success = true, message = "Plan updated successfully!" });

                return RedirectToAction(nameof(Plans));
            }
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { success = false, errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage) });

            return View(model);
        }

        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> CheckPlanSubscribers(int planId)
        {
            var activeSubscribers = await _masterDb.TenantSubscriptions
                .Where(s => s.PlanId == planId && s.Status == "Active" && s.EndDate > IndianTime.Now)
                .ToListAsync();

            return Json(new
            {
                hasActiveSubscribers = activeSubscribers.Any(),
                count = activeSubscribers.Count,
                subscribers = activeSubscribers
            });
        }

        [HttpPost]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> TogglePlan(int id, bool isActive)
        {
            var plan = await _masterDb.SaasPlans.FindAsync(id);
            if (plan == null)
                return NotFound();

            plan.IsActive = isActive;
            plan.UpdatedOn = IndianTime.Now;
            _masterDb.SaasPlans.Update(plan);
            await _masterDb.SaveChangesAsync();

            return RedirectToAction(nameof(Plans));
        }

        // --- Admin: Manage Partner Subscriptions ---
        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> TenantSubscriptions(string? search, string? status, int page = 1)
        {
            int pageSize = 20;

            var query = _masterDb.TenantSubscriptions
                .AsQueryable();

            // Apply search filter - note: Tenant and Plan are [BsonIgnore], so only search by SubscriptionId or Status
            if (!string.IsNullOrEmpty(search))
            {
                if (int.TryParse(search, out int searchId))
                {
                    query = query.Where(s => s.SubscriptionId == searchId || s.TenantId == searchId);
                }
                else
                {
                    query = query.Where(s => s.Status != null && s.Status.Contains(search));
                }
            }

            // Apply status filter
            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(s => s.Status == status);
            }

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            var subscriptions = await query
                .OrderByDescending(s => s.CreatedOn)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Search = search;
            ViewBag.Status = status;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;

            return View(subscriptions);
        }

        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> GetPartnerSubscription(int tenantId)
        {
            var currentSubscription = await _masterDb.TenantSubscriptions
                .Where(s => s.TenantId == tenantId && s.Status == "Active")
                .FirstOrDefaultAsync();

            var scheduledSubscription = await _masterDb.TenantSubscriptions
                .Where(s => s.TenantId == tenantId && s.Status == "Scheduled")
                .FirstOrDefaultAsync();

            var partner = await _masterDb.Tenants.FindAsync(tenantId);

            return Json(new
            {
                success = true,
                partner = new
                {
                    tenantId = partner?.TenantId,
                    companyName = partner?.CompanyName,
                    email = partner?.CompanyName
                },
                currentSubscription = currentSubscription != null ? new
                {
                    subscriptionId = currentSubscription.SubscriptionId,
                    planId = currentSubscription.PlanId,
                    planName = currentSubscription.Plan?.PlanName,
                    amount = currentSubscription.Amount,
                    billingCycle = currentSubscription.BillingCycle,
                    startDate = currentSubscription.StartDate.ToString("MMM dd, yyyy"),
                    endDate = currentSubscription.EndDate.ToString("MMM dd, yyyy"),
                    status = currentSubscription.Status
                } : null,
                scheduledSubscription = scheduledSubscription != null ? new
                {
                    subscriptionId = scheduledSubscription.SubscriptionId,
                    planId = scheduledSubscription.PlanId,
                    planName = scheduledSubscription.Plan?.PlanName,
                    amount = scheduledSubscription.Amount,
                    billingCycle = scheduledSubscription.BillingCycle,
                    startDate = scheduledSubscription.StartDate.ToString("MMM dd, yyyy"),
                    status = scheduledSubscription.Status
                } : null
            });
        }

        // Admin: Extend Trial Subscription
        [HttpPost]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> ExtendTrial(int subscriptionId, int days)
        {
            var subscription = await _masterDb.TenantSubscriptions.FindAsync(subscriptionId);
            if (subscription == null)
                return Json(new { success = false, message = "Subscription not found" });

            subscription.EndDate = subscription.EndDate.AddDays(days);
            subscription.UpdatedOn = IndianTime.Now;
            _masterDb.TenantSubscriptions.Update(subscription);
            await _masterDb.SaveChangesAsync();

            return Json(new { success = true, message = $"Trial extended by {days} days until {subscription.EndDate:MMM dd, yyyy}" });
        }


        [HttpPost]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> AdminChangePlan(int tenantId, int newPlanId, string billingCycle, string reason)
        {
            using var transaction = await _masterDb.Database.BeginTransactionAsync();

            try
            {
                var partner = await _masterDb.Tenants.FindAsync(tenantId);
                if (partner == null)
                    return Json(new { success = false, message = "Partner not found" });

                var newPlan = await _masterDb.SaasPlans.FindAsync(newPlanId);
                if (newPlan == null)
                    return Json(new { success = false, message = "Plan not found" });

                var amount = billingCycle.ToLower() == "annual" ? newPlan.YearlyPrice : newPlan.MonthlyPrice;

                // Get current active subscription
                var currentSub = await _masterDb.TenantSubscriptions
                    .Where(s => s.TenantId == tenantId && s.Status == "Active")
                    .FirstOrDefaultAsync();

                if (currentSub != null)
                {
                    // Expire current subscription
                    currentSub.Status = "Expired";
                    currentSub.EndDate = IndianTime.Now;
                    currentSub.UpdatedOn = IndianTime.Now;
                    currentSub.CancellationReason = $"Admin changed plan - {reason}";
                    currentSub.CancelledOn = IndianTime.Now;
                    _masterDb.TenantSubscriptions.Update(currentSub);
                }

                // Cancel any scheduled subscriptions
                var scheduledSubscriptions = await _masterDb.TenantSubscriptions
                    .Where(s => s.TenantId == tenantId && s.Status == "Scheduled")
                    .ToListAsync();

                foreach (var sch in scheduledSubscriptions)
                {
                    sch.Status = "Cancelled";
                    sch.CancelledOn = IndianTime.Now;
                    sch.CancellationReason = $"Admin changed plan - {reason}";
                    sch.UpdatedOn = IndianTime.Now;
                    _masterDb.TenantSubscriptions.Update(sch);
                }

                // Create new active subscription
                var newSub = new TenantSubscriptionModel
                {
                    TenantId = tenantId,
                    PlanId = newPlanId,
                    BillingCycle = billingCycle,
                    Amount = amount,
                    StartDate = IndianTime.Now,
                    EndDate = billingCycle.ToLower() == "annual" ? IndianTime.Now.AddYears(1) : IndianTime.Now.AddMonths(1),
                    Status = "Active",
                    PaymentMethod = "Admin Assignment",
                    PaymentTransactionId = $"admin_{IndianTime.Now.Ticks}",
                    LastPaymentDate = IndianTime.Now,
                    NextPaymentDate = billingCycle.ToLower() == "annual" ? IndianTime.Now.AddYears(1) : IndianTime.Now.AddMonths(1),
                    AutoRenew = false,
                    CreatedOn = IndianTime.Now,
                    UpdatedOn = IndianTime.Now
                };

                _masterDb.TenantSubscriptions.Add(newSub);
                await _masterDb.SaveChangesAsync();

                // Create payment transaction record
                var payTransaction = new SaasPaymentTransactionModel
                {
                    TenantId = tenantId,
                    SubscriptionId = newSub.SubscriptionId,
                    TransactionReference = $"ADMIN_ACTIVATION_{IndianTime.Now:yyyyMMddHHmmss}",
                    Amount = amount,
                    Currency = "INR",
                    Status = "Success",
                    TransactionType = "Admin Assignment",
                    PaymentMethod = "Admin",
                    TransactionDate = IndianTime.Now,
                    CompletedDate = IndianTime.Now,
                    Description = $"Admin assigned {newPlan.PlanName} plan ({billingCycle})",
                    PlanName = newPlan.PlanName,
                    BillingCycle = billingCycle,
                    CreatedOn = IndianTime.Now
                };

                _masterDb.SaasPaymentTransactions.Add(payTransaction);
                await _masterDb.SaveChangesAsync();

                await transaction.CommitAsync();

                _logger.LogInformation($"Admin changed partner {tenantId} plan to {newPlan.PlanName}. Reason: {reason}");

                // Send plan change notification email to partner
                if (!string.IsNullOrEmpty(partner.Email))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var scopeFactory = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();
                            using var emailScope = scopeFactory.CreateScope();
                            var emailService = emailScope.ServiceProvider.GetRequiredService<EmailService>();
                            var baseUrl = $"{Request.Scheme}://{Request.Host}";
                            await emailService.SendTemplateEmailAsync(
                                "PlanChangeNotification",
                                partner.Email,
                                0,
                                new Dictionary<string, string>
                                {
                                    ["PlanName"] = newPlan.PlanName ?? "",
                                    ["BillingCycle"] = billingCycle ?? "",
                                    ["Amount"] = $"{amount:N2}",
                                    ["Name"] = partner.CompanyName ?? "",
                                    ["DashboardUrl"] = $"{baseUrl}/SaasSubscription/MyPlan",
                                    ["CompanyName"] = "PropTech CRM",
                                    ["Year"] = IndianTime.Now.Year.ToString()
                                },
                                "Subscription");
                        }
                        catch { }
                    });
                }

                return Json(new
                {
                    success = true,
                    message = $"Successfully assigned {newPlan.PlanName} ({billingCycle}) plan to {partner.CompanyName}"
                });
            }
            catch (Exception ex)
            {

                _logger.LogError(ex, $"Error changing partner plan for partner {tenantId}");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // --- Membership Addons ---
        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> Addons()
        {
            var addons = await _masterDb.SaasPlans
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.PlanName)
                .ToListAsync();

            return View(addons);
        }


        [HttpGet]
        public async Task<IActionResult> Transactions(string? status, string? type, DateTime? fromDate, DateTime? toDate)
        {
            var (userId, role, tenantId) = GetCurrentUserContext();


            var transactionQuery = _masterDb.SaasPaymentTransactions
                .Include(t => t.Subscription)
                .AsQueryable();


            if (role == "Admin" && tenantId.HasValue)

                transactionQuery = transactionQuery.Where(t => t.TenantId == tenantId.Value);


            if (!string.IsNullOrEmpty(status))
                transactionQuery = transactionQuery.Where(t => t.Status == status);

            if (!string.IsNullOrEmpty(type))
                transactionQuery = transactionQuery.Where(t => t.TransactionType == type);

            if (fromDate.HasValue)
                transactionQuery = transactionQuery.Where(t => t.TransactionDate >= fromDate.Value);

            if (toDate.HasValue)
                transactionQuery = transactionQuery.Where(t => t.TransactionDate <= toDate.Value.AddDays(1));

            var transactions = await transactionQuery
                .OrderByDescending(t => t.TransactionDate)
                .ToListAsync();


            // For refund transactions, replace refund ID with original payment ID for better display
            foreach (var transaction in transactions.Where(t => t.TransactionType == "Refund" &&
                                                         !string.IsNullOrEmpty(t.RazorpayPaymentId) &&
                                                         t.RazorpayPaymentId.StartsWith("rfnd_")))
            {
                if (transaction.SubscriptionId.HasValue)
                {
                    var originalTransaction = await _masterDb.SaasPaymentTransactions
                        .Where(t => t.SubscriptionId == transaction.SubscriptionId.Value &&
                         t.TenantId == tenantId &&
                                   t.Status == "Success" &&
                                   t.TransactionType != "Refund" &&
                                    t.TransactionType != "Cancellation" &&
                                   !string.IsNullOrEmpty(t.RazorpayPaymentId) &&
                                   !t.RazorpayPaymentId.StartsWith("pay_"))
                        .OrderByDescending(t => t.TransactionDate)
                        .FirstOrDefaultAsync();

                    if (originalTransaction != null && !string.IsNullOrEmpty(originalTransaction.RazorpayPaymentId))
                    {

                        transaction.RazorpayPaymentId = originalTransaction.RazorpayPaymentId;
                    }
                }
            }

            // Get cancelled subscriptions with pending refunds for admin
            var pendingRefundSubscriptions = new List<TenantSubscriptionModel>();
            if (role?.ToLower() == "admin")
            {
                var refundQuery = _masterDb.TenantSubscriptions
                    .Where(s => s.Status == "Cancelled" &&
                               s.CancellationReason != null &&
                               s.CancellationReason.Contains("Refund Pending") &&
                               s.TenantId == tenantId).AsQueryable();

                if (fromDate.HasValue)
                    refundQuery = refundQuery.Where(s => s.CancelledOn >= fromDate.Value);

                if (toDate.HasValue)
                    refundQuery = refundQuery.Where(s => s.CancelledOn <= toDate.Value.AddDays(1));

                pendingRefundSubscriptions = await refundQuery
                    .OrderByDescending(s => s.CancelledOn)
                    .ToListAsync();
            }


            ViewBag.Status = status;
            ViewBag.Type = type;
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
            ViewBag.IsAdmin = role?.ToLower() == "admin";
            ViewBag.PendingRefundSubscriptions = pendingRefundSubscriptions;

            return View(transactions);
        }
        // --- Partner: My Plan Selection & Management ---

        [HttpGet]
        public async Task<IActionResult> MyPlan()
        {
            var (userId, role, tenantId) = GetCurrentUserContext();

            if (!tenantId.HasValue) return RedirectToAction("AccessDenied", "Home");

            // Get current active subscription
            var currentSubscription = await _masterDb.TenantSubscriptions
                .Where(s => s.TenantId == tenantId.Value && s.Status == "Active")
                .OrderByDescending(s => s.CreatedOn)
                .FirstOrDefaultAsync();

            // If service returns expired subscription, get the real active one
            if (currentSubscription == null || currentSubscription.EndDate <= IndianTime.Now)
            {
                currentSubscription = await _masterDb.TenantSubscriptions
                    .Where(s => s.TenantId == tenantId.Value &&
                                s.Status == "Active" &&
                                s.EndDate > IndianTime.Now)
                    .OrderByDescending(s => s.CreatedOn)
                    .FirstOrDefaultAsync();
            }

            // (No write here — this is a display-only GET action.)

            var availablePlans = await _masterDb.SaasPlans.Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync();

            // Get scheduled subscription if any - exclude cancelled/refunded subscriptions properly
            var scheduledSubscription = await _masterDb.TenantSubscriptions
                .Where(s => s.TenantId == tenantId.Value &&
                           s.Status == "Scheduled" &&
                           (s.CancellationReason == null ||
                           (!s.CancellationReason.Contains("Refund") &&
                           !s.CancellationReason.Contains("Cancelled by user") &&
                           !s.CancellationReason.Contains("PERMANENTLY CANCELLED"))))
                .OrderByDescending(s => s.CreatedOn)
                .FirstOrDefaultAsync();

            _logger.LogInformation($"MyPlan: Partner {tenantId.Value} - Found scheduled subscription: {scheduledSubscription != null}");

            if (scheduledSubscription != null)
            {
                _logger.LogInformation($"Scheduled subscription details: ID={scheduledSubscription.SubscriptionId}, Plan={scheduledSubscription.Plan.PlanName}");
            }
            else
            {
                // Check if there are any subscriptions with "Scheduled Payment" transaction type
                var scheduledTransactions = await _masterDb.SaasPaymentTransactions
                    .Where(t => t.TenantId == tenantId.Value &&
                               t.TransactionType == "Scheduled Payment" &&
                               t.Status == "Success")
                    .OrderByDescending(t => t.TransactionDate)
                    .ToListAsync();

                _logger.LogInformation($"Found {scheduledTransactions.Count} scheduled payment transactions for partner {tenantId.Value}");

                foreach (var trans in scheduledTransactions)
                {
                    _logger.LogInformation($"Scheduled transaction: ID={trans.TransactionId}, SubscriptionID={trans.SubscriptionId}, Amount={trans.Amount} , Date ={trans.TransactionDate}");

                    if (trans.SubscriptionId.HasValue)
                    {
                        var relatedSubscription = await _masterDb.TenantSubscriptions
                            .FirstOrDefaultAsync(s => s.SubscriptionId == trans.SubscriptionId.Value &&
                                       s.Status == "Cancelled" &&
                                       s.Status == "Refunded");

                        if (relatedSubscription != null)
                        {
                            _logger.LogInformation($"Relating subscription found but status is {relatedSubscription.Status}, fixing it");
                            if (relatedSubscription.Status == "Scheduled" && relatedSubscription.StartDate > IndianTime.Now)
                            {
                                _logger.LogInformation($"Fixing subscription status from {relatedSubscription.Status} to Scheduled");
                                relatedSubscription.Status = "Scheduled";
                                await _masterDb.SaveChangesAsync();
                                scheduledSubscription = relatedSubscription;
                            }
                        }
                    }
                }
            }



            // Get cancelled subscriptions with pending refunds
            var cancelledWithPendingRefund = await _masterDb.TenantSubscriptions
                .Where(s => s.TenantId == tenantId.Value &&
                           s.Status == "Cancelled" &&
                           s.CancellationReason != null &&
                           s.CancellationReason.Contains("Refund Pending"))
                .ToListAsync();

            ViewBag.CurrentSubscription = currentSubscription;
            ViewBag.ScheduledSubscription = scheduledSubscription;
            ViewBag.CancelledWithPendingRefund = cancelledWithPendingRefund;
            ViewBag.AvailablePlans = availablePlans;
            ViewBag.TenantId = tenantId.Value;
            ViewBag.RazorpayKeyId = (await _masterDb.SaasPaymentConfig.FirstOrDefaultAsync(c => c.IsActive))?.RazorpayKeyId ?? "";

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> CancelCurrentPlan(int tenantId)
        {
            try
            {
                var currentSubscription = await _masterDb.TenantSubscriptions
                    .Where(s => s.TenantId == tenantId && s.Status == "Active")
                    .FirstOrDefaultAsync();

                if (currentSubscription == null)
                    return Json(new { success = false, message = "No active subscription found" });

                // Cancel the subscription immediately
                currentSubscription.Status = "Cancelled";
                currentSubscription.CancelledOn = IndianTime.Now;
                currentSubscription.EndDate = IndianTime.Now;
                currentSubscription.CancellationReason = "Cancelled by user - Refund Pending";
                currentSubscription.UpdatedOn = IndianTime.Now;

                _masterDb.TenantSubscriptions.Update(currentSubscription);
                await _masterDb.SaveChangesAsync();

                _logger.LogInformation($"Partner {tenantId} cancelled subscription {currentSubscription.SubscriptionId}. Refund pending.");

                return Json(new
                {
                    success = true,
                    message = "Plan cancelled successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error cancelling plan for partner {tenantId}");
                return Json(new { success = false, message = "Error cancelling plan" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CancelScheduledPlan(int subscriptionId)
        {
            var (userId, role, tenantId) = GetCurrentUserContext();

            if (!tenantId.HasValue)
                return Json(new { success = false, message = "Partner context not found" });

            try
            {
                var scheduledSubscription = await _masterDb.TenantSubscriptions
                    .Where(s => s.SubscriptionId == subscriptionId &&
                               s.TenantId == tenantId.Value &&
                               s.Status == "Scheduled")
                    .FirstOrDefaultAsync();

                if (scheduledSubscription == null)
                    return Json(new { success = false, message = "Scheduled subscription not found" });



                // Get the original payment transaction to fetch card details
                var paymentTransaction = await _masterDb.SaasPaymentTransactions
                    .Where(t => t.SubscriptionId == subscriptionId && t.Status == "Success")
                    .OrderByDescending(t => t.TransactionDate)
                    .FirstOrDefaultAsync();

                string cardInfo = "original payment method";
                if (paymentTransaction != null &&
                    !string.IsNullOrEmpty(paymentTransaction.CardNetwork) &&
                    !string.IsNullOrEmpty(paymentTransaction.CardLast4))
                {
                    cardInfo = $"{paymentTransaction.CardNetwork} **** {paymentTransaction.CardLast4}";
                    if (!string.IsNullOrEmpty(paymentTransaction.CardType))
                    {
                        cardInfo += $" ({paymentTransaction.CardType})";
                    }
                }

                scheduledSubscription.Status = "Cancelled";
                scheduledSubscription.CancelledOn = IndianTime.Now;
                scheduledSubscription.CancellationReason = $"Cancelled By user - refund pending: {scheduledSubscription.Amount:NO}. Refund will be processed to {cardInfo}";
                scheduledSubscription.UpdatedOn = IndianTime.Now;

                // Create a cancellation transaction record for visibility in Transactions page
                var cancellationTransaction = new SaasPaymentTransactionModel
                {
                    TenantId = tenantId.Value,
                    SubscriptionId = subscriptionId,
                    TransactionReference = $"CANCEL_{subscriptionId}_{IndianTime.Now.Ticks}",
                    Amount = scheduledSubscription.Amount,
                    Currency = "INR",
                    Status = "Cancelled",
                    RefundStatus = "Pending",
                    TransactionType = "Cancellation",
                    PaymentMethod = "User Cancellation",
                    TransactionDate = IndianTime.Now,
                    Description = $"Scheduled plan cancelled by user - {scheduledSubscription.Plan?.PlanName} Refund Pending.",
                    PlanName = scheduledSubscription.Plan?.PlanName,
                    BillingCycle = scheduledSubscription.BillingCycle,
                    NetAmount = scheduledSubscription.Amount,
                    CreatedOn = IndianTime.Now
                };

                _masterDb.SaasPaymentTransactions.Add(cancellationTransaction);
                await _masterDb.SaveChangesAsync();

                _logger.LogInformation($"User cancelled scheduled plan {subscriptionId} for partner {scheduledSubscription.Plan?.PlanName}");

                return Json(new
                {
                    success = true,
                    refundPending = true,
                    refundAmount = scheduledSubscription.Amount,
                    message = $"Your scheduled {scheduledSubscription.Plan.PlanName} plan has been cancelled successfully."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error cancelling scheduled plan {subscriptionId}");
                return Json(new { success = false, message = "Error cancelling scheduled plan" });
            }
        }

        // --- Reporting: Transactions ---

        //[HttpGet]
        //[AllowAnonymous]
        //public async Task<IActionResult> GetupUpgradeOptions(int tenantId)
        //{
        //    try
        //    {
        //        var partner = await _masterDb.Tenants.FindAsync(tenantId);
        //        if (partner == null) return Json(new { success = false, message = "Partner not found" });

        //        var currentSubscription = await _masterDb.TenantSubscriptions
        //            .Where(s => s.TenantId == tenantId && s.Status == "Active")
        //            .FirstOrDefaultAsync();



        //        var availablePlans = await _masterDb.SaasPlans
        //            .Where(p => p.IsActive == true)
        //            .OrderBy(p => p.SortOrder)
        //            
        //            .ToListAsync();

        //        return Json(new
        //        {
        //            success = true,
        //            hasActivePlan = currentSubscription != null,
        //            partner = new
        //            {
        //                tenantId = partner.TenantId,
        //                companyName = partner.CompanyName,
        //                email = partner.CompanyName
        //            },
        //            currentSubscription = currentSubscription != null ? new
        //            {
        //                subscriptionId = currentSubscription.SubscriptionId,
        //                planId = currentSubscription.PlanId,
        //                planName = currentSubscription.Plan.PlanName,
        //                amount = currentSubscription.Amount > 0 ? currentSubscription.Amount : CalculateRemainingAmount(currentSubscription),
        //                billingCycle = currentSubscription.BillingCycle,
        //                startDate = currentSubscription.StartDate.ToString("MMM dd, yyyy"),
        //                endDate = currentSubscription.EndDate.ToString("MMM dd, yyyy"),
        //                daysRemaining = (currentSubscription.EndDate - IndianTime.Now).Days,
        //                status = currentSubscription.Status
        //            } : null,



        //            availablePlans = availablePlans.Select(p => new
        //            {
        //                planId = p.PlanId,
        //                planName = p.PlanName ?? "",
        //                description = p.Description ?? "",
        //                monthlyPrice = p.MonthlyPrice,
        //                yearlyPrice = p.YearlyPrice,
        //                maxAgents = p.MaxAgents,
        //                maxLeadsPerMonth = p.MaxLeadsPerMonth,
        //                maxStorageGB = p.MaxStorageGB,
        //                hasWhatsAppIntegration = p.HasWhatsAppIntegration,
        //                hasFacebookIntegration = p.HasFacebookIntegration,
        //                hasEmailIntegration = p.HasEmailIntegration,
        //                hasAdvancedReports = p.HasAdvancedReports,
        //                hasDataExport = p.HasAdvancedReports,
        //                supportLevel = p.SupportLevel ?? ""
        //            }).ToList()
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        return Json(new { success = false, message = $"Error: {ex.Message}" });
        //    }
        //}

        //    }
        //}

        [HttpGet]
        [AllowAnonymous]
        // 0 references
        public async Task<IActionResult> GetUpgradeOptions(int tenantId)
        {
            try
            {
                var partner = await _masterDb.Tenants.FindAsync(tenantId);
                if (partner == null)
                    return Json(new { success = false, message = "Partner not found" });

                var currentSubscription = await _masterDb.TenantSubscriptions
                    .Where(s => s.TenantId == tenantId && s.Status == "Active")
                    .FirstOrDefaultAsync();

                var scheduledSubscription = await _masterDb.TenantSubscriptions
                    .Where(s => s.TenantId == tenantId && s.Status == "Scheduled")
                    .OrderByDescending(s => s.CreatedOn)
                    .FirstOrDefaultAsync();

                var availablePlans = await _masterDb.SaasPlans
                    .Where(p => p.IsActive == true)
                    .OrderBy(p => p.SortOrder)
                    
                    .ToListAsync();

                // For display: if amount is 0 but plan has a price, show the plan price
                decimal displayAmount = 0;
                if (currentSubscription != null)
                {
                    displayAmount = currentSubscription.Amount > 0
                        ? currentSubscription.Amount
                        : (currentSubscription.Plan != null
                            ? (currentSubscription.BillingCycle?.ToLower() == "annual"
                                ? currentSubscription.Plan.YearlyPrice
                                : currentSubscription.Plan.MonthlyPrice)
                            : 0);
                }

                return Json(new
                {
                    success = true,
                    hasActivePlan = currentSubscription != null,
                    partner = new
                    {
                        tenantId = partner.TenantId,
                        companyName = partner.CompanyName ?? "",
                        email = partner.Email ?? partner.CompanyName ?? ""
                    },
                    currentSubscription = currentSubscription != null ? new
                    {
                        subscriptionId = currentSubscription.SubscriptionId,
                        planId = currentSubscription.PlanId,
                        planName = currentSubscription.Plan?.PlanName ?? "",
                        amount = displayAmount,
                        billingCycle = currentSubscription.BillingCycle ?? "monthly",
                        startDate = currentSubscription.StartDate.ToString("yyyy-MM-dd"),
                        endDate = currentSubscription.EndDate.ToString("yyyy-MM-dd"),
                        daysRemaining = (currentSubscription.EndDate - IndianTime.Now).Days,
                        status = currentSubscription.Status ?? ""
                    } : null,
                    scheduledSubscription = scheduledSubscription != null ? new
                    {
                        subscriptionId = scheduledSubscription.SubscriptionId,
                        planId = scheduledSubscription.PlanId,
                        planName = scheduledSubscription.Plan?.PlanName ?? "",
                        amount = scheduledSubscription.Amount,
                        billingCycle = scheduledSubscription.BillingCycle ?? "monthly",
                        startDate = scheduledSubscription.StartDate.ToString("yyyy-MM-dd"),
                        endDate = scheduledSubscription.EndDate.ToString("yyyy-MM-dd")
                    } : null,
                    availablePlans = availablePlans.Select(p => new
                    {
                        planId = p.PlanId,
                        planName = p.PlanName ?? "",
                        description = p.Description ?? "",
                        monthlyPrice = p.MonthlyPrice,
                        yearlyPrice = p.YearlyPrice,
                        maxAgents = p.MaxAgents,
                        maxLeadsPerMonth = p.MaxLeadsPerMonth,
                        maxStorageGB = p.MaxStorageGB,
                        hasWhatsAppIntegration = p.HasWhatsAppIntegration,
                        hasFacebookIntegration = p.HasFacebookIntegration,
                        hasEmailIntegration = p.HasEmailIntegration,
                        hasAdvancedReports = p.HasAdvancedReports,
                        hasDataExport = p.HasAdvancedReports,
                        supportLevel = p.SupportLevel ?? ""
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // 0 references

        private decimal CalculateRemainingAmount(TenantSubscriptionModel subscription)
        {
            var totalDays = Math.Max(1, (subscription.EndDate - subscription.StartDate).Days);
            var remainingDays = Math.Max(0, (subscription.EndDate - IndianTime.Now).Days);

            if (totalDays <= 0 || remainingDays <= 0) return 0;

            // For existing upgrade subscriptions with 0, calculate based on original remaining amount logic
            if (subscription.Amount == 0)
            {
                // This is from existing upgrade - calculate actual remaining amount
                // Trial/Free subscriptions get zero credit
                // Find the original Basic Plan subscription amount to calculate per-day rate
                var perDayRate = 0m;
                return Math.Round(perDayRate * remainingDays, 2);
            }

            var subscriptionperDayRate = subscription.Amount / totalDays;
            return Math.Round(subscriptionperDayRate * remainingDays, 2);
        }




        private async Task<string> CreateSaasRazorpayOrder(decimal amount, string receipt)
        {
            var saasConfig = await _masterDb.SaasPaymentConfig.FirstOrDefaultAsync(c => c.IsActive);
            if (saasConfig == null) throw new Exception("Razorpay not configured. The Super Admin must go to Finance → Payment Config in the sidebar and save the Razorpay Key ID and Key Secret before subscriptions can process payments.");

            using var hc = new HttpClient();
            hc.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{saasConfig.RazorpayKeyId}:{saasConfig.RazorpayKeySecret}")));

            var payload = System.Text.Json.JsonSerializer.Serialize(new { amount = (amount * 100), currency = "INR", receipt = receipt });
            var resp = await hc.PostAsync("https://api.razorpay.com/v1/orders", new StringContent(payload, Encoding.UTF8, "application/json"));
            var js = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) throw new Exception("Razorpay" + js);
            return System.Text.Json.JsonDocument.Parse(js).RootElement.GetProperty("id").GetString();
        }

        //[HttpPost]
        //public async Task<IActionResult> calculateupgrade(int tenantId, int newPlanId, string billingCycle, string upgradeType)
        //{
        //    try
        //    {
        //        var currentSubscription = await _masterDb.TenantSubscriptions
        //            .Where(s => s.TenantId == tenantId && s.Status == "Active")
        //            .FirstOrDefaultAsync();

        //        var newPlan = await _masterDb.SaasPlans.FindAsync(newPlanId);
        //        if (newPlan == null) return Json(new { success = false, message = "Plan not found" });

        //        var newAmount = billingCycle.ToLower() == "annual" ? newPlan.YearlyPrice : newPlan.MonthlyPrice;

        //        // Check if this is an activation request
        //        var activateNow = Request.Form["activateNow"].ToString() == "true";

        //        IActionResult calculationResult;
        //        switch (upgradeType.ToLower())
        //        {
        //            case "existing":
        //                // Trial/free subscriptions cannot use existing upgrade (no amount to convert)
        //                if (currentSubscription == null || currentSubscription.Amount == 0 || (currentSubscription.BillingCycle == "Trial"))
        //                {
        //                    return Json(new { success = false, message = "Cannot use 'Existing Upgrade' for free trial. Please choose 'Immediate Upgrade' or 'Scheduled Plan'." });
        //                }
        //                calculationResult = CalculateExistingPlanUpgrade(currentSubscription, newPlan, newAmount, billingCycle);
        //                break;
        //            case "immediate":
        //                calculationResult = CalculateImmediateUpgrade(currentSubscription, newPlan, newAmount, billingCycle);
        //                break;
        //            case "scheduled":
        //                calculationResult = CalculateScheduledPlan(currentSubscription, newPlan, newAmount, billingCycle);
        //                break;
        //            default:
        //                return Json(new { success = false, message = "Invalid upgrade type" });
        //        }

        //        if (activateNow == true)
        //        {
        //            return await ActivateUpgradeNow(tenantId, newPlanId, billingCycle, upgradeType, currentSubscription, newPlan, newAmount);
        //        }

        //        return calculationResult;
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error calculating upgrade");
        //        return Json(new { success = false, message = "Error calculating upgrade" });
        //    }
        //}
        [HttpPost]
        [AllowAnonymous]
        // 0 references
        public async Task<IActionResult> CalculateUpgrade(int tenantId, int newPlanId, string billingCycle, string upgradeType)
        {
            try
            {
                var currentSubscription = await _masterDb.TenantSubscriptions
                    .Where(s => s.TenantId == tenantId && s.Status == "Active")
                    .FirstOrDefaultAsync();

                var newPlan = await _masterDb.SaasPlans.FindAsync(newPlanId);
                if (newPlan == null)
                    return Json(new { success = false, message = "Plan not found" });

                var newAmount = billingCycle.ToLower() == "annual" ? newPlan.YearlyPrice : newPlan.MonthlyPrice;

                // Check if this is an activation request
                var activateNow = Request.Form["activateNow"].ToString() == "true";

                IActionResult calculationResult;
                switch (upgradeType.ToLower())
                {
                    case "existing":
                        // No active subscription at all
                        if (currentSubscription == null)
                        {
                            return Json(new
                            {
                                success = false,
                                insufficientAmount = true,
                                message = "No active subscription found",
                                calculation = new
                                {
                                    currentPlan = "None",
                                    remainingAmount = 0,
                                    requiredAmount = Math.Round(newAmount / (billingCycle.ToLower() == "annual" ? 365 : 30), 2),
                                    shortfall = Math.Round(newAmount / (billingCycle.ToLower() == "annual" ? 365 : 30), 2)
                                }
                            });
                        }

                        // Trial subscriptions - show insufficient amount
                        if (currentSubscription.BillingCycle == "Trial")
                        {
                            var upgDays = billingCycle.ToLower() == "annual" ? 365 : 30;
                            var reqPerDay = Math.Round(newAmount / upgDays, 2);
                            return Json(new
                            {
                                success = false,
                                insufficientAmount = true,
                                message = "Trial/free plans have no remaining value to convert. Please choose 'Immediate Upgrade' (pay the difference) or 'Scheduled Plan' (full payment, starts after trial expires) to upgrade.",
                                calculation = new
                                {
                                    currentPlan = currentSubscription.Plan?.PlanName ?? "Trial",
                                    currentAmount = 0,
                                    remainingDays = Math.Max(0, (currentSubscription.EndDate - IndianTime.Now).Days),
                                    remainingAmount = 0,
                                    perDayRate = 0,
                                    newPlan = newPlan.PlanName,
                                    newAmount = newAmount,
                                    upgradePerDayRate = reqPerDay,
                                    requiredAmount = reqPerDay,
                                    shortfall = reqPerDay
                                }
                            });
                        }

                        calculationResult = CalculateExistingPlanUpgrade(currentSubscription, newPlan, newAmount, billingCycle);
                        break;

                    case "immediate":
                        calculationResult = CalculateImmediateUpgrade(currentSubscription, newPlan, newAmount, billingCycle, tenantId);
                        break;

                    case "scheduled":
                        calculationResult = CalculateScheduledPlan(currentSubscription, newPlan, newAmount, billingCycle, tenantId);
                        break;

                    default:
                        return Json(new { success = false, message = "Invalid upgrade type" });
                }

                // If activateNow is true, perform the actual activation
                if (activateNow)
                {
                    return await ActivateUpgradeNow(tenantId, newPlanId, billingCycle, upgradeType, currentSubscription, newPlan, newAmount);
                }

                return calculationResult;
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error calculating upgrade" });
            }
        }


        // --- Upgrade Engine: Calculations & Helpers ---


        //private async Task<IActionResult> ActivateUpgradeNow(int tenantId, int newPlanId, string billingCycle, string upgradeType, TenantSubscriptionModel currentSubscription, SaasSubscriptionPlanModel newPlan, decimal newAmount)
        //{
        //    using var transaction = await _masterDb.Database.BeginTransactionAsync();
        //    try
        //    {
        //        _logger.LogInformation($"ActivateUpgradeNow: tenantId={tenantId}, newPlanId={newPlanId}, billingCycle={billingCycle}, upgradeType={upgradeType}");

        //        DateTime startDate = IndianTime.Now;
        //        DateTime endDate;
        //        decimal actualAmountPaid = newAmount; // Default to full amount

        //        // Calculate and date based on upgrade type
        //        switch (upgradeType.ToLower())
        //        {
        //            case "existing":
        //                if (currentSubscription != null)
        //                {
        //                    var totalDays = Math.Max(1, (currentSubscription.EndDate - currentSubscription.StartDate).Days);
        //                    var remainingDays = Math.Max(0, (currentSubscription.EndDate - IndianTime.Now).Days);

        //                    _logger.LogInformation($"Existing Upgrade: TotalDays={totalDays}, remainingDays={remainingDays}, currentAmount={currentSubscription.Amount}");

        //                    if (totalDays > 0 && remainingDays > 0)
        //                    {
        //                        // Calculate actual remaining amount and converted days
        //                        decimal actualCurrentAmount = currentSubscription.Amount;
        //                        decimal perDayRate;

        //                        if (currentSubscription.Amount == 0)
        //                        {
        //                            var originalAmount = 98m;
        //                            perDayRate = 0;
        //                            actualCurrentAmount = 0;
        //                        }
        //                        else
        //                        {
        //                            perDayRate = currentSubscription.Amount / totalDays;
        //                        }

        //                        var remainingAmount = perDayRate * remainingDays;
        //                        var upgradePerDayRate = newAmount / (billingCycle.ToLower() == "annual" ? 365 : 30);

        //                        var convertedDays = (int)(remainingAmount / upgradePerDayRate);
        //                        if ((remainingAmount % upgradePerDayRate) > 0) convertedDays += 1;

        //                        _logger.LogInformation($"Converted days calculation: remainingAmount={remainingAmount}, upgradePerDayRate={upgradePerDayRate}, convertedDays={convertedDays}");

        //                        endDate = startDate.AddDays(convertedDays);

        //                        // Store the actual remaining amount paid, not full plan price
        //                        actualAmountPaid = Math.Round(remainingAmount, 2);
        //                    }
        //                    else
        //                    {
        //                        _logger.LogWarning($"Invalid subscription data: totalDays={totalDays}, remainingDays={remainingDays}, using default duration");
        //                        endDate = billingCycle.ToLower() == "annual" ? startDate.AddYears(1) : startDate.AddMonths(1);
        //                    }
        //                }
        //                else
        //                {
        //                    _logger.LogWarning("No current subscription found, using default duration");
        //                    endDate = billingCycle.ToLower() == "annual" ? startDate.AddYears(1) : startDate.AddMonths(1);
        //                }
        //                break;

        //            case "immediate":
        //                endDate = billingCycle.ToLower() == "annual" ? startDate.AddYears(1) : startDate.AddMonths(1);

        //                // Calculate credit amount for immediate upgrade
        //                if (currentSubscription != null)
        //                {
        //                    var totalDays = Math.Max(1, (currentSubscription.EndDate - currentSubscription.StartDate).Days);
        //                    var remainingDays = Math.Max(0, (currentSubscription.EndDate - IndianTime.Now).Days);

        //                    decimal perDayRate;
        //                    if (currentSubscription.Amount == 0)
        //                    {
        //                        var originalAmount = 98m;
        //                        perDayRate = 0;
        //                    }
        //                    else
        //                    {
        //                        perDayRate = currentSubscription.Amount / totalDays;
        //                    }

        //                    var creditAmount = Math.Round(perDayRate * remainingDays, 2);
        //                    actualAmountPaid = Math.Max(0, newAmount - creditAmount);
        //                }
        //                break;

        //            case "scheduled":
        //                startDate = currentSubscription?.EndDate ?? IndianTime.Now;
        //                endDate = billingCycle.ToLower() == "annual" ? startDate.AddYears(1) : startDate.AddMonths(1);
        //                break;

        //            default:
        //                endDate = billingCycle.ToLower() == "annual" ? startDate.AddYears(1) : startDate.AddMonths(1);
        //                break;
        //        }

        //        // End current subscription if exists
        //        if (currentSubscription != null)
        //        {
        //            currentSubscription.Status = "Expired";
        //            currentSubscription.EndDate = IndianTime.Now;
        //            currentSubscription.UpdatedOn = IndianTime.Now;
        //            currentSubscription.CancellationReason = $"Upgraded to {newPlan.PlanName} via admin activation";
        //            currentSubscription.CancelledOn = IndianTime.Now;
        //        }

        //        // Create new subscription
        //        var newSub = new TenantSubscriptionModel
        //        {
        //            TenantId = tenantId,
        //            PlanId = newPlanId,
        //            BillingCycle = billingCycle,
        //            Amount = actualAmountPaid, // Store actual amount paid, not full plan price
        //            StartDate = startDate,
        //            EndDate = endDate,
        //            Status = "Active",
        //            PaymentMethod = "Admin Activation",
        //            PaymentTransactionId = $"admin_activation_{IndianTime.Now.Ticks}",
        //            LastPaymentDate = IndianTime.Now,
        //            NextPaymentDate = endDate,
        //            AutoRenew = false,
        //            CreatedOn = IndianTime.Now,
        //            UpdatedOn = IndianTime.Now
        //        };








        //        _masterDb.TenantSubscriptions.Add(newSub);
        //        await _masterDb.SaveChangesAsync();

        //        // Create payment transaction record
        //        var payTransaction = new SaasPaymentTransactionModel
        //        {
        //            TenantId = tenantId,
        //            SubscriptionId = newSub.SubscriptionId,
        //            TransactionReference = $"ADMIN_ACTIVATION_{IndianTime.Now:yyyyMMddHHmmss}",
        //            Amount = actualAmountPaid,
        //            Currency = "INR",
        //            Status = "Success",
        //            TransactionType = "Admin Activation",
        //            PaymentMethod = "Admin",
        //            TransactionDate = IndianTime.Now,
        //            CompletedDate = IndianTime.Now,
        //            Description = $"Admin activated {newPlan.PlanName} plan ({billingCycle}) - {upgradeType} upgrade",
        //            PlanName = newPlan.PlanName,
        //            BillingCycle = billingCycle,
        //            CreatedOn = IndianTime.Now
        //        };

        //        _masterDb.SaasPaymentTransactions.Add(payTransaction);
        //        await _masterDb.SaveChangesAsync();

        //        await transaction.CommitAsync();

        //        _logger.LogInformation($"Admin activated {newPlan.PlanName} plan for partner {tenantId} via {upgradeType} upgrade");

        //        return Json(new
        //        {
        //            success = true,
        //            message = $"Plan activated successfully! {newPlan.PlanName} is now active until {endDate:MMM dd, yyyy}."
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        await transaction.RollbackAsync();
        //        _logger.LogError(ex, $"Error activating upgrade for partner {tenantId}");
        //        return Json(new { success = false, message = $"Activation failed: {ex.Message}" });
        //    }
        //}
        private async Task<IActionResult> ActivateUpgradeNow(
     int tenantId,
     int newPlanId,
     string billingCycle,
     string upgradeType,
     TenantSubscriptionModel currentSubscription,
     SaasSubscriptionPlanModel newPlan,
     decimal newAmount)
        {
            // For existing upgrade, validate sufficient amount before activation
            if (upgradeType.ToLower() == "existing")
            {
                if (currentSubscription == null || currentSubscription.BillingCycle == "Trial")
                {
                    return Json(new { success = false, message = "Trial/free plans have no remaining value to convert. Please select 'Immediate Upgrade' (credit applied, pay the difference) or 'Scheduled Plan' (full payment, starts after current plan) instead." });
                }

                // Get actual amount - if stored as 0, use plan price
                decimal checkAmount = currentSubscription.Amount;
                if (checkAmount == 0 && currentSubscription.Plan != null)
                {
                    checkAmount = currentSubscription.BillingCycle?.ToLower() == "annual"
                        ? currentSubscription.Plan.YearlyPrice
                        : currentSubscription.Plan.MonthlyPrice;
                }

                if (checkAmount == 0)
                {
                    return Json(new { success = false, message = "Insufficient amount. Please choose Immediate or Scheduled." });
                }

                var totalD = Math.Max(1, (currentSubscription.EndDate - currentSubscription.StartDate).Days);
                var remainD = Math.Max(0, (currentSubscription.EndDate - IndianTime.Now).Days);
                var pdr = checkAmount / totalD;
                var remAmt = pdr * remainD;
                var upgD = billingCycle.ToLower() == "annual" ? 365 : 30;
                var upgPdr = newAmount / upgD;

                if (remAmt < upgPdr)
                {
                    return Json(new { success = false, message = $"Insufficient amount (?{Math.Round(remAmt, 2)}) for even 1 day of new plan (?{Math.Round(upgPdr, 2)}/day). Please choose Immediate or Scheduled." });
                }
            }

            using var transaction = await _masterDb.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation($"ActivateUpgradeNow: tenantId={tenantId}, newPlanId={newPlanId}, billingCycle={billingCycle}, upgradeType={upgradeType}, newAmount={newAmount}");

                DateTime startDate = IndianTime.Now;
                DateTime endDate;
                decimal actualAmountPaid = newAmount; // Default to full amount

                // Calculate end date based on upgrade type
                switch (upgradeType.ToLower())
                {
                    case "existing":
                        if (currentSubscription != null)
                        {
                            var totalDays = Math.Max(1, (currentSubscription.EndDate - currentSubscription.StartDate).Days);
                            var remainingDays = Math.Max(0, (currentSubscription.EndDate - IndianTime.Now).Days);

                            _logger.LogInformation($"Existing upgrade: totalDays={totalDays}, remainingDays={remainingDays}, currentAmount={currentSubscription.Amount}");

                            if (totalDays > 0 && remainingDays > 0)
                            {
                                // Get actual amount - if stored as 0, use plan price
                                decimal actualCurrentAmount = currentSubscription.Amount;
                                if (actualCurrentAmount == 0 && currentSubscription.Plan != null)
                                {
                                    actualCurrentAmount = currentSubscription.BillingCycle?.ToLower() == "annual"
                                        ? currentSubscription.Plan.YearlyPrice
                                        : currentSubscription.Plan.MonthlyPrice;
                                }

                                decimal perDayRate = actualCurrentAmount > 0 ? actualCurrentAmount / totalDays : 0;
                                var remainingAmount = perDayRate * remainingDays;
                                var upgradeDays = billingCycle.ToLower() == "annual" ? 365 : 30;
                                var upgradePerDayRate = newAmount / upgradeDays;

                                var convertedDays = (int)(remainingAmount / upgradePerDayRate);
                                if ((remainingAmount % upgradePerDayRate) > 0) convertedDays += 1;

                                _logger.LogInformation($"Converted days calculation: remainingAmount={remainingAmount}, upgradePerDayRate={upgradePerDayRate}, convertedDays={convertedDays}");

                                endDate = startDate.AddDays(convertedDays);
                                actualAmountPaid = newAmount; // Store full plan price for display
                            }
                            else
                            {
                                _logger.LogWarning($"Invalid subscription data: totalDays={totalDays}, remainingDays={remainingDays}, using default duration");
                                endDate = billingCycle.ToLower() == "annual" ? startDate.AddYears(1) : startDate.AddMonths(1);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("No current subscription found, using default duration");
                            endDate = billingCycle.ToLower() == "annual" ? startDate.AddYears(1) : startDate.AddMonths(1);
                        }
                        break;

                    case "immediate":
                        endDate = billingCycle.ToLower() == "annual" ? startDate.AddYears(1) : startDate.AddMonths(1);

                        // Calculate credit amount for immediate upgrade
                        if (currentSubscription != null)
                        {
                            var totalDays = Math.Max(1, (currentSubscription.EndDate - currentSubscription.StartDate).Days);
                            var remainingDays = Math.Max(0, (currentSubscription.EndDate - IndianTime.Now).Days);

                            decimal creditBase = currentSubscription.Amount;
                            if (creditBase == 0 && currentSubscription.Plan != null)
                            {
                                creditBase = currentSubscription.BillingCycle?.ToLower() == "annual"
                                    ? currentSubscription.Plan.YearlyPrice
                                    : currentSubscription.Plan.MonthlyPrice;
                            }

                            decimal perDayRate = (creditBase > 0 && currentSubscription.BillingCycle != "Trial") ? creditBase / totalDays : 0;
                            var creditAmount = Math.Round(perDayRate * remainingDays, 2);
                            actualAmountPaid = Math.Max(0, newAmount - creditAmount);
                            //actualAmountPaid = Math.Max(0, actualAmountPaid - walletUsed);
                        }
                        break;

                    case "scheduled":
                        startDate = currentSubscription?.EndDate.AddDays(1) ?? IndianTime.Now;
                        endDate = billingCycle.ToLower() == "annual" ? startDate.AddYears(1) : startDate.AddMonths(1);
                        break;

                    default:
                        endDate = billingCycle.ToLower() == "annual" ? startDate.AddYears(1) : startDate.AddMonths(1);
                        break;
                }

                // End current subscription if exists
                if (currentSubscription != null)
                {
                    currentSubscription.Status = "Expired";
                    currentSubscription.EndDate = IndianTime.Now;
                    currentSubscription.UpdatedOn = IndianTime.Now;
                    currentSubscription.CancellationReason = $"Upgraded to {newPlan.PlanName} via admin activation";
                    currentSubscription.CancelledOn = IndianTime.Now;
                }

                _logger.LogInformation($"Final actualAmountPaid before creating subscription: {actualAmountPaid}");

                decimal walletUsed = 0;

                // ?? Calculate referral usage
                var totalReferral = await _masterDb.ReferralEarnings
                    .Where(r => r.TenantId == tenantId && !r.IsUsed)
                    .SumAsync(r => (decimal?)r.Amount) ?? 0;

                if (totalReferral > 0)
                {
                    walletUsed = Math.Min(totalReferral, actualAmountPaid);

                    await DeductReferralBalance(tenantId, walletUsed);

                    //actualAmountPaid -= walletUsed;
                    actualAmountPaid = Math.Max(0, actualAmountPaid - walletUsed);
                }

                _logger.LogInformation($"Wallet used: {walletUsed}, Final payable: {actualAmountPaid}");

                // Create new subscription
                var newSubscription = new TenantSubscriptionModel
                {
                    TenantId = tenantId,
                    PlanId = newPlanId,
                    BillingCycle = billingCycle,
                    Amount = actualAmountPaid,
                    StartDate = startDate,
                    EndDate = endDate,
                    Status = "Active",
                    PaymentMethod = actualAmountPaid == 0 ? "Wallet" : "Mixed",
                    PaymentTransactionId = $"admin_activation_{IndianTime.Now.Ticks}",
                    LastPaymentDate = IndianTime.Now,
                    NextPaymentDate = endDate,
                    AutoRenew = false,
                    CreatedOn = IndianTime.Now,
                    UpdatedOn = IndianTime.Now
                };

                _masterDb.TenantSubscriptions.Add(newSubscription);
                await _masterDb.SaveChangesAsync();

                // Create transaction record
                var paymentTransaction = new SaasPaymentTransactionModel
                {
                    TenantId = tenantId,
                    SubscriptionId = newSubscription.SubscriptionId,
                    TransactionReference = $"ADMIN_ACTIVATION_{IndianTime.Now:yyyyMMddHHmmss}",
                    Amount = actualAmountPaid,
                    Currency = "INR",
                    Status = "Success",
                    TransactionType = "Admin Activation",
                    PaymentMethod = actualAmountPaid == 0 ? "Wallet" :
                                       walletUsed > 0 ? "Mixed" : "Online",
                    TransactionDate = IndianTime.Now,
                    CompletedDate = IndianTime.Now,
                    NetAmount = actualAmountPaid,
                    Description = $"Upgrade using ?{walletUsed} wallet + ?{actualAmountPaid} payment",
                    PlanName = newPlan.PlanName,
                    BillingCycle = billingCycle,
                    CreatedOn = IndianTime.Now
                };

                _masterDb.SaasPaymentTransactions.Add(paymentTransaction);
                await _masterDb.SaveChangesAsync();

                await transaction.CommitAsync();

                _logger.LogInformation($"Admin activated {newPlan.PlanName} plan for partner {tenantId} via {upgradeType} upgrade");

                return Json(new
                {
                    success = true,
                    message = $"Plan activated successfully! {newPlan.PlanName} is now active until {endDate:MMM dd, yyyy}."
                });
            }
            catch (Exception ex)
            {

                _logger.LogError(ex, $"Error activating upgrade for partner {tenantId}");
                return Json(new { success = false, message = $"Activation failed: {ex.Message}" });
            }
        }



        private IActionResult CalculateExistingPlanUpgrade(TenantSubscriptionModel currentSubscription, SaasSubscriptionPlanModel newPlan, decimal newAmount, string billingCycle)
        {
            if (currentSubscription == null)
                return Json(new { success = false, message = "No active subscription found" });

            var totalDays = Math.Max(1, (currentSubscription.EndDate - currentSubscription.StartDate).Days);
            var remainingDays = Math.Max(0, (currentSubscription.EndDate - IndianTime.Now).Days);

            if (totalDays <= 0 || remainingDays <= 0)
                return Json(new { success = false, message = "No remaining days in current subscription" });

            // Calculate actual remaining amount using the same logic as CalculateRemainingAmount
            decimal actualCurrentAmount = currentSubscription.Amount;
            if (actualCurrentAmount == 0 && currentSubscription.Plan != null)
            {
                actualCurrentAmount = currentSubscription.BillingCycle?.ToLower() == "annual" ? currentSubscription.Plan.YearlyPrice : currentSubscription.Plan.MonthlyPrice;
            }
            ;

            decimal perDayRate;
            if (actualCurrentAmount == 0 || currentSubscription.BillingCycle == "Trail")
            {
                perDayRate = 0;
            }
            else
            {
                perDayRate = actualCurrentAmount / totalDays;
            }
            var remainingAmount = Math.Round(perDayRate * remainingDays, 2);

            var upgradeDays = billingCycle.ToLower() == "annual" ? 365 : 30;
            var upgradePerDayRate = newAmount / upgradeDays;

            // Check if remaining amount is sufficient for at least 1 day of new plan
            if (remainingAmount < upgradePerDayRate)
            {
                return Json(new
                {
                    success = false,
                    insufficientAmount = true,
                    message = "Insufficient amount for upgrade",
                    calculation = new
                    {
                        currentPlan = currentSubscription.Plan?.PlanName,
                        currentAmount = actualCurrentAmount,
                        remainingDays = remainingDays,
                        remainingAmount = remainingAmount,
                        perDayRate = Math.Round(perDayRate, 2),
                        newPlan = newPlan.PlanName,
                        newAmount = newAmount,
                        upgradePerDayRate = Math.Round(upgradePerDayRate, 2),
                        requiredAmount = Math.Round(upgradePerDayRate, 2),
                        shortfall = Math.Round(upgradePerDayRate - remainingAmount, 2)
                    }
                });
            }

            var convertedDays = (int)(remainingAmount / upgradePerDayRate);
            var remainingAfterConversion = remainingAmount - (convertedDays * upgradePerDayRate);

            if (remainingAfterConversion > 0)
            {
                convertedDays += 1;
            }

            var upgradeStartDate = IndianTime.Now;
            var upgradeEndDate = upgradeStartDate.AddDays(convertedDays);

            return Json(new
            {
                success = true,
                upgradeType = "existing",
                calculation = new
                {
                    currentPlan = currentSubscription.Plan?.PlanName,
                    currentAmount = actualCurrentAmount,
                    remainingDays = remainingDays,
                    remainingAmount = remainingAmount,
                    perDayRate = Math.Round(perDayRate, 2),
                    newPlan = newPlan.PlanName,
                    newAmount = newAmount,
                    upgradePerDayRate = Math.Round(upgradePerDayRate, 2),
                    convertedDays = convertedDays,
                    upgradeStartDate = upgradeStartDate.ToString("yyyy-MM-dd"),
                    upgradeEndDate = upgradeEndDate.ToString("yyyy-MM-dd"),
                    paymentRequired = 0
                }
            });
        }

        private (int? userId, string username, string role) GetUserDetailsFromToken()
        {
            string token = _httpContextAccessor.HttpContext?.Request.Cookies["jwtToken"];
            if (string.IsNullOrEmpty(token)) return (null, null, null);

            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(token);

                var userId = jwt.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
                var username = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
                var role = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;

                return (int.Parse(userId), username, role);
            }
            catch
            {
                return (null, null, null);
            }
        }

        private IActionResult CalculateImmediateUpgrade(TenantSubscriptionModel currentSubscription, SaasSubscriptionPlanModel newPlan, decimal newAmount, string billingCycle, int tenantId)
        {


            decimal adjustedAmount = newAmount;
            decimal creditAmount = 0;

            if (currentSubscription != null)
            {
                var totalDays = Math.Max(1, (currentSubscription.EndDate - currentSubscription.StartDate).Days);
                var remainingDays = Math.Max(0, (currentSubscription.EndDate - IndianTime.Now).Days);

                // Calculate actual remaining amount using same logic as CalculateRemainingAmount
                decimal actualCurrentAmount = currentSubscription.Amount;
                if (actualCurrentAmount == 0 && currentSubscription.Plan != null)
                {
                    actualCurrentAmount = currentSubscription.BillingCycle?.ToLower() == "annual" ? currentSubscription.Plan.YearlyPrice : currentSubscription.Plan.MonthlyPrice;
                }
            ;

                decimal perDayRate;
                if (actualCurrentAmount == 0 || currentSubscription.BillingCycle == "Trail")
                {
                    perDayRate = 0;
                }
                else
                {
                    perDayRate = actualCurrentAmount / totalDays;
                }


                creditAmount = Math.Round(perDayRate * remainingDays, 2);
                adjustedAmount = Math.Max(0, newAmount - creditAmount);
            }
            var startDate = IndianTime.Now;
            var endDate = billingCycle.ToLower() == "annual" ? startDate.AddYears(1) : startDate.AddMonths(1);

            // For display: show the actual amount used in calculation
            decimal displayCurrentAmount = currentSubscription?.Amount ?? 0;
            if (displayCurrentAmount == 0 && currentSubscription?.Plan != null)
            {
                displayCurrentAmount = currentSubscription.BillingCycle?.ToLower() == "annual"
                    ? currentSubscription.Plan.YearlyPrice
                    : currentSubscription.Plan.MonthlyPrice;
            }
            if (currentSubscription != null)
            {
                tenantId = currentSubscription.TenantId;

            }

            var earnings = _masterDb.ReferralEarnings
                .Where(r => r.TenantId == tenantId && !r.IsUsed)
                .ToList();



            var balance = earnings.Sum(e => e.Amount);
            var finalPayable = Math.Max(0, Math.Round(adjustedAmount, 2) - balance);


            return Json(new
            {
                success = true,
                upgradeType = "immediate",
                calculation = new
                {
                    currentPlan = currentSubscription?.Plan?.PlanName,
                    currentAmount = displayCurrentAmount,
                    remainingDays = currentSubscription != null ? Math.Max(0, (currentSubscription.EndDate - IndianTime.Now).Days) : 0,
                    creditAmount = Math.Round(creditAmount, 2),
                    newPlan = newPlan.PlanName,
                    newAmount = newAmount,
                    adjustedAmount = finalPayable, //Math.Round(adjustedAmount, 2) - balance,
                    rewardPoints = balance,
                    startDate = startDate.ToString("yyyy-MM-dd"),
                    endDate = endDate.ToString("yyyy-MM-dd"),
                    paymentRequired = finalPayable
                }
            });
        }

        private IActionResult CalculateScheduledPlan(TenantSubscriptionModel currentSubscription, SaasSubscriptionPlanModel newPlan, decimal newAmount, string billingCycle, int tenantId)
        {
            DateTime startDate;
            if (currentSubscription != null)
            {
                startDate = currentSubscription.EndDate.AddDays(1);

            }
            else
            {
                startDate = IndianTime.Now;
            }

            var endDate = billingCycle.ToLower() == "annual" ? startDate.AddYears(1) : startDate.AddMonths(1);
            var earnings = _masterDb.ReferralEarnings
        .Where(r => r.TenantId == tenantId && !r.IsUsed)
        .ToList();

            decimal balance = earnings.Sum(x => x.Amount);

            // ? FINAL PAYABLE
            decimal finalPayable = Math.Max(0, newAmount - balance);
            finalPayable = Math.Round(finalPayable, 2);

            return Json(new
            {
                success = true,
                upgradeType = "scheduled",
                calculation = new
                {
                    currentPlan = currentSubscription?.Plan?.PlanName,
                    newPlan = newPlan.PlanName,
                    newAmount = newAmount,
                    rewardPoints = balance,
                    adjustedAmount = finalPayable,

                    startDate = startDate.ToString("yyyy-MM-dd"),
                    endDate = endDate.ToString("yyyy-MM-dd"),

                    paymentRequired = finalPayable
                }
            });
        }

        [HttpPost]
        public async Task<IActionResult> CreatePaymentLink(int tenantId, int planId, string billingCycle, string upgradeType, decimal amount)
        {
            try
            {
                if (amount <= 0)
                {
                    return Json(new { success = false, message = $"Invalid amount: {amount}. Amount must be greater than 0." });
                }
                var partner = await _masterDb.Tenants.FindAsync(tenantId);
                if (partner == null) return Json(new { success = false, message = "Partner not found" });

                var plan = await _masterDb.SaasPlans.FindAsync(planId);
                if (plan == null) return Json(new { success = false, message = "Plan not found" });

                // Get Razorpay keys from SaasPaymentConfig (Master DB)
                var saasConfig = await _masterDb.SaasPaymentConfig.FirstOrDefaultAsync(c => c.IsActive);
                if (saasConfig == null) return Json(new { success = false, message = "Razorpay not configured. The Super Admin needs to go to Finance → Payment Config and enter their Razorpay Key ID & Secret in Settings first." });
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(saasConfig.RazorpayKeyId + ":" + saasConfig.RazorpayKeySecret)));
                var orderPayload = System.Text.Json.JsonSerializer.Serialize(new { amount = (int)(amount * 100), currency = "INR", receipt = $"upgrade_{tenantId}_{planId}" });
                var orderResponse = await httpClient.PostAsync("https://api.razorpay.com/v1/orders", new StringContent(orderPayload, System.Text.Encoding.UTF8, "application/json"));
                var orderJson = await orderResponse.Content.ReadAsStringAsync();
                if (!orderResponse.IsSuccessStatusCode) return Json(new { success = false, message = $"Failed to create payment order: {orderJson}" });
                var orderId = System.Text.Json.JsonDocument.Parse(orderJson).RootElement.GetProperty("id").GetString();


                // Create a temporary transaction record
                var upgradeRequest = new SaasPaymentTransactionModel
                {
                    TenantId = tenantId,
                    TransactionReference = orderId,
                    RazorpayOrderId = orderId,
                    Amount = amount,
                    Currency = "INR",
                    Status = "Pending",
                    TransactionType = $"Upgrade_{upgradeType}",
                    PaymentMethod = "Razorpay",
                    TransactionDate = IndianTime.Now,
                    PlanName = plan.PlanName,
                    Description = $"Plan upgarde to {plan.PlanName} ({billingCycle}-{upgradeType})",
                    BillingCycle = billingCycle,
                    CreatedOn = IndianTime.Now,
                    NetAmount = 0,

                };

                _masterDb.SaasPaymentTransactions.Add(upgradeRequest);
                await _masterDb.SaveChangesAsync();

                var paymentUrl = $"{Request.Scheme}://{Request.Host}/saassubscription/pay?orderId={orderId}";

                // Try sending email in background using EmailService with template
                bool emailSent = false;
                if (!string.IsNullOrEmpty(partner.Email))
                {
                    var scopeFactory = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();
                    var partnerEmail = partner.Email;
                    var partnerName = partner.CompanyName;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var scope = scopeFactory.CreateScope();
                            var emailService = scope.ServiceProvider.GetRequiredService<EmailService>();
                            await emailService.SendTemplateEmailAsync(
                                "PaymentRequired",
                                partnerEmail,
                                0,
                                new Dictionary<string, string>
                                {
                                    ["PlanName"] = plan.PlanName ?? "",
                                    ["BillingCycle"] = billingCycle ?? "",
                                    ["Amount"] = $"{amount:N2}",
                                    ["Name"] = partnerName ?? "",
                                    ["PaymentLink"] = paymentUrl,
                                    ["CompanyName"] = "PropTech CRM",
                                    ["Year"] = IndianTime.Now.Year.ToString()
                                },
                                "Subscription");
                        }
                        catch { }
                    });
                    emailSent = true;
                }

                return Json(new
                {
                    success = true,
                    paymentLink = paymentUrl,
                    orderId = orderId,
                    amount = amount * 100,
                    partnerEmail = partner.Email ?? partner.CompanyName,
                    planName = plan.PlanName,
                    billingCycle = billingCycle,
                    upgradeType = upgradeType,
                    emailSent = emailSent
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating payment link for partner {tenantId}");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Pay(string orderId)
        {
            ViewBag.OrderId = orderId;
            return View();
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetOrderDetails(string orderId)
        {
            try
            {
                var transaction = await _masterDb.SaasPaymentTransactions
                    .Where(t => t.RazorpayOrderId == orderId)
                    .FirstOrDefaultAsync();

                if (transaction == null)
                {
                    return Json(new { success = false, message = "Order not found" });
                }

                // Resolve planId from PlanName
                var matchedPlan = await _masterDb.SaasPlans.FirstOrDefaultAsync(p => p.PlanName == transaction.PlanName);

                return Json(new
                {
                    success = true,
                    razorpayKey = (await _masterDb.SaasPaymentConfig.FirstOrDefaultAsync(c => c.IsActive))?.RazorpayKeyId ?? "",
                    order = new
                    {
                        orderId = orderId,
                        tenantId = transaction.TenantId,
                        amount = (int)(transaction.Amount * 100), // Convert to paise
                        planId = matchedPlan?.PlanId ?? 0,
                        planName = transaction.PlanName,
                        billingCycle = transaction.BillingCycle,
                        upgradeType = transaction.TransactionType?.Replace("Upgrade_", ""),
                        partnerEmail = transaction.Tenant?.Email ?? transaction.Tenant?.CompanyName
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching order details for {OrderId}", orderId);
                return Json(new { success = false, message = "Error loading order details" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SelectPlan(int planId, string billingCycle, string upgradeType = "immediate")
        {
            var (userId, role, tenantId) = GetCurrentUserContext();
            if (!tenantId.HasValue)
                return Json(new { success = false, message = "Partner context not found" });

            var plan = await _masterDb.SaasPlans.FindAsync(planId);
            if (plan == null)
                return Json(new { success = false, message = "Plan not found" });

            // Get current active subscription
            var currentSubscription = await _masterDb.TenantSubscriptions
                .Where(s => s.TenantId == tenantId.Value && s.Status == "Active")
                .FirstOrDefaultAsync();

            var amount = billingCycle.ToLower() == "annual" ? plan.YearlyPrice : plan.MonthlyPrice;

            if (currentSubscription == null)
            {
                // Create Razorpay order for immediate subscription
                var orderId = await CreateSaasRazorpayOrder(amount, $"subscription_{tenantId.Value}");

                return Json(new
                {
                    success = true,
                    orderId = orderId,
                    amount = amount * 100, // Razorpay expects amount in paise
                    planName = plan.PlanName,
                    billingCycle = billingCycle
                });
            }
            else
            {
                if (upgradeType.ToLower() == "immediate")
                {
                    // Calculate credit amount for immediate upgrade
                    var totalDays = Math.Max(1, (currentSubscription.EndDate - currentSubscription.StartDate).Days);
                    var remainingDays = Math.Max(0, (currentSubscription.EndDate - IndianTime.Now).Days);

                    // Get actual amount - if stored as 0, use plan price
                    decimal creditBase = currentSubscription.Amount;
                    if (creditBase == 0)
                    {
                        var currentPlan = await _masterDb.SaasPlans.FindAsync(currentSubscription.PlanId);
                        if (currentPlan != null)
                        {
                            creditBase = currentSubscription.BillingCycle?.ToLower() == "annual" ? currentPlan.YearlyPrice : currentPlan.MonthlyPrice;
                        }
                    }

                    decimal perDayRate = (creditBase > 0 && currentSubscription.BillingCycle != "Trial") ? creditBase / totalDays : 0;

                    var creditAmount = Math.Round(perDayRate * remainingDays, 2);

                    var adjustedAmount = Math.Max(0, amount - creditAmount);

                    var earnings = await _masterDb.ReferralEarnings
                          .Where(r => r.TenantId == tenantId && !r.IsUsed)
                          .ToListAsync();



                    var balance = earnings.Sum(e => e.Amount);
                    var finalPayable = Math.Max(0, adjustedAmount - balance);

                    try
                    {

                        // ? CASE 1: No payment required
                        if (finalPayable == 0)
                        {
                            return Json(new
                            {
                                success = true,
                                isFreeUpgrade = true,
                                amount = 0,
                                orderId = (string)null,
                                fullAmount = amount,
                                creditAmount = creditAmount,
                                rewardUsed = Math.Min(balance, adjustedAmount),
                                amountToPay = 0,
                                planName = plan.PlanName,
                                billingCycle = billingCycle,
                                upgradeType = "immediate"
                            });
                        }

                        // Create Razorpay order for the adjusted amount (after credit)
                        var orderId = await CreateSaasRazorpayOrder(adjustedAmount, $"upgrade_immediate_{tenantId}_{planId}");

                        return Json(new
                        {
                            success = true,
                            orderId = orderId,
                            amount = finalPayable * 100, // Amount to pay in paise
                            fullAmount = amount,
                            rewardUsed = Math.Min(balance, adjustedAmount),
                            creditAmount = creditAmount,
                            amountToPay = finalPayable,
                            planName = plan.PlanName,
                            billingCycle = billingCycle,
                            upgradeType = "immediate"
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error creating Razorpay order for immediate upgrade");
                        return Json(new { success = false, message = "Failed to create payment order" });
                    }
                }
                else // scheduled upgrade
                {
                    try
                    {
                        // 1?? Get scheduled credit (IMPORTANT)
                        var existingScheduled = await _masterDb.TenantSubscriptions
                            .Where(s => s.TenantId == tenantId && s.Status == "Scheduled")
                            .ToListAsync();

                        decimal scheduledCredit = existingScheduled.Sum(x => x.Amount);

                        // 2?? Get wallet balance (referral)
                        var earnings = await _masterDb.ReferralEarnings
                            .Where(r => r.TenantId == tenantId && !r.IsUsed)
                            .ToListAsync();

                        decimal walletBalance = earnings.Sum(e => e.Amount);

                        // 3?? Calculate final payable
                        decimal finalPayable = Math.Max(0, amount - scheduledCredit);
                        decimal walletUsed = Math.Min(walletBalance, finalPayable);
                        finalPayable -= walletUsed;

                        // 4?? CASE: FREE UPGRADE
                        if (finalPayable == 0)
                        {
                            return Json(new
                            {
                                success = true,
                                isFreeUpgrade = true,
                                orderId = (string)null,
                                amount = 0,
                                fullAmount = amount,
                                scheduledCredit,
                                walletUsed,
                                amountToPay = 0,
                                planName = plan.PlanName,
                                billingCycle = billingCycle,
                                upgradeType = "scheduled",
                                startDate = currentSubscription?.EndDate.AddDays(1).ToString("yyyy-MM-dd")
                            });
                        }

                        // 5?? CREATE ORDER with FINAL PAYABLE (IMPORTANT FIX)
                        var orderId = await CreateSaasRazorpayOrder(finalPayable, $"upgrade_scheduled_{tenantId}_{planId}");

                        return Json(new
                        {
                            success = true,
                            orderId = orderId,
                            amount = finalPayable * 100, // Razorpay paise
                            fullAmount = amount,
                            scheduledCredit,
                            walletUsed,
                            amountToPay = finalPayable,
                            planName = plan.PlanName,
                            billingCycle = billingCycle,
                            upgradeType = "scheduled",
                            startDate = currentSubscription?.EndDate.AddDays(1).ToString("yyyy-MM-dd")
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Scheduled upgrade order creation failed");
                        return Json(new { success = false, message = "Failed to Create Payment order" });
                    }
                }
            }
        }

        public class DeductRequest
        {
            public int TenantId { get; set; }
        }

        [HttpPost]
        private async Task DeductReferralBalance(int tenantId, decimal amountToDeduct)
        {
            var earnings = await _masterDb.ReferralEarnings
                .Where(r => r.TenantId == tenantId && !r.IsUsed)
                .OrderBy(r => r.CreatedOn)
                .ToListAsync();

            decimal remaining = amountToDeduct;

            foreach (var item in earnings)
            {
                if (remaining <= 0)
                    break;

                if (item.Amount <= remaining)
                {
                    remaining -= item.Amount;
                    item.Amount = 0;
                    item.IsUsed = true;
                }
                else
                {
                    item.Amount -= remaining;
                    remaining = 0;
                }

                // Optional but good
                //item.UpdatedOn = IndianTime.Now;
            }

            // ? DO NOT call SaveChanges here
        }

        //public async Task<IActionResult> DeductReferralBalance(int tenantId)
        //{
        //    try
        //    {
        //        //var tenantId = tenantId;

        //        var earnings = await _masterDb.ReferralEarnings
        //            .Where(r => r.TenantId == tenantId && !r.IsUsed)
        //            .ToListAsync();

        //        if (!earnings.Any())
        //        {
        //            return Json(new { success = true, message = "No balance to deduct" });
        //        }

        //        // ?? Total balance
        //        var balance = earnings.Sum(e => e.Amount);

        //        // ?? Mark all as used
        //        foreach (var item in earnings)
        //        {
        //            item.IsUsed = true;
        //            item.Amount = 0; // optional (if you want to zero it)
        //            //item.UsedDate = DateTime.UtcNow; // optional column
        //        }

        //        await _masterDb.SaveChangesAsync();

        //        return Json(new
        //        {
        //            success = true,
        //            deductedAmount = balance
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        return Json(new
        //        {
        //            success = false,
        //            message = ex.Message
        //        });
        //    }
        //}

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> ConfirmPayment(string razorpayPaymentId, string razorpayOrderId, string razorpaySignature, int planId, string billingCycle, string? upgradeType = "immediate", string paymentStatus = null, int? tenantId = null)
        {

            var (userId, role, contextTenantId) = GetCurrentUserContext();

            var resolvedTenantId = tenantId ?? contextTenantId;

            if (!resolvedTenantId.HasValue || resolvedTenantId == 0)
            {
                // Try to resolve from the order
                var orderTransaction = await _masterDb.SaasPaymentTransactions
                    .FirstOrDefaultAsync(t => t.RazorpayOrderId == razorpayOrderId);
                if (orderTransaction != null)
                    resolvedTenantId = orderTransaction.TenantId;
            }

            if (!resolvedTenantId.HasValue || resolvedTenantId == 0)
                return Json(new { success = false, message = "Partner context not found", errorCode = "NO_PARTNER" });

            // Check if payment failed at Razorpay level (before verification)
            if (!string.IsNullOrEmpty(paymentStatus) && paymentStatus.ToLower() == "failed")
            {
                _logger.LogWarning($"Payment failed at Razorpay level for order {razorpayOrderId}");

                var failedTransaction = new SaasPaymentTransactionModel
                {
                    TenantId = resolvedTenantId.Value,
                    TransactionReference = razorpayOrderId ?? "unknown",
                    RazorpayOrderId = razorpayOrderId,
                    RazorpayPaymentId = razorpayPaymentId,
                    Status = "Failed",
                    TransactionType = "Payment Failure",
                    PaymentMethod = "Razorpay",
                    TransactionDate = IndianTime.Now,
                    CompletedDate = IndianTime.Now,
                    Description = "Payment failed at gateway",
                    CreatedOn = IndianTime.Now
                };

                _masterDb.SaasPaymentTransactions.Add(failedTransaction);
                await _masterDb.SaveChangesAsync();

                return Json(new
                {
                    success = false,
                    message = "Payment was declined. Please try again or use a different payment method.",
                    errorCode = "PAYMENT_FAILED",
                    canRetry = true
                });
            }
            using var transaction = await _masterDb.Database.BeginTransactionAsync();
            try
            {
                _logger.LogInformation($"Starting payment confirmation for partner {resolvedTenantId}, plan {planId}, upgradeType={upgradeType}");

                // Check if this order was already processed (by webhook race condition)
                var alreadyProcessed = await _masterDb.SaasPaymentTransactions
                    .AnyAsync(t => t.RazorpayOrderId == razorpayOrderId && t.Status == "Success");
                if (alreadyProcessed)
                {
                    return Json(new { success = true, message = "Payment successful! Your plan is now active." });
                }

                // Also update the pending transaction to Success
                var pendingTransaction = await _masterDb.SaasPaymentTransactions
                    .FirstOrDefaultAsync(t => t.RazorpayOrderId == razorpayOrderId && t.Status == "Pending");
                if (pendingTransaction != null)
                {
                    pendingTransaction.Status = "Success";
                    pendingTransaction.RazorpayPaymentId = razorpayPaymentId;
                    pendingTransaction.RazorpaySignature = razorpaySignature;
                    pendingTransaction.CompletedDate = IndianTime.Now;
                }



                var saasConfigVerify = await _masterDb.SaasPaymentConfig.FirstOrDefaultAsync(c => c.IsActive);
                var expectedSignature = "";
                if (saasConfigVerify != null)
                {
                    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(saasConfigVerify.RazorpayKeySecret));
                    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(razorpayOrderId + "|" + razorpayPaymentId));
                    expectedSignature = BitConverter.ToString(hash).Replace("-", "").ToLower();
                }

                if (expectedSignature != razorpaySignature)
                {
                    _logger.LogWarning($"Payment verification failed for order {razorpayOrderId}");

                    var failedTransaction = new SaasPaymentTransactionModel
                    {
                        TenantId = resolvedTenantId.Value,
                        TransactionReference = razorpayOrderId,
                        RazorpayOrderId = razorpayOrderId,
                        RazorpayPaymentId = razorpayPaymentId,
                        Status = "Failed",
                        TransactionType = "Verification failed",
                        PaymentMethod = "Razorpay",
                        TransactionDate = IndianTime.Now,
                        CompletedDate = IndianTime.Now,
                        Description = "Payment signature verification failed",
                        CreatedOn = IndianTime.Now
                    };

                    _masterDb.SaasPaymentTransactions.Add(failedTransaction);
                    await _masterDb.SaveChangesAsync();

                    return Json(new
                    {
                        success = false,
                        message = "Payment verification failed. If amount was deducted, it will be refunded within 5-7 business days.",
                        errorCode = "VERIFICATION_FAILED",
                        canRetry = false
                    });
                }

                string? cardType = null;
                string? cardNetwork = null;
                string? cardLast4 = null;
                string? bankName = null;

            try
            {
                    var (success, paymentDetails) = await _razorpayService.FetchPaymentAsync(razorpayPaymentId);
                    if (success && paymentDetails.HasValue)
                    {
                        var payment = paymentDetails.Value;

                        if (payment.TryGetProperty("method", out var method) && method.GetString() == "card")
                        {
                            if (payment.TryGetProperty("card", out var card))
                            {
                                cardType = card.TryGetProperty("type", out var type) ? type.GetString() : null;
                                cardNetwork = card.TryGetProperty("network", out var network) ? network.GetString() : null;
                                cardLast4 = card.TryGetProperty("last4", out var last4) ? last4.GetString() : null;

                                if (card.TryGetProperty("issuer", out var issuer))
                                {
                                    bankName = issuer.GetString();
                                }
                                else if (payment.TryGetProperty("bank", out var bank))
                                {
                                    bankName = bank.GetString();
                                }
                            }
                        }


                        _logger.LogInformation($"Card details fetched: Type={cardType}, Network={cardNetwork}, Last4={cardLast4}, Bank={bankName}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to fetch card details from Razorpay, continuing with payment processing");
                }

                var plan = await _masterDb.SaasPlans.FindAsync(planId);
                if (plan == null)
                {
                    _logger.LogWarning($"Plan {planId} not found");
    
                    return Json(new { success = false, message = "Invalid plan selected", errorCode = "INVALID_PLAN" });
                }

                var amount = billingCycle.ToLower() == "annual" ? plan.YearlyPrice : plan.MonthlyPrice;

                var currentSubscription = await _masterDb.TenantSubscriptions
                    .Where(s => s.TenantId == resolvedTenantId.Value && s.Status == "Active")
                    .FirstOrDefaultAsync();

                if (currentSubscription != null)
                {
                    if (upgradeType?.ToLower() == "immediate")
                    {

                        // ?? STEP 1: Recalculate credit
                        //var totalDays = Math.Max(1, (currentSubscription.EndDate - currentSubscription.StartDate).Days);
                        //var remainingDays = Math.Max(0, (currentSubscription.EndDate - IndianTime.Now).Days);

                        var totalDays = Math.Max(1, (int)Math.Ceiling((currentSubscription.EndDate - currentSubscription.StartDate).TotalDays));
                        var remainingDays = Math.Max(0, (int)Math.Ceiling((currentSubscription.EndDate - IndianTime.Now).TotalDays));

                        decimal creditBase = currentSubscription.Amount;

                        if (creditBase == 0)
                        {
                            var currentPlan = await _masterDb.SaasPlans.FindAsync(currentSubscription.PlanId);
                            if (currentPlan != null)
                            {
                                creditBase = currentSubscription.BillingCycle?.ToLower() == "annual"
                                    ? currentPlan.YearlyPrice
                                    : currentPlan.MonthlyPrice;
                            }
                        }

                        decimal perDayRate = (creditBase > 0 && currentSubscription.BillingCycle != "Trial")
                            ? creditBase / totalDays
                            : 0;

                        var creditAmount = Math.Round(perDayRate * remainingDays, 2);

                        // ?? STEP 2: Adjust new plan price after credit
                        var adjustedAmount = Math.Max(0, amount - creditAmount);

                        // ?? STEP 3: Get reward balance
                        var earnings = _masterDb.ReferralEarnings
                            .Where(r => r.TenantId == resolvedTenantId.Value && !r.IsUsed)
                            .ToList();

                        var balance = earnings.Sum(e => e.Amount);

                        // ?? STEP 4: Calculate how much reward to use
                        var rewardToUse = Math.Min(balance, adjustedAmount);

                        // ?? STEP 5: Expire current subscription

                        currentSubscription.Status = "Expired";
                        currentSubscription.EndDate = IndianTime.Now;
                        currentSubscription.CancellationReason = $"Upgraded to {plan.PlanName} immediately";
                        currentSubscription.CancelledOn = IndianTime.Now;
                        currentSubscription.UpdatedOn = IndianTime.Now;


                        var immediateSubscription = new TenantSubscriptionModel
                        {
                            TenantId = resolvedTenantId.Value,
                            PlanId = planId,
                            BillingCycle = billingCycle,
                            Amount = amount,
                            StartDate = IndianTime.Now,
                            EndDate = billingCycle.ToLower() == "annual" ? IndianTime.Now.AddYears(1) : IndianTime.Now.AddMonths(1),
                            Status = "Active",
                            PaymentMethod = "Razorpay",
                            PaymentTransactionId = razorpayPaymentId,
                            LastPaymentDate = IndianTime.Now,
                            NextPaymentDate = billingCycle.ToLower() == "annual" ? IndianTime.Now.AddYears(1) : IndianTime.Now.AddMonths(1),
                            AutoRenew = false,
                            CreatedOn = IndianTime.Now,
                            UpdatedOn = IndianTime.Now
                        };

                        _masterDb.TenantSubscriptions.Add(immediateSubscription);
                        await _masterDb.SaveChangesAsync();

                        var immediateTransaction = new SaasPaymentTransactionModel
                        {
                            TenantId = resolvedTenantId.Value,
                            SubscriptionId = immediateSubscription.SubscriptionId,
                            TransactionReference = razorpayOrderId,
                            RazorpayPaymentId = razorpayPaymentId,
                            RazorpayOrderId = razorpayOrderId,
                            RazorpaySignature = razorpaySignature,
                            Amount = amount,
                            Currency = "INR",
                            Status = "Success",
                            TransactionType = "Immediate Upgrade",
                            PaymentMethod = "Razorpay",
                            TransactionDate = IndianTime.Now,
                            CompletedDate = IndianTime.Now,
                            PlanName = plan.PlanName,
                            BillingCycle = billingCycle,
                            // ? Important tracking
                            DiscountAmount = creditAmount + rewardToUse,
                            NetAmount = amount - creditAmount - rewardToUse,

                            Description = $"Immediate upgrade to {plan.PlanName} plan",
                            CardType = cardType,
                            CardNetwork = cardNetwork,
                            CardLast4 = cardLast4,
                            BankName = bankName,
                            CreatedOn = IndianTime.Now
                        };

                        _masterDb.SaasPaymentTransactions.Add(immediateTransaction);
                        await _masterDb.SaveChangesAsync();

                        immediateSubscription.PaymentTransactionId = immediateTransaction.TransactionId.ToString();
                        await _masterDb.SaveChangesAsync();


                        // ?? STEP 8: Deduct rewards (CRITICAL - before commit)
                        if (rewardToUse > 0)
                        {
                            await DeductReferralBalance(resolvedTenantId.Value, rewardToUse);

                            // ? Save once here (atomic)
                            await _masterDb.SaveChangesAsync();
                        }

                        // ?? STEP 9: Commit transaction
                        await transaction.CommitAsync();

                        return Json(new { success = true, message = $"Payment successful! Your {plan.PlanName} plan is now active immediately." });
                    }
                    else
                    {
                        //_logger.LogInformation($"Processing scheduled upgrade, current expires: {currentSubscription.EndDate}");

                        //// Calculate credit from existing scheduled subscriptions
                        //decimal creditAmount = 0;
                        //string creditDescription = "";

                        //// Cancel existing scheduled subscriptions instead of deleting them
                        //// (they have payment transactions referencing them via FK)
                        //var existingScheduled = await _masterDb.TenantSubscriptions
                        //    .Where(s => s.TenantId == tenantId && s.Status == "Scheduled")
                        //    .ToListAsync();

                        //if (existingScheduled.Any())
                        //{
                        //    foreach (var scheduled in existingScheduled)
                        //    {
                        //        creditAmount += scheduled.Amount;
                        //        scheduled.Status = "Cancelled";
                        //        scheduled.CancelledOn = IndianTime.Now;
                        //        scheduled.CancellationReason = "Replaced by new scheduled plan - credit applied";
                        //        scheduled.UpdatedOn = IndianTime.Now;
                        //    }


                        //    // Validate that new plan is not cheaper (downgrade)
                        //    if (amount <= creditAmount)
                        //    {
                        //        await transaction.RollbackAsync();
                        //        _logger.LogWarning($"Attempted downgrade from ?{creditAmount} to ?{amount}");

                        //        return Json(new
                        //        {
                        //            success = false,
                        //            isDowngrade = true,
                        //            existingPlanName = existingScheduled.FirstOrDefault()?.Plan?.PlanName,
                        //            existingAmount = creditAmount,
                        //            newPlanName = plan.PlanName,
                        //            newAmount = amount,
                        //            refundAmount = creditAmount - amount,
                        //            message = $"Cannot process downgrade. You already paid ?{creditAmount:N0} for {existingScheduled.FirstOrDefault()?.Plan?.PlanName}. The {plan.PlanName} costs ?{amount:N0}. Please contact support."
                        //        });
                        //    }
                        //    await _masterDb.SaveChangesAsync();
                        //    _logger.LogInformation($"Cancelled {existingScheduled.Count} existing scheduled subscriptions. Total credit: ?{creditAmount}");

                        //    creditDescription = $" (Credit of ?{creditAmount:N0} applied from {existingScheduled.FirstOrDefault()?.Plan?.PlanName})";
                        //}
                        //// Create scheduled subscription first
                        //var startDate = currentSubscription.EndDate.AddDays(1);
                        //_logger.LogInformation($"Creating scheduled subscription starting: {startDate}");

                        //var sPlan = await _masterDb.SaasPlans.FindAsync(planId);
                        //var sAmt = billingCycle.ToLower() == "annual" ? sPlan.YearlyPrice : sPlan.MonthlyPrice;
                        //var sEnd = billingCycle.ToLower() == "annual" ? startDate.AddYears(1) : startDate.AddMonths(1); // (Inferred end-date logic cut off in screen)
                        //var scheduledSubscription = new TenantSubscriptionModel
                        //{
                        //    TenantId = tenantId.Value,
                        //    PlanId = planId,
                        //    BillingCycle = billingCycle,
                        //    Amount = sAmt,
                        //    StartDate = startDate,
                        //    EndDate = sEnd,
                        //    Status = "Scheduled",
                        //    CreatedOn = DateTime.UtcNow
                        //};
                        //_masterDb.TenantSubscriptions.Add(scheduledSubscription);
                        //await _masterDb.SaveChangesAsync();
                        //_logger.LogInformation($"Scheduled subscription created with ID: {scheduledSubscription.SubscriptionId}");

                        //// Create payment transaction record
                        //var paymentTransaction = new SaasPaymentTransactionModel
                        //{
                        //    TenantId = tenantId.Value,
                        //    SubscriptionId = scheduledSubscription.SubscriptionId,
                        //    TransactionReference = razorpayOrderId,
                        //    RazorpayPaymentId = razorpayPaymentId,
                        //    RazorpayOrderId = razorpayOrderId,
                        //    RazorpaySignature = razorpaySignature,
                        //    Amount = amount, // Full plan amount (before credit)
                        //    Currency = "INR",
                        //    TransactionType = "Scheduled Payment",
                        //    Status = "Success",
                        //    PaymentMethod = "Razorpay",
                        //    TransactionDate = IndianTime.Now,
                        //    CompletedDate = IndianTime.Now,
                        //    PlanName = plan.PlanName,
                        //    BillingCycle = billingCycle,
                        //    DiscountAmount = creditAmount, // Credit stored as discount
                        //    NetAmount = amount - creditAmount, // Actual amount paid
                        //    Description = $"Scheduled subscription payment for {plan.PlanName} plan{creditDescription}",
                        //    CardType = cardType,
                        //    CardNetwork = cardNetwork,
                        //    CardLast4 = cardLast4,
                        //    BankName = bankName,
                        //    CreatedOn = IndianTime.Now
                        //};

                        //_masterDb.SaasPaymentTransactions.Add(paymentTransaction);
                        //await _masterDb.SaveChangesAsync();
                        //_logger.LogInformation($"Transaction created with ID: {paymentTransaction.TransactionId}");

                        //// Update scheduled subscription with payment transaction reference
                        //// Re-fetch the subscription to ensure it's tracked by the current context
                        //var subscriptionToUpdate = await _masterDb.TenantSubscriptions
                        //    .FirstOrDefaultAsync(s => s.SubscriptionId == scheduledSubscription.SubscriptionId);

                        //if (subscriptionToUpdate != null)
                        //{
                        //    subscriptionToUpdate.PaymentTransactionId = paymentTransaction.TransactionId.ToString();
                        //    subscriptionToUpdate.LastPaymentDate = IndianTime.Now;
                        //    await _masterDb.SaveChangesAsync();
                        //    _logger.LogInformation($"Updated scheduled subscription with payment reference");
                        //}

                        //// Commit the database transaction
                        //await transaction.CommitAsync();

                        //var netAmountPaid = amount - creditAmount;
                        //var message = existingScheduled.Any()
                        //    ? creditAmount > 0
                        //        ? $"Payment successful! ?{netAmountPaid:N0} paid (?{creditAmount:N0} credit applied from previous plan). Your {plan.PlanName} plan will activate on {startDate:dd/MM/yyyy}."
                        //        : $"Payment successful! ?{netAmountPaid:N0} received. Previous scheduled plan replaced. Your new {plan.PlanName} plan will activate on {startDate:dd/MM/yyyy}."
                        //    : $"Payment successful! ?{netAmountPaid:N0} received. Your new {plan.PlanName} plan will activate on {startDate:dd/MM/yyyy}.";

                        //return Json(new { success = true, message });
                        _logger.LogInformation($"Processing scheduled upgrade, current expires: {currentSubscription?.EndDate}");

                        var tid = resolvedTenantId ?? tenantId.Value;

                        decimal creditAmount = 0;

                        // 1?? Cancel existing scheduled plans
                        var existingScheduled = await _masterDb.TenantSubscriptions
                            .Where(s => s.TenantId == tid && s.Status == "Scheduled")
                            .ToListAsync();

                        if (existingScheduled.Any())
                        {
                            foreach (var scheduled in existingScheduled)
                            {
                                creditAmount += scheduled.Amount;

                                scheduled.Status = "Cancelled";
                                scheduled.CancelledOn = IndianTime.Now;
                                scheduled.CancellationReason = "Replaced by new scheduled plan - credit applied";
                                scheduled.UpdatedOn = IndianTime.Now;
                            }

                            await _masterDb.SaveChangesAsync();
                        }

                        // 2?? Downgrade check
                        if (amount <= creditAmount)
                        {
            

                            return Json(new
                            {
                                success = false,
                                isDowngrade = true,
                                existingAmount = creditAmount,
                                newAmount = amount,
                                refundAmount = creditAmount - amount,
                                message = $"Cannot downgrade. You already have ?{creditAmount:N0} credit."
                            });
                        }

                        // 3?? Base payable
                        decimal finalPayable = Math.Max(0, amount - creditAmount);

                        // 4?? WALLET APPLY
                        var earnings = await _masterDb.ReferralEarnings
                            .Where(r => r.TenantId == tid && !r.IsUsed)
                            .ToListAsync();

                        decimal walletBalance = earnings.Sum(e => e.Amount);

                        decimal walletUsed = Math.Min(walletBalance, finalPayable);

                        // 5?? Create scheduled subscription
                        var startDate = currentSubscription?.EndDate.AddDays(1) ?? IndianTime.Now;

                        var sPlan = await _masterDb.SaasPlans.FindAsync(planId);
                        var sAmt = billingCycle.ToLower() == "annual"
                            ? sPlan.YearlyPrice
                            : sPlan.MonthlyPrice;

                        var sEnd = billingCycle.ToLower() == "annual"
                            ? startDate.AddYears(1)
                            : startDate.AddMonths(1);

                        var scheduledSubscription = new TenantSubscriptionModel
                        {
                            TenantId = tid,
                            PlanId = planId,
                            BillingCycle = billingCycle,
                            Amount = sAmt,
                            StartDate = startDate,
                            EndDate = sEnd,
                            Status = "Scheduled",
                            CreatedOn = IndianTime.Now,
                            UpdatedOn = IndianTime.Now
                        };

                        _masterDb.TenantSubscriptions.Add(scheduledSubscription);
                        await _masterDb.SaveChangesAsync();

                        // 6?? DEDUCT WALLET ONLY AFTER SAVE (IMPORTANT)
                        if (walletUsed > 0)
                        {
                            await DeductReferralBalance(tid, walletUsed);
                        }

                        // 7?? Transaction
                        var paymentTransaction = new SaasPaymentTransactionModel
                        {
                            TenantId = tid,
                            SubscriptionId = scheduledSubscription.SubscriptionId,
                            TransactionReference = razorpayOrderId,
                            RazorpayPaymentId = razorpayPaymentId,
                            RazorpayOrderId = razorpayOrderId,
                            RazorpaySignature = razorpaySignature,

                            Amount = amount,
                            DiscountAmount = creditAmount + walletUsed,
                            NetAmount = finalPayable - walletUsed,

                            Currency = "INR",
                            Status = "Success",
                            TransactionType = "Scheduled Payment",
                            PaymentMethod = (finalPayable - walletUsed) == 0 ? "Wallet" : "Mixed",

                            TransactionDate = IndianTime.Now,
                            CompletedDate = IndianTime.Now,

                            PlanName = plan.PlanName,
                            BillingCycle = billingCycle,

                            Description = $"Credit ?{creditAmount}, Wallet ?{walletUsed}",

                            CreatedOn = IndianTime.Now
                        };

                        _masterDb.SaasPaymentTransactions.Add(paymentTransaction);
                        await _masterDb.SaveChangesAsync();

                        // 8?? LINK
                        scheduledSubscription.PaymentTransactionId = paymentTransaction.TransactionId.ToString();
                        scheduledSubscription.LastPaymentDate = IndianTime.Now;

                        await _masterDb.SaveChangesAsync();

                        // 9?? COMMIT
                        await transaction.CommitAsync();

                        return Json(new
                        {
                            success = true,
                            message = $"Scheduled plan confirmed! ?{(finalPayable - walletUsed):N0} paid (Credit ?{creditAmount:N0}, Wallet ?{walletUsed:N0}). Starts {startDate:dd/MM/yyyy}"
                        });
                    }
                }
                else
                {
                    _logger.LogInformation("Creating immediate subscription");

                    // Create immediate subscription first
                    var iPlan = await _masterDb.SaasPlans.FindAsync(planId);
                    var iAmt = billingCycle.ToLower() == "annual" ? iPlan.YearlyPrice : iPlan.MonthlyPrice;
                    var iEnd = billingCycle.ToLower() == "annual" ? IndianTime.Now.AddYears(1) : IndianTime.Now.AddMonths(1); // (Inferred based on typical SaaS billing logic)

                    var subscription = new TenantSubscriptionModel
                    {
                        TenantId = tenantId.Value,
                        PlanId = planId,
                        BillingCycle = billingCycle,
                        Amount = iAmt,
                        StartDate = DateTime.UtcNow,
                        EndDate = iEnd,
                        Status = "Active",
                        CreatedOn = DateTime.UtcNow
                    };
                    _masterDb.TenantSubscriptions.Add(subscription);
                    await _masterDb.SaveChangesAsync();
                    _logger.LogInformation($"Immediate subscription created with ID: {subscription.SubscriptionId}");

                    // Create payment transaction record

                    var paymentTransaction = new SaasPaymentTransactionModel
                    {
                        TenantId = tenantId.Value,
                        SubscriptionId = subscription.SubscriptionId,
                        TransactionReference = razorpayOrderId,
                        RazorpayPaymentId = razorpayPaymentId,
                        RazorpayOrderId = razorpayOrderId,
                        RazorpaySignature = razorpaySignature,
                        Amount = amount,
                        Currency = "INR",
                        TransactionType = "Payment",
                        Status = "Success",
                        PaymentMethod = "Razorpay",
                        TransactionDate = IndianTime.Now,
                        CompletedDate = IndianTime.Now,
                        PlanName = plan.PlanName,
                        BillingCycle = billingCycle,
                        NetAmount = amount,
                        Description = $"Subscription payment for {plan.PlanName} plan",
                        CardType = cardType,
                        CardNetwork = cardNetwork,
                        CardLast4 = cardLast4,
                        BankName = bankName,
                        CreatedOn = IndianTime.Now
                    };

                    _masterDb.SaasPaymentTransactions.Add(paymentTransaction);
                    await _masterDb.SaveChangesAsync();
                    _logger.LogInformation($"Transaction created with ID: {paymentTransaction.TransactionId}");

                    // Update subscription with payment transaction reference
                    // Re-fetch the subscription to ensure it's tracked by the current context
                    var subscriptionToUpdate = await _masterDb.TenantSubscriptions
                        .FirstOrDefaultAsync(s => s.SubscriptionId == subscription.SubscriptionId);

                    if (subscriptionToUpdate != null)
                    {
                        subscriptionToUpdate.PaymentTransactionId = paymentTransaction.TransactionId.ToString();
                        subscriptionToUpdate.LastPaymentDate = IndianTime.Now;
                        await _masterDb.SaveChangesAsync();
                        _logger.LogInformation($"Updated subscription with payment reference");
                    }

                    // Commit the database transaction
                    await transaction.CommitAsync();

                    return Json(new { success = true, message = $"Payment successful! ?{amount:N0} received. Your {plan.PlanName} subscription is now active." });
                }
            }
            catch (Exception ex)
            {
                // Rollback the transaction on error


                _logger.LogError(ex, $"Error processing payment confirmation for partner {tenantId}, plan {planId}");

                // Log inner exception details for better debugging
                var innerMessage = ex.InnerException?.Message ?? "No inner exception";
                var innerStackTrace = ex.InnerException?.StackTrace ?? "No stack trace";
                _logger.LogError($"Inner Exception: {innerMessage}");
                _logger.LogError($"Inner Stack Trace: {innerStackTrace}");

                // Provide detailed error message for debugging
                var errorDetails = ex.InnerException != null
                    ? $"{ex.Message} - Inner: {ex.InnerException.Message}"
                    : ex.Message;

                return Json(new { success = false, message = $"Payment processing failed: {errorDetails}" });
            }
        }


        //[HttpGet]
        //public async Task<IActionResult> ConfirmPaymentAdmin(string razorpayPaymentId, int tenantId, string razorpayOrderId, string razorpaySignature, int planId, string billingCycle, string? upgradeType = "immediate", string paymentStatus = null)
        //{

        //    //var (userId, role, tenantId) = GetCurrentUserContext();


        //    if (tenantId < 0)
        //    {
        //        return Json(new { success = false, message = "Partner context not found", errorCode = "NO_PARTNER" });
        //    }

        //    if (string.IsNullOrEmpty(razorpayPaymentId) && paymentStatus.ToLower() == "failed")
        //    {
        //        _logger.LogWarning($"Payment failed at Razorpay level for order {razorpayOrderId}");

        //        var failedTransaction = new SaasPaymentTransactionModel
        //        {
        //            TenantId = tenantId,
        //            TransactionReference = razorpayOrderId ?? "unknown",
        //            RazorpayOrderId = razorpayOrderId,
        //            RazorpayPaymentId = razorpayPaymentId,
        //            Status = "Failed",
        //            TransactionType = "Payment Failure",
        //            PaymentMethod = "Razorpay",
        //            TransactionDate = IndianTime.Now,
        //            CompletedDate = IndianTime.Now,
        //            Description = "Payment failed at gateway",
        //            CreatedOn = IndianTime.Now
        //        };

        //        _masterDb.SaasPaymentTransactions.Add(failedTransaction);
        //        await _masterDb.SaveChangesAsync();

        //        return Json(new
        //        {
        //            success = false,
        //            message = "Payment was declined. Please try again or use a different payment method.",
        //            errorCode = "PAYMENT_FAILED",
        //            canRetry = true
        //        });
        //    }
        //    using var transaction = await _masterDb.Database.BeginTransactionAsync();

        //    try
        //    {



        //        var saasConfigVerify = await _masterDb.SaasPaymentConfig.FirstOrDefaultAsync(c => c.IsActive);
        //        var expectedSignature = "";
        //        if (saasConfigVerify != null)
        //        {
        //            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(saasConfigVerify.RazorpayKeySecret));
        //            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(razorpayOrderId + "|" + razorpayPaymentId));
        //            expectedSignature = BitConverter.ToString(hash).Replace("-", "").ToLower();
        //        }

        //        if (expectedSignature != razorpaySignature)
        //        {
        //            _logger.LogWarning($"Payment verification failed for order {razorpayOrderId}");

        //            var failedTransaction = new SaasPaymentTransactionModel
        //            {
        //                TenantId = tenantId,
        //                TransactionReference = razorpayOrderId,
        //                RazorpayOrderId = razorpayOrderId,
        //                RazorpayPaymentId = razorpayPaymentId,
        //                Status = "Failed",
        //                TransactionType = "Verification failed",
        //                PaymentMethod = "Razorpay",
        //                TransactionDate = IndianTime.Now,
        //                CompletedDate = IndianTime.Now,
        //                Description = "Payment signature verification failed",
        //                CreatedOn = IndianTime.Now
        //            };

        //            _masterDb.SaasPaymentTransactions.Add(failedTransaction);
        //            await _masterDb.SaveChangesAsync();

        //            return Json(new
        //            {
        //                success = false,
        //                message = "Payment verification failed. If amount was deducted, it will be refunded within 5-7 business days.",
        //                errorCode = "VERIFICATION_FAILED",
        //                canRetry = false
        //            });
        //        }

        //        string? cardType = null;
        //        string? cardNetwork = null;
        //        string? cardLast4 = null;
        //        string? bankName = null;

        //        try
        //        {
        //            var (success, paymentDetails) = await _razorpayService.FetchPaymentAsync(razorpayPaymentId);
        //            if (success && paymentDetails.HasValue)
        //            {
        //                var payment = paymentDetails.Value;

        //                if (payment.TryGetProperty("method", out var method) && method.GetString() == "card")
        //                {
        //                    if (payment.TryGetProperty("card", out var card))
        //                    {
        //                        cardType = card.TryGetProperty("type", out var type) ? type.GetString() : null;
        //                        cardNetwork = card.TryGetProperty("network", out var network) ? network.GetString() : null;
        //                        cardLast4 = card.TryGetProperty("last4", out var last4) ? last4.GetString() : null;

        //                        if (card.TryGetProperty("issuer", out var issuer))
        //                        {
        //                            bankName = issuer.GetString();
        //                        }
        //                        else if (payment.TryGetProperty("bank", out var bank))
        //                        {
        //                            bankName = bank.GetString();
        //                        }
        //                    }
        //                }


        //                _logger.LogInformation($"Card details fetched: Type={cardType}, Network={cardNetwork}, Last4={cardLast4}, Bank={bankName}");
        //            }
        //        }
        //        catch (Exception ex)
        //        {
        //            _logger.LogWarning(ex, $"Failed to fetch card details from Razorpay, continuing with payment processing");
        //        }

        //        var plan = await _masterDb.SaasPlans.FindAsync(planId);
        //        if (plan == null)
        //        {
        //            _logger.LogWarning($"Plan {planId} not found");
        //            return Json(new { success = false, message = "Invalid plan selected", errorCode = "INVALID_PLAN" });
        //        }

        //        var amount = billingCycle.ToLower() == "annual" ? plan.YearlyPrice : plan.MonthlyPrice;

        //        var currentSubscription = await _masterDb.TenantSubscriptions
        //            .Where(s => s.TenantId == tenantId && s.Status == "Active")
        //            .FirstOrDefaultAsync();

        //        if (currentSubscription != null)
        //        {
        //            if (upgradeType?.ToLower() != "immediate")
        //            {


        //                currentSubscription.Status = "Expired";
        //                currentSubscription.EndDate = IndianTime.Now;
        //                currentSubscription.CancellationReason = $"Upgraded to {plan.PlanName} immediately";
        //                currentSubscription.CancelledOn = IndianTime.Now;

        //                var immediateSubscription = new TenantSubscriptionModel
        //                {
        //                    TenantId = tenantId,
        //                    PlanId = planId,
        //                    BillingCycle = billingCycle,
        //                    Amount = amount,
        //                    StartDate = IndianTime.Now,
        //                    EndDate = billingCycle.ToLower() == "annual" ? IndianTime.Now.AddYears(1) : IndianTime.Now.AddMonths(1),
        //                    Status = "Active",
        //                    PaymentMethod = "Razorpay",
        //                    PaymentTransactionId = razorpayPaymentId,
        //                    LastPaymentDate = IndianTime.Now,
        //                    NextPaymentDate = billingCycle.ToLower() == "annual" ? IndianTime.Now.AddYears(1) : IndianTime.Now.AddMonths(1),
        //                    AutoRenew = false,
        //                    CreatedOn = IndianTime.Now,
        //                    UpdatedOn = IndianTime.Now
        //                };

        //                _masterDb.TenantSubscriptions.Add(immediateSubscription);
        //                await _masterDb.SaveChangesAsync();

        //                var immediateTransaction = new SaasPaymentTransactionModel
        //                {
        //                    TenantId = tenantId,
        //                    SubscriptionId = immediateSubscription.SubscriptionId,
        //                    TransactionReference = razorpayOrderId,
        //                    RazorpayPaymentId = razorpayPaymentId,
        //                    RazorpayOrderId = razorpayOrderId,
        //                    RazorpaySignature = razorpaySignature,
        //                    Amount = amount,
        //                    Currency = "INR",
        //                    Status = "Success",
        //                    TransactionType = "Immediate Upgrade",
        //                    PaymentMethod = "Razorpay",
        //                    TransactionDate = IndianTime.Now,
        //                    CompletedDate = IndianTime.Now,
        //                    PlanName = plan.PlanName,
        //                    BillingCycle = billingCycle,
        //                    Description = $"Immediate upgrade to {plan.PlanName} plan",
        //                    CardType = cardType,
        //                    CardNetwork = cardNetwork,
        //                    CardLast4 = cardLast4,
        //                    BankName = bankName,
        //                    CreatedOn = IndianTime.Now
        //                };

        //                _masterDb.SaasPaymentTransactions.Add(immediateTransaction);
        //                await _masterDb.SaveChangesAsync();

        //                immediateSubscription.PaymentTransactionId = immediateTransaction.TransactionId.ToString();
        //                await _masterDb.SaveChangesAsync();

        //                await transaction.CommitAsync();

        //                return Json(new { success = true, message = $"Payment successful! Your {plan.PlanName} plan is now active immediately." });
        //            }
        //            else
        //            {
        //                _logger.LogInformation($"Processing scheduled upgrade, current expires: {currentSubscription.EndDate}");

        //                // Calculate credit from existing scheduled subscriptions
        //                decimal creditAmount = 0;
        //                string creditDescription = "";

        //                // Cancel existing scheduled subscriptions instead of deleting them
        //                // (they have payment transactions referencing them via FK)
        //                var existingScheduled = await _masterDb.TenantSubscriptions
        //                    .Where(s => s.TenantId == tenantId && s.Status == "Scheduled")
        //                    .ToListAsync();

        //                if (existingScheduled.Any())
        //                {
        //                    foreach (var scheduled in existingScheduled)
        //                    {
        //                        creditAmount += scheduled.Amount;
        //                        scheduled.Status = "Cancelled";
        //                        scheduled.CancelledOn = IndianTime.Now;
        //                        scheduled.CancellationReason = "Replaced by new scheduled plan - credit applied";
        //                        scheduled.UpdatedOn = IndianTime.Now;
        //                    }


        //                    // Validate that new plan is not cheaper (downgrade)
        //                    if (amount <= creditAmount)
        //                    {
        //        
        //                        _logger.LogWarning($"Attempted downgrade from ?{creditAmount} to ?{amount}");

        //                        return Json(new
        //                        {
        //                            success = false,
        //                            isDowngrade = true,
        //                            existingPlanName = existingScheduled.FirstOrDefault()?.Plan?.PlanName,
        //                            existingAmount = creditAmount,
        //                            newPlanName = plan.PlanName,
        //                            newAmount = amount,
        //                            refundAmount = creditAmount - amount,
        //                            message = $"Cannot process downgrade. You already paid ?{creditAmount:N0} for {existingScheduled.FirstOrDefault()?.Plan?.PlanName}. The {plan.PlanName} costs ?{amount:N0}. Please contact support."
        //                        });
        //                    }
        //                    await _masterDb.SaveChangesAsync();
        //                    _logger.LogInformation($"Cancelled {existingScheduled.Count} existing scheduled subscriptions. Total credit: ?{creditAmount}");

        //                    creditDescription = $" (Credit of ?{creditAmount:N0} applied from {existingScheduled.FirstOrDefault()?.Plan?.PlanName})";
        //                }
        //                // Create scheduled subscription first
        //                var startDate = currentSubscription.EndDate.AddDays(1);
        //                _logger.LogInformation($"Creating scheduled subscription starting: {startDate}");

        //                var sPlan = await _masterDb.SaasPlans.FindAsync(planId);
        //                var sAmt = billingCycle.ToLower() == "annual" ? sPlan.YearlyPrice : sPlan.MonthlyPrice;
        //                var sEnd = billingCycle.ToLower() == "annual" ? startDate.AddYears(1) : startDate.AddMonths(1); // (Inferred end-date logic cut off in screen)
        //                var scheduledSubscription = new TenantSubscriptionModel
        //                {
        //                    TenantId = tenantId,
        //                    PlanId = planId,
        //                    BillingCycle = billingCycle,
        //                    Amount = sAmt,
        //                    StartDate = startDate,
        //                    EndDate = sEnd,
        //                    Status = "Scheduled",
        //                    CreatedOn = DateTime.UtcNow
        //                };
        //                _masterDb.TenantSubscriptions.Add(scheduledSubscription);
        //                await _masterDb.SaveChangesAsync();
        //                _logger.LogInformation($"Scheduled subscription created with ID: {scheduledSubscription.SubscriptionId}");

        //                // Create payment transaction record
        //                var paymentTransaction = new SaasPaymentTransactionModel
        //                {
        //                    TenantId = tenantId,
        //                    SubscriptionId = scheduledSubscription.SubscriptionId,
        //                    TransactionReference = razorpayOrderId,
        //                    RazorpayPaymentId = razorpayPaymentId,
        //                    RazorpayOrderId = razorpayOrderId,
        //                    RazorpaySignature = razorpaySignature,
        //                    Amount = amount, // Full plan amount (before credit)
        //                    Currency = "INR",
        //                    TransactionType = "Scheduled Payment",
        //                    Status = "Success",
        //                    PaymentMethod = "Razorpay",
        //                    TransactionDate = IndianTime.Now,
        //                    CompletedDate = IndianTime.Now,
        //                    PlanName = plan.PlanName,
        //                    BillingCycle = billingCycle,
        //                    DiscountAmount = creditAmount, // Credit stored as discount
        //                    NetAmount = amount - creditAmount, // Actual amount paid
        //                    Description = $"Scheduled subscription payment for {plan.PlanName} plan{creditDescription}",
        //                    CardType = cardType,
        //                    CardNetwork = cardNetwork,
        //                    CardLast4 = cardLast4,
        //                    BankName = bankName,
        //                    CreatedOn = IndianTime.Now
        //                };

        //                _masterDb.SaasPaymentTransactions.Add(paymentTransaction);
        //                await _masterDb.SaveChangesAsync();
        //                _logger.LogInformation($"Transaction created with ID: {paymentTransaction.TransactionId}");

        //                // Update scheduled subscription with payment transaction reference
        //                // Re-fetch the subscription to ensure it's tracked by the current context
        //                var subscriptionToUpdate = await _masterDb.TenantSubscriptions
        //                    .FirstOrDefaultAsync(s => s.SubscriptionId == scheduledSubscription.SubscriptionId);

        //                if (subscriptionToUpdate != null)
        //                {
        //                    subscriptionToUpdate.PaymentTransactionId = paymentTransaction.TransactionId.ToString();
        //                    subscriptionToUpdate.LastPaymentDate = IndianTime.Now;
        //                    await _masterDb.SaveChangesAsync();
        //                    _logger.LogInformation($"Updated scheduled subscription with payment reference");
        //                }

        //                // Commit the database transaction
        //                await transaction.CommitAsync();

        //                var netAmountPaid = amount - creditAmount;
        //                var message = existingScheduled.Any()
        //                    ? creditAmount > 0
        //                        ? $"Payment successful! ?{netAmountPaid:N0} paid (?{creditAmount:N0} credit applied from previous plan). Your {plan.PlanName} plan will activate on {startDate:dd/MM/yyyy}."
        //                        : $"Payment successful! ?{netAmountPaid:N0} received. Previous scheduled plan replaced. Your new {plan.PlanName} plan will activate on {startDate:dd/MM/yyyy}."
        //                    : $"Payment successful! ?{netAmountPaid:N0} received. Your new {plan.PlanName} plan will activate on {startDate:dd/MM/yyyy}.";

        //                return Json(new { success = true, message });
        //            }
        //        }
        //        else
        //        {
        //            _logger.LogInformation("Creating immediate subscription");

        //            // Create immediate subscription first
        //            var iPlan = await _masterDb.SaasPlans.FindAsync(planId);
        //            var iAmt = billingCycle.ToLower() == "annual" ? iPlan.YearlyPrice : iPlan.MonthlyPrice;
        //            var iEnd = billingCycle.ToLower() == "annual" ? IndianTime.Now.AddYears(1) : IndianTime.Now.AddMonths(1); // (Inferred based on typical SaaS billing logic)

        //            var subscription = new TenantSubscriptionModel
        //            {
        //                TenantId = tenantId,
        //                PlanId = planId,
        //                BillingCycle = billingCycle,
        //                Amount = iAmt,
        //                StartDate = DateTime.UtcNow,
        //                EndDate = iEnd,
        //                Status = "Active",
        //                CreatedOn = DateTime.UtcNow
        //            };
        //            _masterDb.TenantSubscriptions.Add(subscription);
        //            await _masterDb.SaveChangesAsync();
        //            _logger.LogInformation($"Immediate subscription created with ID: {subscription.SubscriptionId}");

        //            // Create payment transaction record

        //            var paymentTransaction = new SaasPaymentTransactionModel
        //            {
        //                TenantId = tenantId,
        //                SubscriptionId = subscription.SubscriptionId,
        //                TransactionReference = razorpayOrderId,
        //                RazorpayPaymentId = razorpayPaymentId,
        //                RazorpayOrderId = razorpayOrderId,
        //                RazorpaySignature = razorpaySignature,
        //                Amount = amount,
        //                Currency = "INR",
        //                TransactionType = "Payment",
        //                Status = "Success",
        //                PaymentMethod = "Razorpay",
        //                TransactionDate = IndianTime.Now,
        //                CompletedDate = IndianTime.Now,
        //                PlanName = plan.PlanName,
        //                BillingCycle = billingCycle,
        //                NetAmount = amount,
        //                Description = $"Subscription payment for {plan.PlanName} plan",
        //                CardType = cardType,
        //                CardNetwork = cardNetwork,
        //                CardLast4 = cardLast4,
        //                BankName = bankName,
        //                CreatedOn = IndianTime.Now
        //            };

        //            _masterDb.SaasPaymentTransactions.Add(paymentTransaction);
        //            await _masterDb.SaveChangesAsync();
        //            _logger.LogInformation($"Transaction created with ID: {paymentTransaction.TransactionId}");

        //            // Update subscription with payment transaction reference
        //            // Re-fetch the subscription to ensure it's tracked by the current context
        //            var subscriptionToUpdate = await _masterDb.TenantSubscriptions
        //                .FirstOrDefaultAsync(s => s.SubscriptionId == subscription.SubscriptionId);

        //            if (subscriptionToUpdate != null)
        //            {
        //                subscriptionToUpdate.PaymentTransactionId = paymentTransaction.TransactionId.ToString();
        //                subscriptionToUpdate.LastPaymentDate = IndianTime.Now;
        //                await _masterDb.SaveChangesAsync();
        //                _logger.LogInformation($"Updated subscription with payment reference");
        //            }

        //            // Commit the database transaction
        //            await transaction.CommitAsync();

        //            return Json(new { success = true, message = $"Payment successful! ?{amount:N0} received. Your {plan.PlanName} subscription is now active." });
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        // Rollback the transaction on error
        //        await transaction.RollbackAsync();

        //        _logger.LogError(ex, $"Error processing payment confirmation for partner {tenantId}, plan {planId}");

        //        // Log inner exception details for better debugging
        //        var innerMessage = ex.InnerException?.Message ?? "No inner exception";
        //        var innerStackTrace = ex.InnerException?.StackTrace ?? "No stack trace";
        //        _logger.LogError($"Inner Exception: {innerMessage}");
        //        _logger.LogError($"Inner Stack Trace: {innerStackTrace}");

        //        // Provide detailed error message for debugging
        //        var errorDetails = ex.InnerException != null
        //            ? $"{ex.Message} - Inner: {ex.InnerException.Message}"
        //            : ex.Message;

        //        return Json(new { success = false, message = $"Payment processing failed: {errorDetails}" });
        //    }
        //}

        // Admin: Manage Partner Subscriptions
        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> ManageTenantSubscriptions(string search, int? plan, string billing, DateTime? fromDate, DateTime? toDate, int page = 1)
        {
            const int pageSize = 10;

            // Get only partners with active subscriptions
            var query = _masterDb.TenantSubscriptions
                .Where(s => s.Status == "Active")
                .AsQueryable();

            // Apply filters
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(s => s.Tenant.CompanyName != null && s.Tenant.CompanyName.Contains(search));
            }

            if (plan.HasValue)
            {
                query = query.Where(s => s.PlanId == plan.Value);
            }

            if (!string.IsNullOrEmpty(billing))
            {
                query = query.Where(s => s.BillingCycle == billing);
            }

            if (fromDate.HasValue)
            {
                query = query.Where(s => s.StartDate >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(s => s.StartDate <= toDate.Value);
            }

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            var subscriptions = await query
                .OrderByDescending(s => s.CreatedOn)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var tenantIds = subscriptions.Select(s => s.TenantId).Distinct().ToList();
            var scheduledSubscriptions = await _masterDb.TenantSubscriptions
                .Where(s => tenantIds.Contains(s.TenantId) && s.Status == "Scheduled")
                .Select(s => new
                {
                    s.TenantId,
                    s.PlanId,
                    s.BillingCycle,
                    PlanName = s.Plan.PlanName
                })
                .ToListAsync();

            ViewBag.ScheduledSubscriptions = scheduledSubscriptions;

            var availablePlans = await _masterDb.SaasPlans.Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync();
            var allPlans = await _masterDb.SaasPlans.ToListAsync();

            ViewBag.AvailablePlans = availablePlans;
            ViewBag.Plans = allPlans;
            ViewBag.Search = search;
            ViewBag.SelectedPlan = plan?.ToString();
            ViewBag.SelectedBilling = billing;
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;

            return View(subscriptions);
        }

        // --- Administrative: Pending Refunds & Processing ---

        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> PendingRefunds(string? search, int page = 1)
        {
            var (userId, role, tenantId) = GetCurrentUserContext();
            int pageSize = 20;

            // Get cancellation transactions that need refund processing
            var query = _masterDb.SaasPaymentTransactions
                .Where(t => t.TransactionType == "Cancellation" &&
                           t.Status == "Cancelled" &&
                           t.Description != null &&
                           t.Description.Contains("Refund Pending"))
                .AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(t => t.Tenant!.CompanyName.Contains(search) ||
                                       t.Tenant.CompanyName.Contains(search));
            }

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            var cancellationTransactions = await query
                .OrderByDescending(t => t.TransactionDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Search = search;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;
            ViewBag.TotalRefundAmount = cancellationTransactions.Sum(t => t.Amount);
            ViewBag.IsAdmin = role?.ToLower() == "admin";

            return View(cancellationTransactions);
        }

        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> GetRefundDetails(int transactionId)
        {
            try
            {
                var cancellationTransaction = await _masterDb.SaasPaymentTransactions
                    .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

                if (cancellationTransaction == null)
                    return Json(new { success = false, message = "Transaction not found" });

                var subscription = await _masterDb.TenantSubscriptions
                    .FirstOrDefaultAsync(s => s.SubscriptionId == cancellationTransaction.SubscriptionId);

                // Find the original payment transaction
                var paymentTransaction = await _masterDb.SaasPaymentTransactions
                    .Where(t => t.SubscriptionId == cancellationTransaction.SubscriptionId &&
                               t.Status == "Success")
                    .OrderByDescending(t => t.TransactionDate)
                    .FirstOrDefaultAsync();

                if (paymentTransaction == null)
                {
                    // If no transaction found for this subscription, look for any recent payment by this partner
                    paymentTransaction = await _masterDb.SaasPaymentTransactions
                        .Where(t => t.TenantId == cancellationTransaction.TenantId &&
                                   t.Status == "Success" &&
                                   !string.IsNullOrEmpty(t.RazorpayPaymentId) &&
                                   !t.TransactionType.Contains("Refund")
                                   && !t.TransactionType.Contains("Cancellation"))
                        .OrderByDescending(t => t.TransactionDate)
                        .FirstOrDefaultAsync();
                }

                return Json(new
                {
                    success = true,
                    refundDetails = new
                    {
                        transactionId = transactionId,
                        partnerName = cancellationTransaction.Tenant.CompanyName,
                        partnerEmail = cancellationTransaction.Tenant.Email,
                        refundAmount = cancellationTransaction.Amount,
                        planName = cancellationTransaction.PlanName,
                        billingCycle = cancellationTransaction.BillingCycle,
                        cancelledDate = cancellationTransaction.TransactionDate.ToString("MMM dd, yyyy HH:mm"),
                        originalPayment = paymentTransaction != null ? new
                        {
                            transactionId = paymentTransaction.TransactionId,
                            razorpayPaymentId = paymentTransaction.RazorpayPaymentId,
                            amount = paymentTransaction.Amount,
                            paymentDate = paymentTransaction.TransactionDate.ToString("MMM dd, yyyy HH:mm"),
                            cardType = paymentTransaction.CardType,
                            cardNetwork = paymentTransaction.CardNetwork,
                            cardLast4 = paymentTransaction.CardLast4,
                            bankName = paymentTransaction.BankName
                        } : null,
                        canProcessRazorpayRefund = paymentTransaction != null && !string.IsNullOrEmpty(paymentTransaction.RazorpayPaymentId)
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting refund details for transaction {transactionId}");
                return Json(new { success = false, message = $"Error getting refund details {ex.Message}" });
            }
        }

        [HttpPost]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> MarkRefundProcessed(int transactionId, string refundNotes)
        {
            try
            {
                var cancellationTransaction = await _masterDb.SaasPaymentTransactions
                    .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

                if (cancellationTransaction == null)
                    return Json(new { success = false, message = "Transaction not found" });

                var subscription = await _masterDb.TenantSubscriptions
                    .FirstOrDefaultAsync(s => s.SubscriptionId == cancellationTransaction.SubscriptionId);

                if (subscription == null)
                    return Json(new { success = false, message = "Subscription not found" });

                var paymentTransaction = await _masterDb.SaasPaymentTransactions
                    .Where(t => t.SubscriptionId == cancellationTransaction.SubscriptionId)
                    .FirstOrDefaultAsync();

                if (paymentTransaction == null)
                {
                    paymentTransaction = await _masterDb.SaasPaymentTransactions
                        .Where(t => t.SubscriptionId == cancellationTransaction.SubscriptionId
                          && t.Status == "Success"
                          && t.TransactionType != "Refund"
                          && t.TransactionType != " Cancellation")
                        .OrderByDescending(t => t.TransactionDate)
                        .FirstOrDefaultAsync();
                }

                if (paymentTransaction == null || string.IsNullOrEmpty(paymentTransaction.RazorpayPaymentId))
                {
                    paymentTransaction = await _masterDb.SaasPaymentTransactions
                        .Where(t => t.TenantId == subscription.TenantId
                          && t.Status == "Success"
                          && !string.IsNullOrEmpty(t.RazorpayPaymentId)
                          && t.TransactionType != "Refund"
                          && t.TransactionType != " Cancellation")
                        .OrderByDescending(t => t.TransactionDate)
                        .FirstOrDefaultAsync();
                }
                if (paymentTransaction == null || string.IsNullOrEmpty(paymentTransaction.RazorpayPaymentId))
                {
                    cancellationTransaction.Description = cancellationTransaction.Description?.Replace("Refund Pending", "Manual Refund Processed") + $" - Admin Notes: {refundNotes}";
                    if (subscription != null)
                    {


                        subscription.Status = "Cancelled";
                        subscription.CancelledOn = IndianTime.Now;
                        subscription.CancellationReason = "PERMANENTLY CANCELLED - Manual Refund Processed - " + refundNotes;
                        subscription.UpdatedOn = IndianTime.Now;
                    }
                    var manualRefundTransaction = new SaasPaymentTransactionModel
                    {
                        TenantId = subscription.TenantId,
                        SubscriptionId = subscription.SubscriptionId,
                        TransactionReference = "manual_refund_" + IndianTime.Now.Ticks,
                        Amount = cancellationTransaction.Amount,
                        Currency = "INR",
                        Status = "Success",
                        TransactionType = "Refund",
                        PaymentMethod = "Manual",
                        TransactionDate = IndianTime.Now,
                        CompletedDate = IndianTime.Now,
                        Description = "Manual refund for (" + subscription.Plan.PlanName + ") - Admin will transfer (" + cancellationTransaction.Amount + ") to partner - " + refundNotes,
                        PlanName = subscription.Plan.PlanName,
                        BillingCycle = subscription.BillingCycle,
                        NetAmount = cancellationTransaction.Amount,
                        CreatedOn = IndianTime.Now
                    };

                    _masterDb.SaasPaymentTransactions.Add(manualRefundTransaction);
                    await _masterDb.SaveChangesAsync();

                    _logger.LogInformation($"Manual refund transaction created for {subscription.Tenant.CompanyName}. Amount: {cancellationTransaction.Amount}");

                    return Json(new { success = true, message = $"Manual refund of {cancellationTransaction.Amount:NO} processed successfully to {cancellationTransaction.Tenant?.CompanyName} " });
                }

                // Process Razorpay refund
                var (success, refundId, message) = await _razorpayService.CreateRefundAsync(
                    paymentTransaction.RazorpayPaymentId,
                    cancellationTransaction.Amount,
                    refundNotes
                );

                if (success)
                {
                    // Update cancellation transaction
                    cancellationTransaction.Description = cancellationTransaction.Description?.Replace("Refund Pending", "Refund Processed") + $" - Razorpay Refund ID: {refundId} - Admin Notes: {refundNotes}";

                    // Update the related subscription status to ensure it's properly cancelled
                    if (subscription != null)
                    {
                        subscription.Status = "Cancelled";
                        subscription.CancelledOn = IndianTime.Now;
                        subscription.CancellationReason = "PERMANENTLY CANCELLED - Razorpay Refund Processed - DO NOT REACTIVATE";
                        subscription.UpdatedOn = IndianTime.Now;
                    }

                    // Create refund transaction record
                    var refundTransaction = new SaasPaymentTransactionModel
                    {
                        TenantId = subscription.TenantId,
                        SubscriptionId = subscription.SubscriptionId,
                        TransactionReference = refundId,
                        RazorpayPaymentId = paymentTransaction.RazorpayPaymentId,
                        Amount = cancellationTransaction.Amount,
                        Currency = "INR",
                        TransactionType = "Refund",
                        Status = "Success",
                        PaymentMethod = paymentTransaction.PaymentMethod,
                        TransactionDate = IndianTime.Now,
                        CompletedDate = IndianTime.Now,
                        Description = $"Refund processed for {cancellationTransaction.PlanName} - Razorpay Refund ID: {refundId} - {refundNotes}",
                        PlanName = cancellationTransaction.PlanName,
                        BillingCycle = cancellationTransaction.BillingCycle,
                        NetAmount = cancellationTransaction.Amount,
                        CreatedOn = IndianTime.Now
                    };

                    _masterDb.SaasPaymentTransactions.Add(refundTransaction);
                    await _masterDb.SaveChangesAsync();

                    // (Inferred log cut off at the bottom of the screen)
                    _logger.LogInformation($"Razorpay refund processed for cancellation transaction ID: {cancellationTransaction.TransactionId}. Refund ID: {refundId}, Amount: {cancellationTransaction.Amount}");
                    return Json(new
                    {
                        success = true,
                        message = $"Refund of ?{cancellationTransaction.Amount:N0} processed successfully for {cancellationTransaction.Tenant?.CompanyName}. Refund ID: {refundId}"
                    });
                }
                else
                {
                    _logger.LogError($"Razorpay refund failed for cancellation {transactionId}: {message}");
                    return Json(new { success = false, message = $"Razorpay refund failed: {message}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing refund for transaction {transactionId}");
                return Json(new { success = false, message = $"Error processing refund: {ex.Message}" });
            }
        }


        [HttpPost]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> AdminUpgradePlan(int tenantId, int newPlanId, string billingCycle)
        {
            try
            {
                var plan = await _masterDb.SaasPlans.FindAsync(newPlanId);
                if (plan == null) return Json(new { success = false, message = "Invalid plan selected" });

                // Get current active subscription
                var currentSubscription = await _masterDb.TenantSubscriptions
                    .Where(s => s.TenantId == tenantId && s.Status == "Active")
                    .FirstOrDefaultAsync();

                // Cancel existing scheduled subscriptions
                var existingScheduled = await _masterDb.TenantSubscriptions
                    .Where(s => s.TenantId == tenantId && s.Status == "Scheduled")
                    .ToListAsync();

                foreach (var scheduled in existingScheduled)
                {
                    scheduled.Status = "Cancelled";
                    scheduled.CancelledOn = IndianTime.Now;
                    scheduled.CancellationReason = "Replaced by new scheduled plan";
                    scheduled.UpdatedOn = IndianTime.Now;
                }

                var amount = billingCycle.ToLower() == "annual" ? plan.YearlyPrice : plan.MonthlyPrice;
                var startDate = currentSubscription?.EndDate.AddDays(1) ?? IndianTime.Now;
                var endDate = billingCycle.ToLower() == "annual" ? startDate.AddYears(1) : startDate.AddMonths(1);

                // Create admin transaction
                var transaction = new SaasPaymentTransactionModel
                {
                    TenantId = tenantId,
                    TransactionReference = $"admin_upgrade_{IndianTime.Now.Ticks}",
                    Amount = amount,
                    TransactionType = "Upgrade",
                    Status = "Success",
                    PaymentMethod = "Admin",
                    TransactionDate = IndianTime.Now,
                    CompletedDate = IndianTime.Now,
                    PlanName = plan.PlanName,
                    BillingCycle = billingCycle,
                    NetAmount = amount,
                    Description = $"Admin upgrade to {plan.PlanName} plan (scheduled)"
                };

                _masterDb.SaasPaymentTransactions.Add(transaction);
                await _masterDb.SaveChangesAsync();


                // Create new active subscription
                var newSub = new TenantSubscriptionModel
                {
                    TenantId = tenantId,
                    PlanId = newPlanId,
                    BillingCycle = billingCycle,
                    Amount = amount,
                    StartDate = IndianTime.Now,
                    EndDate = endDate,
                    Status = "Scheduled",
                    CreatedOn = IndianTime.Now,
                    UpdatedOn = IndianTime.Now
                };

                _masterDb.TenantSubscriptions.Add(newSub);
                await _masterDb.SaveChangesAsync();
                transaction.SubscriptionId = newSub.SubscriptionId;
                var message = existingScheduled.Any() ?
                    $"previous secheduled plans replaced. new paln will start on {startDate:dd/MM/yyyy}!"
                    : $"Partner Plan scheduled to upgrade on {startDate:dd/MM/yyy}!";

                return Json(new { success = true, message = message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error upgrading plan");
                return Json(new { success = false, message = "Error upgrading plan: " + ex.Message });
            }
        }


        [HttpGet]
        public async Task<IActionResult> GetAvailablePlans()
        {
            var plans = await _masterDb.SaasPlans.Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync();
            return Json(plans);
        }

        [HttpGet]
        public async Task<IActionResult> CheckAgentLimit()
        {
            var (userId, role, tenantId) = GetCurrentUserContext();
            if (!tenantId.HasValue)
                return Json(new { canAdd = false, message = "Partner context not found" });

            var canAdd = true; var message = "";
            return Json(new { canAdd, message });
        }

        [HttpGet]
        public async Task<IActionResult> CheckLeadLimit()
        {
            var (userId, role, tenantId) = GetCurrentUserContext();
            if (!tenantId.HasValue)
                return Json(new { canAdd = false, message = "Partner context not found" });

            var canAdd = true; var message = "";
            return Json(new { canAdd, message });
        }

        [HttpGet]
        public async Task<IActionResult> CheckFeatureAccess(string feature)
        {
            var (userId, role, tenantId) = GetCurrentUserContext();

            if (!tenantId.HasValue)
            {
                return Json(new { hasAccess = false, hasSubscription = false, message = "No tenant context found." });
            }

            // Get active subscription for this tenant
            var activeSub = await _masterDb.TenantSubscriptions
                .Where(s => s.TenantId == tenantId.Value && s.Status == "Active" && s.EndDate > IndianTime.Now)
                .OrderByDescending(s => s.CreatedOn)
                .FirstOrDefaultAsync();

            if (activeSub == null)
            {
                return Json(new { hasAccess = false, hasSubscription = false, message = "No active subscription found. Please subscribe to a plan." });
            }

            // Get the plan to check features
            var plan = await _masterDb.SaasPlans.FindAsync(activeSub.PlanId);
            if (plan == null)
            {
                return Json(new { hasAccess = false, hasSubscription = true, message = "Subscription plan not found. Please contact support." });
            }

            // Check specific feature access
            bool hasAccess = feature.ToLower() switch
            {
                "whatsapp" => plan.HasWhatsAppIntegration,
                "facebook" => plan.HasFacebookIntegration,
                "email" => plan.HasEmailIntegration,
                "customapi" => plan.HasCustomAPIAccess,
                "advancedreports" => plan.HasAdvancedReports,
                "prioritysupport" => plan.HasPrioritySupport,
                "dataexport" => plan.HasAdvancedReports || plan.HasCustomAPIAccess,
                _ => false
            };

            return Json(new { 
                hasAccess, 
                hasSubscription = true,
                planName = plan.PlanName,
                feature = feature,
                message = hasAccess 
                    ? $"Access granted to {feature}." 
                    : $"The '{feature}' feature is not available in your current {plan.PlanName} plan. Please upgrade to access this feature."
            });
        }

        [HttpGet]
        public async Task<IActionResult> ExportTransactions(string format = "excel", string? status = null, string? type = null, DateTime? fromDate = null, DateTime? toDate = null)
        {
            var (userId, role, tenantId) = GetCurrentUserContext();

            var query = _masterDb.SaasPaymentTransactions
                .Include(t => t.Subscription)
                .AsQueryable();

            if (role?.ToLower() == "partner" && tenantId.HasValue)
                query = query.Where(t => t.TenantId == tenantId.Value);

            if (!string.IsNullOrEmpty(status)) query = query.Where(t => t.Status == status);
            if (!string.IsNullOrEmpty(type)) query = query.Where(t => t.TransactionType == type);
            if (fromDate.HasValue) query = query.Where(t => t.TransactionDate >= fromDate.Value);
            if (toDate.HasValue) query = query.Where(t => t.TransactionDate <= toDate.Value.AddDays(1));

            var transactions = await query.OrderByDescending(t => t.TransactionDate).ToListAsync();

            if (format.ToLower() == "csv")
            {
                var csv = GenerateCSV(transactions);
                return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"Transactions_{IndianTime.Now:yyyy-MM-dd}.csv");
            }

            // Fallback to CSV for now
            var csvContent = GenerateCSV(transactions);
            return File(System.Text.Encoding.UTF8.GetBytes(csvContent), "application/vnd.ms-excel", $"Transactions_{IndianTime.Now:yyyy-MM-dd}.csv");
        }

        private string GenerateCSV(List<SaasPaymentTransactionModel> transactions)
        {
            var csv = new StringBuilder();
            csv.AppendLine("Date,Partner,Plan,Amount,Type,Status,Payment Method,Transaction ID");

            foreach (var t in transactions)
            {
                csv.Append($"{t.TransactionDate:yyyy-MM-dd HH:mm},");
                csv.Append($"\"{t.Tenant?.CompanyName ?? "N/A"}\",");
                csv.Append($"\"{t.PlanName ?? "N/A"}\",");
                csv.Append($"{t.Amount},");
                csv.Append($"{t.TransactionType},");
                csv.Append($"{t.Status},");
                csv.Append($"{t.PaymentMethod},");
                csv.Append($"\"{t.RazorpayPaymentId ?? t.TransactionReference}\"");
                csv.AppendLine();
            }
            return csv.ToString();
        }

        //private async Task CancelExistingScheduledSubscriptions(int tenantId, decimal creditAmount)
        //        {
        //            var existingScheduled = await _masterDb.TenantSubscriptions
        //                .Where(s => s.TenantId == tenantId && s.Status == "Scheduled")
        //                .ToListAsync();

        //            foreach (var scheduled in existingScheduled)
        //            {
        //                scheduled.Status = "Cancelled";
        //                scheduled.CancelledOn = IndianTime.Now;
        //                scheduled.CancellationReason = "Replaced by new scheduled plan - credit applied";
        //                scheduled.UpdatedOn = IndianTime.Now;
        //            }

        //            await _masterDb.SaveChangesAsync();
        //        }



        // --- Razorpay Webhook Handler (Pages 70-79) ---

        [HttpPost]
        [Route("webhook/razorpay")]
        public async Task<IActionResult> RazorpayWebhook()
        {
            try
            {
                var body = await new StreamReader(Request.Body).ReadToEndAsync();
                var signature = Request.Headers["X-Razorpay-Signature"].FirstOrDefault();

                if (string.IsNullOrEmpty(signature)) // || !_razorpayService.VerifyWebhookSignature(body, signature))
                {
                    _logger.LogWarning("Webhook signature verification failed");
                    return BadRequest("Invalid signature");
                }

                var webhook = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(body);
                var eventType = webhook.GetProperty("event").GetString();
                var eventId = webhook.GetProperty("id").GetString();

                // Idempotency check - prevent duplicate webhook processing
                var existingWebhook = await _masterDb.SaasPaymentTransactions
                    .FirstOrDefaultAsync(t => t.WebhookEventId == eventId);

                if (existingWebhook != null)
                {
                    _logger.LogInformation($"Webhook event {eventId} already processed, skipping");
                    return Ok(new { status = "processed" });
                }

                _logger.LogInformation($"Processing webhook event: {eventType}");

                switch (eventType)
                {
                    case "payment.captured":
                        await HandlePaymentCaptured(webhook, eventId);
                        break;
                    case "payment.failed":
                        await HandlePaymentFailed(webhook, eventId);
                        break;
                    case "payment.authorized":
                        await HandlePaymentAuthorized(webhook, eventId);
                        break;
                    default:
                        _logger.LogInformation($"Unhandled webhook event type: {eventType}");
                        break;
                }

                return Ok(new { status = "processed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Razorpay webhook");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        private async Task HandlePaymentCaptured(JsonElement webhook, string eventId)
        {
            var paymentEntity = webhook.GetProperty("payload").GetProperty("payment").GetProperty("entity");
            var paymentId = paymentEntity.GetProperty("id").GetString();
            var orderId = paymentEntity.GetProperty("order_id").GetString();

            _logger.LogInformation($"Payment captured: {paymentId} for order {orderId}");

            var transaction = await _masterDb.SaasPaymentTransactions
                .FirstOrDefaultAsync(t => t.RazorpayOrderId == orderId);

            if (transaction != null && (transaction.Status == "Pending" || transaction.Status == "Authorized"))
            {
                var oldStatus = transaction.Status;
                transaction.Status = "Success";
                transaction.CompletedDate = IndianTime.Now;
                transaction.WebhookEventId = eventId;
                transaction.RazorpayPaymentId = paymentId;

                _logger.LogInformation($"Payment captured for transaction {transaction.TransactionId}, type: {transaction.TransactionType}, amount: {transaction.Amount}");

                // IMMEDIATELY activate plan for upgrade transactions
                if (transaction.TransactionType.StartsWith("Upgrade_") == true)
                {
                    _logger.LogInformation($"Processing upgrade activation");
                    await ActivatePlanImmediately(transaction);
                }
                    // Send plan activation confirmation email to partner
                    if (transaction.Tenant != null && !string.IsNullOrEmpty(transaction.Tenant.Email))
                    {
                        var emailPartner = transaction.Tenant;
                        var emailPlanName = transaction.PlanName ?? "";
                        var emailBillingCycle = transaction.BillingCycle ?? "monthly";
                        var emailAmount = transaction.Amount;
                        _ = Task.Run(async () =>
                        {
            using var transaction = await _masterDb.Database.BeginTransactionAsync();
            try
            {
                                var scopeFactory = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();
                                using var emailScope = scopeFactory.CreateScope();
                                var emailService = emailScope.ServiceProvider.GetRequiredService<EmailService>();
                                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                                await emailService.SendTemplateEmailAsync(
                                    "PlanChangeNotification",
                                    emailPartner.Email,
                                    0,
                                    new Dictionary<string, string>
                                    {
                                        ["PlanName"] = emailPlanName,
                                        ["BillingCycle"] = emailBillingCycle,
                                        ["Amount"] = $"{emailAmount:N2}",
                                        ["Name"] = emailPartner.CompanyName ?? "",
                                        ["DashboardUrl"] = $"{baseUrl}/SaasSubscription/MyPlan",
                                        ["CompanyName"] = "PropTech CRM",
                                        ["Year"] = IndianTime.Now.Year.ToString()
                                    },
                                    "Subscription");
                            }
                            catch { }
                        });
                    }

                await _masterDb.SaveChangesAsync();
                _logger.LogInformation($"Transaction {transaction.TransactionId} marked as Success");
            }
            else
            {
                _logger.LogWarning($"No transaction found for order {orderId}, attempting auto-activation from payment details");

                // Get payment details from Razorpay API to find partner email
                var (success, paymentDetails) = await _razorpayService.FetchPaymentAsync(paymentId);
                if (success && paymentDetails.HasValue)
                {
                    return;
                }
                var payment = paymentDetails.Value;
                var amount = payment.GetProperty("amount").GetInt32() / 100; // Convert from paise
                var customerEmail = "";

                // Try to get email from payment details
                if (payment.TryGetProperty("email", out var emailProp))
                {
                    customerEmail = emailProp.GetString() ?? "";
                }
                else if (payment.TryGetProperty("contact", out var contactProp) && contactProp.GetString() != null)
                {
                    var contact = contactProp.GetString();
                    if (contact != null && contact.Contains("@"))
                    {
                        customerEmail = contact;
                    }
                }

                _logger.LogInformation($"Payment details: amount={amount}, email={customerEmail}");

                // Find partner by email
                var partner = await _masterDb.Tenants
                    .FirstOrDefaultAsync(p => p.CompanyName == customerEmail);

                if (partner != null)
                {
                    _logger.LogInformation($"Found partner {partner.CompanyName} (ID: {partner.TenantId}) for email {customerEmail}, amount: ?{amount}");

                    // Determine plan based on amount
                    var plan = await _masterDb.SaasPlans
                        .Where(p => p.IsActive && Math.Abs(p.MonthlyPrice - amount) < 1) // Match within ?1
                        .FirstOrDefaultAsync();

                    if (plan == null)
                    {
                        // Try yearly price match
                        plan = await _masterDb.SaasPlans
                            .Where(p => p.IsActive && Math.Abs(p.YearlyPrice - amount) < 1)
                            .FirstOrDefaultAsync();
                    }

                    if (plan != null)
                    {
                        _logger.LogInformation($"Matched plan {plan.PlanName} (ID: {plan.PlanId}) for amount ?{amount}");

                        // End current subscription
                        var currentSub = await _masterDb.TenantSubscriptions
                            .Where(s => s.TenantId == partner.TenantId && s.Status == "Active")
                            .FirstOrDefaultAsync();

                        if (currentSub != null)
                        {
                            _logger.LogInformation($"Ending current subscription {currentSub.SubscriptionId} for partner {partner.TenantId}");
                            currentSub.Status = "Expired";
                            currentSub.EndDate = IndianTime.Now;
                            currentSub.CancellationReason = $"Upgraded to {plan.PlanName} via payment";
                            currentSub.CancelledOn = IndianTime.Now;
                            currentSub.UpdatedOn = IndianTime.Now;
                            _masterDb.TenantSubscriptions.Update(currentSub);
                        }

                        // Determine billing cycle based on amount
                        var billingCycle = Math.Abs(plan.YearlyPrice - amount) < 1 ? "annual" : "monthly";
                        var endDate = billingCycle == "annual" ? IndianTime.Now.AddYears(1) : IndianTime.Now.AddMonths(1);
                        // Determine billing cycle based on amount

                        // Create new subscription
                        var newSub = new TenantSubscriptionModel
                        {
                            TenantId = partner.TenantId,
                            PlanId = plan.PlanId,
                            BillingCycle = billingCycle,
                            Amount = amount,
                            StartDate = IndianTime.Now,
                            EndDate = endDate,
                            Status = "Active",
                            PaymentMethod = "Razorpay",
                            PaymentTransactionId = paymentId,
                            LastPaymentDate = IndianTime.Now,
                            NextPaymentDate = endDate,
                            AutoRenew = false,
                            CreatedOn = IndianTime.Now,
                            UpdatedOn = IndianTime.Now
                        };

                        _masterDb.TenantSubscriptions.Add(newSub);
                        await _masterDb.SaveChangesAsync();

                        // Create transaction record
                        var newTransaction = new SaasPaymentTransactionModel
                        {
                            TenantId = partner.TenantId,
                            SubscriptionId = newSub.SubscriptionId,
                            TransactionReference = orderId,
                            RazorpayPaymentId = paymentId,
                            RazorpayOrderId = orderId,
                            Amount = amount,
                            Currency = "INR",
                            Status = "Success",
                            TransactionType = "Auto Upgrade",
                            PaymentMethod = "Razorpay",
                            TransactionDate = IndianTime.Now,
                            CompletedDate = IndianTime.Now,
                            Description = $"Auto-activated {plan.PlanName} plan from webhook",
                            PlanName = plan.PlanName,
                            BillingCycle = billingCycle,
                            NetAmount = amount,
                            WebhookEventId = eventId,
                            CreatedOn = IndianTime.Now
                        };

                        _masterDb.SaasPaymentTransactions.Add(newTransaction);
                        await _masterDb.SaveChangesAsync();
                        // Send plan activation confirmation email to partner
                        if (!string.IsNullOrEmpty(partner.Email))
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var scopeFactory = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();
                                    using var emailScope = scopeFactory.CreateScope();
                                    var emailService = emailScope.ServiceProvider.GetRequiredService<EmailService>();
                                    var emailBaseUrl = $"{Request.Scheme}://{Request.Host}";
                                    await emailService.SendTemplateEmailAsync(
                                        "PlanChangeNotification",
                                        partner.Email,
                                        0,
                                        new Dictionary<string, string>
                                        {
                                            ["PlanName"] = plan.PlanName ?? "",
                                            ["BillingCycle"] = billingCycle,
                                            ["Amount"] = $"{amount:N2}",
                                            ["Name"] = partner.CompanyName ?? "",
                                            ["DashboardUrl"] = $"{emailBaseUrl}/SaasSubscription/MyPlan",
                                            ["CompanyName"] = "PropTech CRM",
                                            ["Year"] = IndianTime.Now.Year.ToString()
                                        },
                                        "Subscription");
                                }
                                catch { }
                            });
                        }

                        _logger.LogInformation($"Auto-activated {plan.PlanName} for partner {partner.CompanyName} from webhook payment {paymentId}. New subscription ID: {newSub.SubscriptionId}");
                    }
                    else
                    {
                        //_logger.LogWarning($"No matching plan found for amount ?{amount}. Available plans: {string.Join(", ", await _masterDb.SaasPlans.Where(p => p.IsActive).Select(p => $"{p.PlanName}"))}");
                    }
                }
                else
                {
                    //_logger.LogWarning($"No partner found for email '{customerEmail}'. Available partners: {string.Join(", ", await _masterDb.Tenants.Select(p => p.CompanyName).ToListAsync())}");
                }
            }
        }
        private async Task ActivateSubscriptionOnCapture(TenantSubscriptionModel subscription)
        {
            try
            {
                // Only activate if subscription is not already active and payment is captured
                if (subscription.Status != "Active")
                {
                    subscription.Status = "Active";
                    subscription.StartDate = IndianTime.Now;
                    subscription.UpdatedOn = IndianTime.Now;

                    _logger.LogInformation($"Activated subscription {subscription.SubscriptionId} on payment capture");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error activating subscription {subscription.SubscriptionId}");
            }
        }

        private async Task ActivatePlanFromWebhook(SaasPaymentTransactionModel transaction)
        {
            try
            {
                var partner = await _masterDb.Tenants.FindAsync(transaction.TenantId);
                if (partner == null) return;

                // Extract plan info from transaction
                var planName = transaction.PlanName;
                var billingCycle = transaction.BillingCycle ?? "monthly";

                var plan = await _masterDb.SaasPlans
                    .FirstOrDefaultAsync(p => p.PlanName == planName);
                if (plan == null) return;

                // Check if partner has no active subscription
                var currentSubscription = await _masterDb.TenantSubscriptions
            .Where(s => s.TenantId == transaction.TenantId && s.Status == "Active")
            .FirstOrDefaultAsync();

                if (currentSubscription == null)
                {
                    // Create immediate active subscription
                    var newSubscription = new TenantSubscriptionModel
                    {
                        TenantId = transaction.TenantId,
                        PlanId = plan.PlanId,
                        BillingCycle = billingCycle,
                        Amount = transaction.Amount,
                        StartDate = IndianTime.Now,
                        EndDate = billingCycle.ToLower() == "annual" ? IndianTime.Now.AddYears(1) : IndianTime.Now.AddMonths(1),
                        Status = "Active",
                        PaymentMethod = "Razorpay",
                        PaymentTransactionId = transaction.TransactionId.ToString(),
                        LastPaymentDate = IndianTime.Now,
                        NextPaymentDate = billingCycle.ToLower() == "annual" ? IndianTime.Now.AddYears(1) : IndianTime.Now.AddMonths(1),
                        AutoRenew = false,
                        CreatedOn = IndianTime.Now,
                        UpdatedOn = IndianTime.Now,
                    };

                    _masterDb.TenantSubscriptions.Add(newSubscription);
                    await _masterDb.SaveChangesAsync();

                    // Update transaction with subscription ID
                    transaction.SubscriptionId = newSubscription.SubscriptionId;

                    _logger.LogInformation($"Activated {planName} plan for partner {partner.CompanyName} via webhook");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error activating plan from webhook for transaction {transaction.TransactionId}");
            }
        }
        private async Task ActivatePlanImmediately(SaasPaymentTransactionModel transaction)
        {
            try
            {
                _logger.LogInformation($"ActivatePlanImmediately called for transaction {transaction.TransactionId}, partner: {transaction.TenantId}");

                var partner = await _masterDb.Tenants.FindAsync(transaction.TenantId);
                if (partner == null)
                {
                    _logger.LogWarning($"Partner {transaction.TenantId} not found");
                    return;
                }

                var upgradeType = transaction.TransactionType.Replace("Upgrade_", "") ?? "immediate";
                _logger.LogInformation($"Activating {transaction.PlanName} upgrade for partner {partner.CompanyName}");

                var plan = await _masterDb.SaasPlans
                    .FirstOrDefaultAsync(p => p.PlanName == transaction.PlanName);

                if (plan == null)
                {
                    _logger.LogWarning($"Plan {transaction.PlanName} not found");
                    return;
                }

                _logger.LogInformation($"Activating {upgradeType} upgrade to {plan.PlanName} for partner {partner.CompanyName}");

                // End current subscription immediately for all upgrade types
                var currentSubscription = await _masterDb.TenantSubscriptions
                    .Where(s => s.TenantId == partner.TenantId && s.Status == "Active")
                    .FirstOrDefaultAsync();

                if (currentSubscription != null)
                {
                    _logger.LogInformation($"Ending current subscription {currentSubscription.SubscriptionId} for upgrade");
                    currentSubscription.Status = "Expired";
                    currentSubscription.EndDate = IndianTime.Now;
                    currentSubscription.UpdatedOn = IndianTime.Now;
                    currentSubscription.CancellationReason = $"Upgraded to {plan.PlanName} via payment";
                    currentSubscription.CancelledOn = IndianTime.Now;
                }

                // Create new active subscription
                var newSub = new TenantSubscriptionModel
                {
                    TenantId = transaction.TenantId,
                    PlanId = plan.PlanId,
                    BillingCycle = transaction.BillingCycle,
                    Amount = transaction.Amount,
                    StartDate = IndianTime.Now,
                    EndDate = (transaction.BillingCycle.ToLower() == "annual") ? IndianTime.Now.AddYears(1) : IndianTime.Now.AddMonths(1),
                    Status = "Active",
                    PaymentMethod = "Razorpay",
                    PaymentTransactionId = transaction.TransactionId.ToString(),
                    LastPaymentDate = IndianTime.Now,
                    NextPaymentDate = (transaction.BillingCycle.ToLower() == "annual") ? IndianTime.Now.AddYears(1) : IndianTime.Now.AddMonths(1),
                    AutoRenew = false,
                    CreatedOn = IndianTime.Now,
                    UpdatedOn = IndianTime.Now
                };

                _masterDb.TenantSubscriptions.Add(newSub);
                await _masterDb.SaveChangesAsync();

                transaction.SubscriptionId = newSub.SubscriptionId;
                await _masterDb.SaveChangesAsync();

                _logger.LogInformation($"Successfully upgraded plan to {plan.PlanName} for {partner.CompanyName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error activating plan from webhook for transaction {transaction.TransactionId}");
            }
        }
        // Debug endpoint to check transactions
        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> DebugTransactions(string? search = null)
        {
            var query = _masterDb.SaasPaymentTransactions
                .AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(t =>
                    (t.RazorpayPaymentId != null && t.RazorpayPaymentId.Contains(search)) ||
                    (t.RazorpayOrderId != null && t.RazorpayOrderId.Contains(search)) ||
                    (t.TransactionReference != null && t.TransactionReference.Contains(search)));
            }

            var transactions = await query
                .OrderByDescending(t => t.TransactionDate)
                .Take(20)
                .Select(t => new
                {
                    t.TransactionId,
                    t.RazorpayPaymentId,
                    t.RazorpayOrderId,
                    t.TransactionReference,
                    t.TransactionType,
                    t.Status,
                    t.Amount,
                    t.TransactionDate,
                    PartnerEmail = t.Tenant != null ? t.Tenant.CompanyName : null
                })
                .ToListAsync();

            return Json(new { success = true, transactions });
        }

        private async Task HandlePaymentFailed(JsonElement webhook, string eventId)
        {
            var paymentEntity = webhook.GetProperty("payload").GetProperty("payment").GetProperty("entity");
            var paymentId = paymentEntity.GetProperty("id").GetString();
            var orderId = paymentEntity.GetProperty("order_id").GetString();
            var errorDescription = paymentEntity.TryGetProperty("error_description", out var ed) ? ed.GetString() : "Payment failed";

            _logger.LogWarning($"Payment failed: {paymentId} for order {orderId}, error: {errorDescription}");

            var transaction = await _masterDb.SaasPaymentTransactions
                .FirstOrDefaultAsync(t => t.RazorpayOrderId == orderId);

            if (transaction != null)
            {
                transaction.Status = "Failed";
                transaction.CompletedDate = IndianTime.Now;
                transaction.WebhookEventId = eventId;
                transaction.RazorpayPaymentId = paymentId;
                transaction.Description += $" | Failed: {errorDescription}";

                _masterDb.SaasPaymentTransactions.Update(transaction);
                await _masterDb.SaveChangesAsync();
            }
        }


        private async Task HandlePaymentAuthorized(JsonElement webhook, string eventId)
        {
            var paymentEntity = webhook.GetProperty("payload").GetProperty("payment").GetProperty("entity");
            var paymentId = paymentEntity.GetProperty("id").GetString();
            var orderId = paymentEntity.GetProperty("order_id").GetString();

            _logger.LogInformation($"Payment authorized: {paymentId} for order {orderId}");

            var transaction = await _masterDb.SaasPaymentTransactions
                .FirstOrDefaultAsync(t => t.RazorpayOrderId == orderId);

            if (transaction != null && transaction.Status == "Pending")
            {
                transaction.Status = "Authorized";
                transaction.WebhookEventId = eventId;
                transaction.RazorpayPaymentId = paymentId;

                _masterDb.SaasPaymentTransactions.Update(transaction);
                await _masterDb.SaveChangesAsync();
            }
        }

        // Direct Razorpay activation endpoint
        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> ActivateFromRazorpay(string paymentId, int tenantId, int planId)
        {
            try
            {
                _logger.LogInformation($"Direct activation from Razorpay: payment={paymentId}, partner={tenantId}, plan={planId}");

                // Fetch payment from Razorpay API
                var (success, paymentDetails) = await _razorpayService.FetchPaymentAsync(paymentId);
                if (!success || !paymentDetails.HasValue)
                {
                    return Json(new { success = false, message = "Payment not found in Razorpay" });
                }

                var payment = paymentDetails.Value;
                var status = payment.GetProperty("status").GetString();
                var amount = payment.GetProperty("amount").GetInt32();
                if (status != "captured")
                {
                    return Json(new { success = false, message = $"Payment status is {status}, not captured" });
                }

                // Get partner and plan
                var partner = await _masterDb.Tenants.FindAsync(tenantId);
                if (partner == null)
                {
                    return Json(new { success = false, message = "Partner not found" });
                }

                var plan = await _masterDb.SaasPlans.FindAsync(planId);
                if (plan == null)
                {
                    return Json(new { success = false, message = "Plan not found" });
                }

                // End current subscription
                var currentSubscription = await _masterDb.TenantSubscriptions
                    .Where(s => s.TenantId == tenantId && s.Status == "Active")
                    .FirstOrDefaultAsync();

                if (currentSubscription != null)
                {
                    currentSubscription.Status = "Expired";
                    currentSubscription.EndDate = IndianTime.Now;
                    currentSubscription.UpdatedOn = IndianTime.Now;
                    currentSubscription.CancellationReason = $"Upgraded to {plan.PlanName} via direct activation";
                    currentSubscription.CancelledOn = IndianTime.Now;
                }

                // Create new active subscription
                var newSubscription = new TenantSubscriptionModel
                {
                    TenantId = tenantId,
                    PlanId = planId,
                    BillingCycle = "monthly",
                    Amount = amount,
                    StartDate = IndianTime.Now,
                    EndDate = IndianTime.Now.AddMonths(1),
                    Status = "Active",
                    PaymentMethod = "Razorpay",
                    PaymentTransactionId = paymentId,
                    LastPaymentDate = IndianTime.Now,
                    NextPaymentDate = IndianTime.Now.AddMonths(1),
                    AutoRenew = false,
                    CreatedOn = IndianTime.Now,

                    // Image 3: Conclusion of ActivateFromRazorpay & Start of PaymentSuccess
                    UpdatedOn = IndianTime.Now
                };

                _masterDb.TenantSubscriptions.Add(newSubscription);
                await _masterDb.SaveChangesAsync();

                _logger.LogInformation($"Successfully activated {plan.PlanName} for partner {partner.CompanyName}");

                return Json(new
                {
                    success = true,
                    message = $"Successfully activated {plan.PlanName} for {partner.CompanyName}",
                    subscriptionId = newSubscription.SubscriptionId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in direct Razorpay activation");
                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> PaymentSuccess(string paymentId, string orderId)
        {
            try
            {
                _logger.LogInformation($"Payment success: paymentId={paymentId}, orderId={orderId}");

                // Hit Razorpay API to get payment status
                var (success, paymentDetails) = await _razorpayService.FetchPaymentAsync(paymentId);
                if (!success || !paymentDetails.HasValue)
                {
                    ViewBag.Status = "Failed";
                    ViewBag.Error = "Payment not found";
                    return View();
                }

                var payment = paymentDetails.Value;
                var status = payment.GetProperty("status").GetString();

                if (status != "captured")
                {

                    // Image 4: Continuation of PaymentSuccess (Validations and entity checks)
                    if (status != "captured")
                    {
                        ViewBag.Status = "Processing";
                        ViewBag.PaymentId = paymentId;
                        ViewBag.OrderId = orderId;
                        return View();
                    }
                }

                // Payment captured - activate plan immediately
                var amount = payment.GetProperty("amount").GetInt32() / 100;

                // Find partner (hardcoded for now)
                var partner = await _masterDb.Tenants
                    .FirstOrDefaultAsync(p => p.CompanyName == "tejaavidi4@gmail.com");

                if (partner == null)
                {
                    ViewBag.Status = "Failed";
                    ViewBag.Error = "Partner not found";
                    return View();
                }

                // Get plan from order details in transaction table
                var orderTransaction = await _masterDb.SaasPaymentTransactions
                    .FirstOrDefaultAsync(t => t.RazorpayOrderId == orderId);

                if (orderTransaction == null)
                {
                    ViewBag.Status = "Failed";
                    ViewBag.Error = "Order transaction not found";
                    return View();
                }

                // Get plan by name from transaction
                var plan = await _masterDb.SaasPlans
                    .FirstOrDefaultAsync(p => p.PlanName == orderTransaction.PlanName);

                if (plan == null)
                {
                    ViewBag.Status = "Failed";
                    ViewBag.Error = "Plan not found";
                    return View();
                }


                // End current subscription
                var currentSub = await _masterDb.TenantSubscriptions
                    .Where(s => s.TenantId == partner.TenantId && s.Status == "Active")
                    .FirstOrDefaultAsync();

                if (currentSub != null)
                {
                    currentSub.Status = "Expired";
                    currentSub.EndDate = IndianTime.Now;
                    currentSub.UpdatedOn = IndianTime.Now;
                }

                // Create new subscription
                var newSub = new TenantSubscriptionModel
                {
                    TenantId = partner.TenantId,
                    PlanId = plan.PlanId,
                    BillingCycle = "monthly",
                    Amount = amount,
                    StartDate = IndianTime.Now,
                    EndDate = IndianTime.Now.AddMonths(1),
                    Status = "Active",
                    PaymentMethod = "Razorpay",
                    PaymentTransactionId = paymentId,
                    LastPaymentDate = IndianTime.Now,
                    NextPaymentDate = IndianTime.Now.AddMonths(1),
                    AutoRenew = false,
                    CreatedOn = IndianTime.Now,
                    UpdatedOn = IndianTime.Now
                };

                _masterDb.TenantSubscriptions.Add(newSub);
                await _masterDb.SaveChangesAsync();

                // Create transaction record
                var transaction = new SaasPaymentTransactionModel
                {
                    TenantId = partner.TenantId,

                    // Image 6: Conclusion of PaymentSuccess & CheckPartnerPlan signature
                    SubscriptionId = newSub.SubscriptionId,
                    TransactionReference = orderId,
                    RazorpayPaymentId = paymentId,
                    RazorpayOrderId = orderId,
                    Amount = amount,
                    Currency = "INR",
                    Status = "Success",
                    TransactionType = "Payment",
                    PaymentMethod = "Razorpay",
                    TransactionDate = IndianTime.Now,
                    CompletedDate = IndianTime.Now,
                    Description = $"Plan activated from payment success",
                    PlanName = plan.PlanName,
                    BillingCycle = "monthly",
                    NetAmount = amount,
                    CreatedOn = IndianTime.Now
                };

                _masterDb.SaasPaymentTransactions.Add(transaction);
                await _masterDb.SaveChangesAsync();

                ViewBag.Status = "Success";
                ViewBag.PaymentId = paymentId;
                ViewBag.OrderId = orderId;
                ViewBag.Amount = amount;
                ViewBag.PlanName = plan.PlanName;
                ViewBag.PartnerName = partner.CompanyName;
                // Send plan activation confirmation email to partner
                if (!string.IsNullOrEmpty(partner.Email) && (orderTransaction == null || !orderTransaction.TransactionType.StartsWith("Upgrade_")))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var scopeFactory = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();
                            using var emailScope = scopeFactory.CreateScope();
                            var emailService = emailScope.ServiceProvider.GetRequiredService<EmailService>();
                            var baseUrl = $"{Request.Scheme}://{Request.Host}";
                            await emailService.SendTemplateEmailAsync(
                                "PlanChangeNotification",
                                partner.Email,
                                0,
                                new Dictionary<string, string>
                                {
                                    ["PlanName"] = plan.PlanName ?? "",
                                    ["BillingCycle"] = "monthly",
                                    ["Amount"] = $"{amount:N2}",
                                    ["Name"] = partner.CompanyName ?? "",
                                    ["DashboardUrl"] = $"{baseUrl}/SaasSubscription/MyPlan",
                                    ["CompanyName"] = "PropTech CRM",
                                    ["Year"] = IndianTime.Now.Year.ToString()
                                },
                                "Subscription");
                        }
                        catch { }
                    });
                }

                return View();

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in payment success");
                ViewBag.Status = "Failed";
                ViewBag.Error = "Error processing payment";
                return View();
            }
        }
        public async Task<IActionResult> CheckPartnerPlan(int tenantId, int planId)
        {
            var partner = await _masterDb.Tenants.FindAsync(tenantId);
            var plan = await _masterDb.SaasPlans.FindAsync(planId);

            return Json(new
            {
                partnerFound = partner != null,
                partnerDetails = partner != null ? new { partner.TenantId, partner.CompanyName } : null,
                planFound = plan != null,
                planDetails = plan != null ? new { plan.PlanId, plan.PlanName, plan.MonthlyPrice } : null
            });
        }

        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> ActivateLatestPayment()
        {
            try
            {
                // Find partner by email
                var partner = await _masterDb.Tenants
                    .FirstOrDefaultAsync(p => p.CompanyName == "tejaavidi4@gmail.com");

                if (partner == null)
                {
                    return Json(new { success = false, message = "Partner not found" });
                }

                // End current subscription
                var currentSub = await _masterDb.TenantSubscriptions
                    .Where(s => s.TenantId == partner.TenantId && s.Status == "Active")
                    .FirstOrDefaultAsync();

                if (currentSub != null)
                {
                    currentSub.Status = "Expired";
                    currentSub.EndDate = IndianTime.Now;
                    currentSub.UpdatedOn = IndianTime.Now;
                }

                // Get plan
                var plan = await _masterDb.SaasPlans
                    .Where(p => p.IsActive)
                    .FirstOrDefaultAsync();

                if (plan == null)
                {
                    return Json(new { success = false, message = "No plan found" });
                }

                // Create new subscription
                var newSub = new TenantSubscriptionModel
                {
                    TenantId = partner.TenantId,
                    PlanId = plan.PlanId,
                    BillingCycle = "monthly",
                    Amount = plan.MonthlyPrice,
                    StartDate = IndianTime.Now,
                    EndDate = IndianTime.Now.AddMonths(1),
                    Status = "Active",
                    PaymentMethod = "Razorpay",
                    PaymentTransactionId = "pay_S278cyLHHT1TA",
                    LastPaymentDate = IndianTime.Now,
                    NextPaymentDate = IndianTime.Now.AddMonths(1),
                    AutoRenew = false,
                    CreatedOn = IndianTime.Now,
                    UpdatedOn = IndianTime.Now,
                };

                _masterDb.TenantSubscriptions.Add(newSub);
                await _masterDb.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"Activated {plan.PlanName} for {partner.CompanyName}"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Admin: Get referral earnings for own tenant (company head can view referral history)
        /// </summary>
        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> AdminReferrals()
        {
            var (_, role, tenantId) = GetCurrentUserContext();
            if (!tenantId.HasValue) return RedirectToAction("AccessDenied", "Home");

            var balance = await _masterDb.ReferralEarnings
                .Where(r => r.TenantId == tenantId.Value && !r.IsUsed)
                .SumAsync(r => (decimal?)r.Amount) ?? 0;

            var earnings = await _masterDb.ReferralEarnings
                .Where(r => r.TenantId == tenantId.Value && r.Type == "Referrer")
                .OrderByDescending(r => r.CreatedOn)
                .ToListAsync();

            var referredTenantIds = earnings.Where(r => r.ReferredTenantId.HasValue)
                .Select(r => r.ReferredTenantId!.Value).Distinct().ToList();
            var tenants = _masterDb.Tenants
                .Where(t => referredTenantIds.Contains(t.TenantId))
                .ToDictionary(t => t.TenantId, t => t.CompanyName ?? "Unknown");

            ViewBag.Balance = balance;
            ViewBag.Referrals = earnings;
            ViewBag.Tenants = tenants;
            ViewBag.TenantId = tenantId.Value;
            return View();
        }

        /// <summary>
        /// Admin: Get referral wallet data for own tenant (JSON for AJAX)
        /// </summary>
        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> GetAdminReferralWallet()
        {
            var (_, role, tenantId) = GetCurrentUserContext();
            if (!tenantId.HasValue)
                return Json(new { success = false, message = "Tenant not found" });

            var balance = await _masterDb.ReferralEarnings
                .Where(r => r.TenantId == tenantId.Value && !r.IsUsed)
                .SumAsync(r => (decimal?)r.Amount) ?? 0;

            var earnings = await _masterDb.ReferralEarnings
                .Where(r => r.TenantId == tenantId.Value && r.Type == "Referrer")
                .OrderByDescending(r => r.CreatedOn)
                .ToListAsync();

            var referredTenantIds = earnings.Where(r => r.ReferredTenantId.HasValue)
                .Select(r => r.ReferredTenantId!.Value).Distinct().ToList();
            var tenants = _masterDb.Tenants
                .Where(t => referredTenantIds.Contains(t.TenantId))
                .ToDictionary(t => t.TenantId, t => t.CompanyName ?? "Unknown");

            var tenant = await _masterDb.Tenants.FindAsync(tenantId.Value);

            return Json(new
            {
                success = true,
                balance = balance,
                referralCode = tenant?.Referral ?? "",
                referrals = earnings.Select(r => new
                {
                    r.Id,
                    r.Type,
                    r.Amount,
                    r.Description,
                    r.ReferralCode,
                    IsUsed = r.IsUsed,
                    JoinedCompany = r.ReferredTenantId.HasValue && tenants.ContainsKey(r.ReferredTenantId.Value)
                        ? tenants[r.ReferredTenantId.Value] : "Unknown",
                    CreatedOn = r.CreatedOn.ToString("yyyy-MM-dd")
                }).ToList()
            });
        }

        /// <summary>
        /// Admin: Manually add referral earnings for an existing member
        /// </summary>
        [HttpPost]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> AdminAddReferral(string referredCompany, decimal amount, string description)
        {
            var (_, role, tenantId) = GetCurrentUserContext();
            if (!tenantId.HasValue)
                return Json(new { success = false, message = "Tenant not found" });

            if (string.IsNullOrWhiteSpace(referredCompany))
                return Json(new { success = false, message = "Referred company name is required" });

            if (amount <= 0)
                return Json(new { success = false, message = "Amount must be greater than 0" });

            try
            {
                var tenant = await _masterDb.Tenants.FindAsync(tenantId.Value);
                if (tenant == null)
                    return Json(new { success = false, message = "Company not found" });

                var referral = new ReferralEarningModel
                {
                    TenantId = tenantId.Value,
                    ReferralCode = tenant.Referral,
                    Type = "Referrer",
                    Amount = amount,
                    Description = description ?? $"Referral bonus for referring {referredCompany}",
                    IsUsed = false,
                    ReferredTenantId = null,
                    CreatedOn = IndianTime.Now
                };

                _masterDb.ReferralEarnings.Add(referral);
                await _masterDb.SaveChangesAsync();

                _logger.LogInformation($"Admin {tenantId} added referral earning: {amount} for {referredCompany}");

                return Json(new { success = true, message = $"Referral earning of ?{amount:N2} added successfully!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding manual referral earning");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }
    }
}
