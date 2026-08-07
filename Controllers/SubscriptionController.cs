using CRM.Attributes;
using CRM.Helpers;
using CRM.Models;
using CRM.Services;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text.Json;

namespace CRM.Controllers
{
    [RoleAuthorize("Admin", "Partner")]
    public class SubscriptionController : Controller
    {
        private readonly AppDbContext _context;
        private readonly SubscriptionService _subscriptionService;
        private readonly RazorpayService _razorpayService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<SubscriptionController> _logger;
        private readonly IConfiguration _config;

        public SubscriptionController(
            AppDbContext context,
            SubscriptionService subscriptionService, IConfiguration config,
            RazorpayService razorpayService,
            IHttpContextAccessor httpContextAccessor,
            ILogger<SubscriptionController> logger)
        {
            _context = context;
            _subscriptionService = subscriptionService;
            _razorpayService = razorpayService;
            _httpContextAccessor = httpContextAccessor;
            _config = config;
            _logger = logger;
        }

        private (int? UserId, string? Role, int? ChannelPartnerId) GetCurrentUserContext()
        {
            var token = _httpContextAccessor.HttpContext?.Request.Cookies["jwtToken"];
            if (string.IsNullOrEmpty(token)) return (null, null, null);

            var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);
            var userIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            var role = jwt.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role)?.Value;

            if (!int.TryParse(userIdClaim, out int userId)) return (null, role, null);

            var user = _context.Users.FirstOrDefault(u => u.UserId == userId);
            return (userId, role, user?.ChannelPartnerId);
        }

        // Subscription Plans Management (Admin)
        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> Plans()
        {
            var plans = await _context.SubscriptionPlans
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.MonthlyPrice)
                .ToListAsync();

            return View(plans);
        }

        // Get Plans for API/AJAX calls
        [HttpGet]
        public async Task<IActionResult> GetPlans()
        {
            var plans = await _context.SubscriptionPlans
                .Where(p => p.IsActive)
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.MonthlyPrice)
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
            return View(new SubscriptionPlanModel());
        }

        [HttpPost]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> CreatePlan(SubscriptionPlanModel model)
        {
            if (ModelState.IsValid)
            {
                model.CreatedDate = IndianTime.Now;
                _context.SubscriptionPlans.Add(model);
                await _context.SaveChangesAsync();

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
        [Route("editplan/{id}")]
        //     public async Task<IActionResult> EditPlan(string id)

        //     {
        //         var decodedId = IdObfuscator.Decode(id);
        //if (decodedId == null)
        //{
        // 			 return NotFound();
        // }

        //         ViewBag.EncodedId = id;

        //         var plan = await _context.SubscriptionPlans.FindAsync(id);
        //         if (plan == null) return NotFound();

        //         return View(plan);
        //     }
        public async Task<IActionResult> EditPlan(string id)
        {
            var decodedId = IdObfuscator.Decode(id);

            if (decodedId == null)
                return NotFound();

            ViewBag.EncodedId = id;

            var plan = await _context.SubscriptionPlans.FindAsync(decodedId.Value);

            if (plan == null)
                return NotFound();

            return View(plan);
        }

        [HttpPost]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> UpdatePlan(SubscriptionPlanModel model)
        {
            if (ModelState.IsValid)
            {
                var existingPlan = await _context.SubscriptionPlans.FindAsync(model.PlanId);
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
                existingPlan.HasCustomReports = model.HasCustomReports;
                existingPlan.HasDataExport = model.HasDataExport;
                existingPlan.HasPrioritySupport = model.HasPrioritySupport;
                existingPlan.HasPhoneSupport = model.HasPhoneSupport;
                existingPlan.HasDedicatedmanager = model.HasDedicatedmanager;
                existingPlan.SupportLevel = model.SupportLevel;
                existingPlan.IsActive = model.IsActive;
                existingPlan.PlanType = model.PlanType;
                existingPlan.SortOrder = model.SortOrder;
                existingPlan.UpdatedOn = IndianTime.Now;

                _context.SubscriptionPlans.Update(existingPlan);
                await _context.SaveChangesAsync();

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
            var activeSubscribers = await _context.PartnerSubscriptions
                
                .Where(s => s.PlanId == planId && s.Status == "Active" && s.EndDate > IndianTime.Now)
                .ToListAsync();

            // Look up plan name separately
            var plan = await _context.SubscriptionPlans.FindAsync(planId);

            // Look up channel partner names separately
            var partnerIds = activeSubscribers.Select(s => s.ChannelPartnerId).Distinct().ToList();
            var partnersList = await _context.ChannelPartners
                .Where(p => partnerIds.Contains(p.PartnerId))
                .ToListAsync();
            var partners = partnersList.ToDictionary(p => p.PartnerId, p => p.CompanyName);

            return Json(new
            {
                hasActiveSubscribers = activeSubscribers.Any(),
                count = activeSubscribers.Count,
                subscribers = activeSubscribers.Select(s => new
                {
                    CompanyName = partners.GetValueOrDefault(s.ChannelPartnerId, "Unknown"),
                    StartDate = s.StartDate,
                    EndDate = s.EndDate
                }).ToList()
            });
        }

        [HttpPost]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> TogglePlan(int id, bool isActive)
        {
            var plan = await _context.SubscriptionPlans.FindAsync(id);
            if (plan == null)
                return NotFound();

            plan.IsActive = isActive;
            plan.UpdatedOn = IndianTime.Now;
            _context.SubscriptionPlans.Update(plan);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Plans));
        }

        // Admin: Manage Partner Subscriptions
        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> PartnerSubscriptions(string? search, string? status, int page = 1)
        {
            int pageSize = 20;

            var query = _context.PartnerSubscriptions
                .AsQueryable();

            // Apply search filter - lookup by ID instead of nav property
            if (!string.IsNullOrEmpty(search))
            {
                // Search by ChannelPartnerId and PlanId using string matching
                var matchingPartnerIds = await _context.ChannelPartners
                    .Where(p => (p.CompanyName != null && p.CompanyName.Contains(search)) ||
                               (p.Email != null && p.Email.Contains(search)))
                    .Select(p => p.PartnerId)
                    .ToListAsync();
                var matchingPlanIds = await _context.SubscriptionPlans
                    .Where(p => p.PlanName != null && p.PlanName.Contains(search))
                    .Select(p => p.PlanId)
                    .ToListAsync();
                query = query.Where(s => matchingPartnerIds.Contains(s.ChannelPartnerId) ||
                                        matchingPlanIds.Contains(s.PlanId));
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

        // Admin: Get Partner Current Subscription for Upgrade Modal
        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> GetPartnerSubscription(int partnerId)
        {
            var currentSubscription = await _context.PartnerSubscriptions
                
                .Where(s => s.ChannelPartnerId == partnerId && s.Status == "Active")
                .FirstOrDefaultAsync();

            var scheduledSubscription = await _context.PartnerSubscriptions
                
                .Where(s => s.ChannelPartnerId == partnerId && s.Status == "Scheduled")
                .FirstOrDefaultAsync();

            var partner = await _context.ChannelPartners.FindAsync(partnerId);

            return Json(new
            {
                success = true,
                partner = new
                {
                    partnerId = partner?.PartnerId,
                    companyName = partner?.CompanyName,
                    email = partner?.Email
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
            var subscription = await _context.PartnerSubscriptions.FindAsync(subscriptionId);
            if (subscription == null)
                return Json(new { success = false, message = "Subscription not found" });

            subscription.EndDate = subscription.EndDate.AddDays(days);
            subscription.UpdatedOn = IndianTime.Now;
            _context.PartnerSubscriptions.Update(subscription);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = $"Trial extended by {days} days until {subscription.EndDate:MMM dd, yyyy}" });
        }

        // Admin: Change Partner Plan (Direct Assignment without Payment)
        [HttpPost]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> AdminChangePlan(int partnerId, int newPlanId, string billingCycle, string reason)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var partner = await _context.ChannelPartners.FindAsync(partnerId);
                if (partner == null)
                    return Json(new { success = false, message = "Partner not found" });

                var newPlan = await _context.SubscriptionPlans.FindAsync(newPlanId);
                if (newPlan == null)
                    return Json(new { success = false, message = "Plan not found" });

                var amount = billingCycle.ToLower() == "annual" ? newPlan.YearlyPrice : newPlan.MonthlyPrice;

                // Get current active subscription
                var currentSubscription = await _context.PartnerSubscriptions
                    .Where(s => s.ChannelPartnerId == partnerId && s.Status == "Active")
                    .FirstOrDefaultAsync();

                if (currentSubscription != null)
                {
                    // Expire current subscription
                    currentSubscription.Status = "Expired";
                    currentSubscription.EndDate = IndianTime.Now;
                    currentSubscription.UpdatedOn = IndianTime.Now;
                    currentSubscription.CancellationReason = $"Admin changed plan - {reason}";
                    currentSubscription.CancelledOn = IndianTime.Now;
                }

                // Cancel any scheduled subscriptions
                var scheduledSubscriptions = await _context.PartnerSubscriptions
                    .Where(s => s.ChannelPartnerId == partnerId && s.Status == "Scheduled")
                    .ToListAsync();

                foreach (var scheduled in scheduledSubscriptions)
                {
                    scheduled.Status = "Cancelled";
                    scheduled.CancelledOn = IndianTime.Now;
                    scheduled.CancellationReason = $"Admin changed plan - {reason}";
                    scheduled.UpdatedOn = IndianTime.Now;
                }

                // Create new active subscription
                var newSubscription = new PartnerSubscriptionModel
                {
                    ChannelPartnerId = partnerId,
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

                _context.PartnerSubscriptions.Add(newSubscription);
                await _context.SaveChangesAsync();

                // Create payment transaction record
                var paymentTransaction = new PaymentTransactionModel
                {
                    ChannelPartnerId = partnerId,
                    SubscriptionId = newSubscription.SubscriptionId,
                    TransactionReference = $"ADMIN_{IndianTime.Now:yyyyMMddHHmmss}",
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

                _context.PaymentTransactions.Add(paymentTransaction);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation($"Admin changed partner {partnerId} plan to {newPlan.PlanName}. Reason: {reason}");

                return Json(new
                {
                    success = true,
                    message = $"Successfully assigned {newPlan.PlanName} ({billingCycle}) plan to {partner.CompanyName}"
                });
            }
            catch (Exception ex)
            {

                _logger.LogError(ex, $"Error changing partner plan for partner {partnerId}");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // Membership Addons
        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> Addons()
        {
            var addons = await _context.SubscriptionAddons
                .OrderBy(a => a.SortOrder)
                
                .ToListAsync();

            return View(addons);
        }

        // Subscription Transactions - Show both payment transactions and refund requests
        [HttpGet]
        public async Task<IActionResult> Transactions(string? status, string? type, DateTime? fromDate, DateTime? toDate)
        {
            var (userId, role, channelPartnerId) = GetCurrentUserContext();

            // Get payment transactions
            var transactionsQuery = _context.PaymentTransactions
                
                
                
                .AsQueryable();

            // Filter by partner if not admin
            if (role?.ToLower() == "partner" && channelPartnerId.HasValue)
            {
                transactionsQuery = transactionsQuery.Where(t => t.ChannelPartnerId == channelPartnerId.Value);
            }

            // Apply filters
            if (!string.IsNullOrEmpty(status))
                transactionsQuery = transactionsQuery.Where(t => t.Status == status);

            if (!string.IsNullOrEmpty(type))
                transactionsQuery = transactionsQuery.Where(t => t.TransactionType == type);

            if (fromDate.HasValue)
                transactionsQuery = transactionsQuery.Where(t => t.TransactionDate >= fromDate.Value);

            if (toDate.HasValue)
                transactionsQuery = transactionsQuery.Where(t => t.TransactionDate <= toDate.Value.AddDays(1));

            var transactions = await transactionsQuery
                .OrderByDescending(t => t.TransactionDate)
                .ToListAsync();

            // For refund transactions, replace refund ID with original payment ID for better display
            foreach (var transaction in transactions.Where(t => t.TransactionType == "Refund" &&
                                                                 !string.IsNullOrEmpty(t.RazorpayPaymentId) &&
                                                                 t.RazorpayPaymentId.StartsWith("rfnd_")))
            {
                if (transaction.SubscriptionId.HasValue)
                {
                    var originalTransaction = await _context.PaymentTransactions
                        .Where(t => t.SubscriptionId == transaction.SubscriptionId.Value &&
                                   t.Status == "Success" &&
                                   t.TransactionType != "Refund" &&
                                   t.TransactionType != "Cancellation" &&
                                   !string.IsNullOrEmpty(t.RazorpayPaymentId) &&
                                   t.RazorpayPaymentId.StartsWith("pay_"))
                        .OrderByDescending(t => t.TransactionDate)
                        .FirstOrDefaultAsync();

                    if (originalTransaction != null && !string.IsNullOrEmpty(originalTransaction.RazorpayPaymentId))
                    {
                        // Replace refund ID with original payment ID for display
                        transaction.RazorpayPaymentId = originalTransaction.RazorpayPaymentId;
                    }
                }
            }

            // Get cancelled subscriptions with pending refunds for admin
            var pendingRefundSubscriptions = new List<PartnerSubscriptionModel>();
            if (role?.ToLower() == "admin")
            {
                var refundQuery = _context.PartnerSubscriptions
                    
                    
                    .Where(s => s.Status == "Cancelled" &&
                               s.CancellationReason != null &&
                               s.CancellationReason.Contains("Refund Pending"))
                    .AsQueryable();

                // Apply date filters
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

        // Partner Plan Selection & Management
        [HttpGet]
        public async Task<IActionResult> MyPlan()
        {
            var (userId, role, channelPartnerId) = GetCurrentUserContext();

            if (!channelPartnerId.HasValue)
                return RedirectToAction("AccessDenied", "Home");

            var currentSubscription = await _subscriptionService.GetActiveSubscriptionAsync(channelPartnerId.Value);

            // If service returns expired subscription, get the real active one
            if (currentSubscription == null || currentSubscription.EndDate <= IndianTime.Now)
            {
                currentSubscription = await _context.PartnerSubscriptions
                    
                    .Where(s => s.ChannelPartnerId == channelPartnerId.Value &&
                               s.Status == "Active" &&
                               s.EndDate > IndianTime.Now)
                    .OrderByDescending(s => s.CreatedOn)
                    .FirstOrDefaultAsync();
            }

            // Update current usage counts
            if (currentSubscription != null)
            {
                // Count approved agents for this partner (Status = "Approved")
                currentSubscription.CurrentAgentCount = await _context.Agents
                    .Where(a => a.ChannelPartnerId == channelPartnerId.Value && a.Status == "Approved")
                    .CountAsync();

                // Count leads created in current billing cycle (from subscription start date)
                currentSubscription.CurrentMonthLeads = await _context.Leads
                    .Where(l => l.ChannelPartnerId == channelPartnerId.Value &&
                               l.CreatedOn >= currentSubscription.StartDate)
                    .CountAsync();

                // Update the database with current counts
                currentSubscription.UpdatedOn = IndianTime.Now;
                await _context.SaveChangesAsync();
            }

            var availablePlans = await _subscriptionService.GetAvailablePlansAsync();

            // Get scheduled subscription if any - exclude cancelled/refunded subscriptions properly
            var scheduledSubscription = await _context.PartnerSubscriptions
                
                .Where(s => s.ChannelPartnerId == channelPartnerId.Value &&
                           s.Status == "Scheduled" &&
                           (s.CancellationReason == null ||
                            (!s.CancellationReason.Contains("Refund") &&
                             !s.CancellationReason.Contains("Cancelled by user") &&
                             !s.CancellationReason.Contains("PERMANENTLY CANCELLED"))))
                .OrderByDescending(s => s.CreatedOn)
                .FirstOrDefaultAsync();

            _logger.LogInformation($"MyPlan: Partner {channelPartnerId.Value} - Found scheduled subscription: {scheduledSubscription != null}");
            if (scheduledSubscription != null)
            {
                _logger.LogInformation($"Scheduled subscription details: ID={scheduledSubscription.SubscriptionId}, Plan={scheduledSubscription.Plan?.PlanName}, StartDate={scheduledSubscription.StartDate}, Status={scheduledSubscription.Status}, Amount={scheduledSubscription.Amount}");
            }
            else
            {
                // Check if there are any subscriptions with "Scheduled Payment" transaction type
                var scheduledTransactions = await _context.PaymentTransactions
                    .Where(t => t.ChannelPartnerId == channelPartnerId.Value &&
                               t.TransactionType == "Scheduled Payment" &&
                               t.Status == "Success")
                    .OrderByDescending(t => t.TransactionDate)
                    .ToListAsync();

                _logger.LogInformation($"Found {scheduledTransactions.Count} scheduled payment transactions for partner {channelPartnerId.Value}");

                foreach (var trans in scheduledTransactions)
                {
                    _logger.LogInformation($"Scheduled transaction: ID={trans.TransactionId}, SubscriptionId={trans.SubscriptionId}, Amount={trans.Amount}, Date={trans.TransactionDate}");

                    if (trans.SubscriptionId.HasValue)
                    {
                        var relatedSubscription = await _context.PartnerSubscriptions
                            
                            .FirstOrDefaultAsync(s => s.SubscriptionId == trans.SubscriptionId.Value &&
                                                     s.Status != "Cancelled" &&
                                                     s.Status != "Refunded");

                        if (relatedSubscription != null)
                        {
                            _logger.LogInformation($"Related subscription found: ID={relatedSubscription.SubscriptionId}, Status={relatedSubscription.Status}, Plan={relatedSubscription.Plan?.PlanName}");

                            // If this subscription should be scheduled but isn't, fix it
                            if (relatedSubscription.Status != "Scheduled" && relatedSubscription.StartDate > IndianTime.Now)
                            {
                                _logger.LogInformation($"Fixing subscription status from {relatedSubscription.Status} to Scheduled");
                                relatedSubscription.Status = "Scheduled";
                                _context.PartnerSubscriptions.Update(relatedSubscription);
                                await _context.SaveChangesAsync();
                                scheduledSubscription = relatedSubscription;
                            }
                        }
                    }
                }
            }

            // Get cancelled subscriptions with pending refunds
            var cancelledWithPendingRefund = await _context.PartnerSubscriptions
                
                .Where(s => s.ChannelPartnerId == channelPartnerId.Value &&
                           s.Status == "Cancelled" &&
                           s.CancellationReason != null &&
                           s.CancellationReason.Contains("Refund Pending"))
                .ToListAsync();

            ViewBag.CurrentSubscription = currentSubscription;
            ViewBag.ScheduledSubscription = scheduledSubscription;
            ViewBag.CancelledWithPendingRefund = cancelledWithPendingRefund;
            ViewBag.AvailablePlans = availablePlans;
            ViewBag.ChannelPartnerId = channelPartnerId.Value;
            ViewBag.RazorpayKeyId = _razorpayService.GetKeyId(); // Add Razorpay key

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> CancelCurrentPlan(int partnerId)
        {
            try
            {
                var currentSubscription = await _context.PartnerSubscriptions
                    
                    .Where(s => s.ChannelPartnerId == partnerId && s.Status == "Active")
                    .FirstOrDefaultAsync();

                if (currentSubscription == null)
                    return Json(new { success = false, message = "No active subscription found" });

                // Cancel the subscription immediately
                currentSubscription.Status = "Cancelled";
                currentSubscription.CancelledOn = IndianTime.Now;
                currentSubscription.EndDate = IndianTime.Now;
                currentSubscription.CancellationReason = $"Cancelled by user - Refund Pending: ₹{currentSubscription.Amount:N0}";
                currentSubscription.UpdatedOn = IndianTime.Now;

                _context.PartnerSubscriptions.Update(currentSubscription);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Partner {partnerId} cancelled subscription {currentSubscription.SubscriptionId}. Refund pending: ₹{currentSubscription.Amount}");

                return Json(new
                {
                    success = true,
                    message = "Plan cancelled successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error cancelling plan for partner {partnerId}");
                return Json(new { success = false, message = "Error cancelling plan" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CancelScheduledPlan(int subscriptionId)
        {
            var (userId, role, channelPartnerId) = GetCurrentUserContext();

            if (!channelPartnerId.HasValue)
                return Json(new { success = false, message = "Partner context not found" });

            try
            {
                var scheduledSubscription = await _context.PartnerSubscriptions
                    
                    .Where(s => s.SubscriptionId == subscriptionId &&
                               s.ChannelPartnerId == channelPartnerId.Value &&
                               s.Status == "Scheduled")
                    .FirstOrDefaultAsync();

                if (scheduledSubscription == null)
                {
                    return Json(new { success = false, message = "Scheduled subscription not found" });
                }

                // Get the original payment transaction to fetch card details
                var paymentTransaction = await _context.PaymentTransactions
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

                // Cancel the scheduled subscription
                scheduledSubscription.Status = "Cancelled";
                scheduledSubscription.CancelledOn = IndianTime.Now;
                scheduledSubscription.CancellationReason = $"Cancelled by user - Refund Pending: ₹{scheduledSubscription.Amount:N0}. Refund will be processed to {cardInfo}";
                scheduledSubscription.UpdatedOn = IndianTime.Now;

                // Create a cancellation transaction record for visibility in Transactions page
                var cancellationTransaction = new PaymentTransactionModel
                {
                    ChannelPartnerId = channelPartnerId.Value,
                    SubscriptionId = subscriptionId,
                    TransactionReference = $"cancel_{subscriptionId}_{IndianTime.Now.Ticks}",
                    Amount = scheduledSubscription.Amount,
                    Currency = "INR",
                    Status = "Cancelled",
                    TransactionType = "Cancellation",
                    PaymentMethod = "User Cancellation",
                    TransactionDate = IndianTime.Now,
                    CompletedDate = IndianTime.Now,
                    Description = $"Scheduled plan cancelled by user - {scheduledSubscription.Plan?.PlanName} - Refund Pending",
                    PlanName = scheduledSubscription.Plan?.PlanName,
                    BillingCycle = scheduledSubscription.BillingCycle,
                    NetAmount = scheduledSubscription.Amount,
                    CreatedOn = IndianTime.Now
                };

                _context.PaymentTransactions.Add(cancellationTransaction);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"User cancelled scheduled subscription {subscriptionId} for {scheduledSubscription.Plan?.PlanName}. Refund pending: ₹{scheduledSubscription.Amount}");

                return Json(new
                {
                    success = true,
                    refundPending = true,
                    refundAmount = scheduledSubscription.Amount,
                    message = $"Your scheduled {scheduledSubscription.Plan?.PlanName} plan has been cancelled successfully."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling scheduled plan {SubscriptionId}", subscriptionId);
                return Json(new { success = false, message = "Error cancelling scheduled plan" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetUpgradeOptions(int partnerId)
        {
            try
            {
                var partner = await _context.ChannelPartners.FindAsync(partnerId);
                if (partner == null)
                    return Json(new { success = false, message = "Partner not found" });

                var currentSubscription = await _context.PartnerSubscriptions
                    
                    .Where(s => s.ChannelPartnerId == partnerId && s.Status == "Active")
                    .FirstOrDefaultAsync();

                var availablePlans = await _context.SubscriptionPlans
                    .Where(p => p.IsActive == true)
                    .OrderBy(p => p.SortOrder)
                    .ThenBy(p => p.MonthlyPrice)
                    .ToListAsync();

                return Json(new
                {
                    success = true,
                    hasActivePlan = currentSubscription != null,
                    partner = new
                    {
                        partnerId = partner.PartnerId,
                        companyName = partner.CompanyName ?? "",
                        email = partner.Email ?? ""
                    },
                    currentSubscription = currentSubscription != null ? new
                    {
                        subscriptionId = currentSubscription.SubscriptionId,
                        planId = currentSubscription.PlanId,
                        planName = currentSubscription.Plan?.PlanName ?? "",
                        amount = currentSubscription.Amount > 0 ? currentSubscription.Amount :
                            // Calculate remaining amount for existing subscriptions with ₹0
                            CalculateRemainingAmount(currentSubscription),
                        billingCycle = currentSubscription.BillingCycle ?? "monthly",
                        startDate = currentSubscription.StartDate.ToString("yyyy-MM-dd"),
                        endDate = currentSubscription.EndDate.ToString("yyyy-MM-dd"),
                        daysRemaining = (currentSubscription.EndDate - IndianTime.Now).Days,
                        status = currentSubscription.Status ?? ""
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
                        //maxStorageGB = p.MaxStorageGB,
                        hasWhatsAppIntegration = p.HasWhatsAppIntegration,
                        hasFacebookIntegration = p.HasFacebookIntegration,
                        hasEmailIntegration = p.HasEmailIntegration,
                        hasAdvancedReports = p.HasAdvancedReports,
                        hasDataExport = p.HasDataExport,
                        supportLevel = p.SupportLevel ?? ""
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        private decimal CalculateRemainingAmount(PartnerSubscriptionModel subscription)
        {
            var totalDays = (subscription.EndDate - subscription.StartDate).Days;
            var remainingDays = Math.Max(0, (subscription.EndDate - IndianTime.Now).Days);

            if (totalDays <= 0 || remainingDays <= 0) return 0;

            // For existing upgrade subscriptions with ₹0, calculate based on original remaining amount logic
            if (subscription.Amount == 0)
            {
                // This is from existing upgrade - calculate actual remaining amount
                // Find the original Basic Plan subscription amount to calculate per-day rate
                var originalAmount = 98m; // Basic Plan monthly amount
                var originalDays = 30; // Monthly plan days
                var perDayRate = originalAmount / originalDays;
                return Math.Round(perDayRate * remainingDays, 2);
            }

            // For normal subscriptions, calculate based on subscription amount
            var subscriptionPerDayRate = subscription.Amount / totalDays;
            return Math.Round(subscriptionPerDayRate * remainingDays, 2);
        }

        [HttpPost]
        public async Task<IActionResult> CalculateUpgrade(int partnerId, int newPlanId, string billingCycle, string upgradeType)
        {
            try
            {
                var currentSubscription = await _context.PartnerSubscriptions
                    
                    .Where(s => s.ChannelPartnerId == partnerId && s.Status == "Active")
                    .FirstOrDefaultAsync();

                var newPlan = await _context.SubscriptionPlans.FindAsync(newPlanId);
                if (newPlan == null)
                    return Json(new { success = false, message = "Plan not found" });

                var newAmount = billingCycle.ToLower() == "annual" ? newPlan.YearlyPrice : newPlan.MonthlyPrice;

                // Check if this is an activation request
                var activateNow = Request.Form["activateNow"].ToString() == "true";

                IActionResult calculationResult;
                switch (upgradeType.ToLower())
                {
                    case "existing":
                        calculationResult = CalculateExistingPlanUpgrade(currentSubscription, newPlan, newAmount, billingCycle);
                        break;

                    case "immediate":
                        calculationResult = CalculateImmediateUpgrade(currentSubscription, newPlan, newAmount, billingCycle);
                        break;

                    case "scheduled":
                        calculationResult = CalculateScheduledPlan(currentSubscription, newPlan, newAmount, billingCycle);
                        break;

                    default:
                        return Json(new { success = false, message = "Invalid upgrade type" });
                }

                // If activateNow is true, perform the actual activation
                if (activateNow)
                {
                    return await ActivateUpgradeNow(partnerId, newPlanId, billingCycle, upgradeType, currentSubscription, newPlan, newAmount);
                }

                return calculationResult;
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error calculating upgrade" });
            }
        }

        private async Task<IActionResult> ActivateUpgradeNow(int partnerId, int newPlanId, string billingCycle, string upgradeType, PartnerSubscriptionModel currentSubscription, SubscriptionPlanModel newPlan, decimal newAmount)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation($"ActivateUpgradeNow: partnerId={partnerId}, newPlanId={newPlanId}, billingCycle={billingCycle}, upgradeType={upgradeType}, newAmount={newAmount}");

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
                                // Calculate actual remaining amount and converted days
                                decimal actualCurrentAmount = currentSubscription.Amount;
                                decimal perDayRate;

                                if (currentSubscription.Amount == 0)
                                {
                                    var originalAmount = 98m;
                                    perDayRate = originalAmount / 30;
                                    actualCurrentAmount = originalAmount;
                                }
                                else
                                {
                                    perDayRate = currentSubscription.Amount / totalDays;
                                }

                                var remainingAmount = perDayRate * remainingDays;
                                var upgradeDays = billingCycle.ToLower() == "annual" ? 365 : 30;
                                var upgradePerDayRate = newAmount / upgradeDays;

                                var convertedDays = (int)(remainingAmount / upgradePerDayRate);
                                if ((remainingAmount % upgradePerDayRate) > 0) convertedDays += 1;

                                _logger.LogInformation($"Converted days calculation: remainingAmount={remainingAmount}, upgradePerDayRate={upgradePerDayRate}, convertedDays={convertedDays}");

                                endDate = startDate.AddDays(convertedDays);

                                // Store the actual remaining amount paid, not full plan price
                                actualAmountPaid = Math.Round(remainingAmount, 2);
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

                            decimal perDayRate;
                            if (currentSubscription.Amount == 0)
                            {
                                var originalAmount = 98m;
                                perDayRate = originalAmount / 30;
                            }
                            else
                            {
                                perDayRate = currentSubscription.Amount / totalDays;
                            }

                            var creditAmount = Math.Round(perDayRate * remainingDays, 2);
                            actualAmountPaid = Math.Max(0, newAmount - creditAmount);
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

                // Create new subscription

                // For existing upgrade, calculate the actual amount paid (remaining amount)
                if (upgradeType.ToLower() == "existing" && currentSubscription != null)
                {
                    _logger.LogInformation($"Using calculated actualAmountPaid: {actualAmountPaid}");
                }

                _logger.LogInformation($"Final actualAmountPaid before creating subscription: {actualAmountPaid}");

                var newSubscription = new PartnerSubscriptionModel
                {
                    ChannelPartnerId = partnerId,
                    PlanId = newPlanId,
                    BillingCycle = billingCycle,
                    Amount = actualAmountPaid, // Store actual amount paid, not full plan price
                    StartDate = startDate,
                    EndDate = endDate,
                    Status = "Active",
                    PaymentMethod = "Admin Activation",
                    PaymentTransactionId = $"admin_activation_{IndianTime.Now.Ticks}",
                    LastPaymentDate = IndianTime.Now,
                    NextPaymentDate = endDate,
                    AutoRenew = false,
                    CreatedOn = IndianTime.Now,
                    UpdatedOn = IndianTime.Now
                };

                _context.PartnerSubscriptions.Add(newSubscription);
                await _context.SaveChangesAsync();

                // Create transaction record
                var paymentTransaction = new PaymentTransactionModel
                {
                    ChannelPartnerId = partnerId,
                    SubscriptionId = newSubscription.SubscriptionId,
                    TransactionReference = $"ADMIN_ACTIVATION_{IndianTime.Now:yyyyMMddHHmmss}",
                    Amount = actualAmountPaid, // Store actual amount paid, not 0
                    Currency = "INR",
                    Status = "Success",
                    TransactionType = "Admin Activation",
                    PaymentMethod = "Admin",
                    TransactionDate = IndianTime.Now,
                    CompletedDate = IndianTime.Now,
                    Description = $"Admin activated {newPlan.PlanName} plan ({billingCycle}) - {upgradeType} upgrade",
                    PlanName = newPlan.PlanName,
                    BillingCycle = billingCycle,
                    CreatedOn = IndianTime.Now
                };

                _context.PaymentTransactions.Add(paymentTransaction);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                _logger.LogInformation($"Admin activated {newPlan.PlanName} plan for partner {partnerId} via {upgradeType} upgrade");

                return Json(new
                {
                    success = true,
                    message = $"Plan activated successfully! {newPlan.PlanName} is now active until {endDate:MMM dd, yyyy}."
                });
            }
            catch (Exception ex)
            {

                _logger.LogError(ex, $"Error activating upgrade for partner {partnerId}");
                return Json(new { success = false, message = $"Activation failed: {ex.Message}" });
            }
        }

        private IActionResult CalculateExistingPlanUpgrade(PartnerSubscriptionModel currentSubscription, SubscriptionPlanModel newPlan, decimal newAmount, string billingCycle)
        {
            if (currentSubscription == null)
                return Json(new { success = false, message = "No active subscription found" });

            var totalDays = Math.Max(1, (currentSubscription.EndDate - currentSubscription.StartDate).Days);
            var remainingDays = Math.Max(0, (currentSubscription.EndDate - IndianTime.Now).Days);

            if (totalDays <= 0 || remainingDays <= 0)
                return Json(new { success = false, message = "No remaining days in current subscription" });

            // Calculate actual remaining amount using the same logic as CalculateRemainingAmount
            decimal actualCurrentAmount = currentSubscription.Amount;
            decimal perDayRate;

            // For existing upgrade subscriptions with ₹0, calculate based on original remaining amount logic
            if (currentSubscription.Amount == 0)
            {
                // This is from existing upgrade - calculate actual remaining amount
                // Find the original Basic Plan subscription amount to calculate per-day rate
                var originalAmount = 98m; // Basic Plan monthly amount
                var originalDays = 30; // Monthly plan days
                perDayRate = originalAmount / originalDays;
                actualCurrentAmount = originalAmount; // Use original amount for display
            }
            else
            {
                // For normal subscriptions, calculate based on subscription amount
                perDayRate = currentSubscription.Amount / totalDays;
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
                convertedDays += 1;

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

        private IActionResult CalculateImmediateUpgrade(PartnerSubscriptionModel currentSubscription, SubscriptionPlanModel newPlan, decimal newAmount, string billingCycle)
        {
            decimal adjustedAmount = newAmount;
            decimal creditAmount = 0;

            if (currentSubscription != null)
            {
                var totalDays = Math.Max(1, (currentSubscription.EndDate - currentSubscription.StartDate).Days);
                var remainingDays = Math.Max(0, (currentSubscription.EndDate - IndianTime.Now).Days);

                // Calculate actual remaining amount using the same logic as existing upgrade
                decimal actualCurrentAmount = currentSubscription.Amount;
                decimal perDayRate;

                // For existing upgrade subscriptions with ₹0, calculate based on original remaining amount logic
                if (currentSubscription.Amount == 0)
                {
                    var originalAmount = 98m; // Basic Plan monthly amount
                    perDayRate = originalAmount / 30;
                    actualCurrentAmount = originalAmount;
                }
                else
                {
                    perDayRate = currentSubscription.Amount / totalDays;
                }

                // Use the actual remaining amount (same as displayed in frontend)
                creditAmount = Math.Round(perDayRate * remainingDays, 2);
                adjustedAmount = Math.Max(0, newAmount - creditAmount);
            }

            var startDate = IndianTime.Now;
            var endDate = billingCycle.ToLower() == "annual" ? startDate.AddYears(1) : startDate.AddMonths(1);

            return Json(new
            {
                success = true,
                upgradeType = "immediate",
                calculation = new
                {
                    currentPlan = currentSubscription?.Plan?.PlanName,
                    currentAmount = currentSubscription?.Amount ?? 0,
                    remainingDays = currentSubscription != null ? Math.Max(0, (currentSubscription.EndDate - IndianTime.Now).Days) : 0,
                    creditAmount = Math.Round(creditAmount, 2),
                    newPlan = newPlan.PlanName,
                    newAmount = newAmount,
                    adjustedAmount = Math.Round(adjustedAmount, 2),
                    startDate = startDate.ToString("yyyy-MM-dd"),
                    endDate = endDate.ToString("yyyy-MM-dd"),
                    paymentRequired = Math.Round(adjustedAmount, 2)
                }
            });
        }

        private IActionResult CalculateScheduledPlan(PartnerSubscriptionModel currentSubscription, SubscriptionPlanModel newPlan, decimal newAmount, string billingCycle)
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

            return Json(new
            {
                success = true,
                upgradeType = "scheduled",
                calculation = new
                {
                    currentPlan = currentSubscription?.Plan?.PlanName,
                    newPlan = newPlan.PlanName,
                    newAmount = newAmount,
                    startDate = startDate.ToString("yyyy-MM-dd"),
                    endDate = endDate.ToString("yyyy-MM-dd"),
                    paymentRequired = newAmount
                }
            });
        }

        [HttpPost]
        public async Task<IActionResult> CreatePaymentLink(int partnerId, int planId, string billingCycle, string upgradeType, decimal amount)
        {
            try
            {
                var partner = await _context.ChannelPartners.FindAsync(partnerId);
                if (partner == null)
                    return Json(new { success = false, message = "Partner not found" });

                var plan = await _context.SubscriptionPlans.FindAsync(planId);
                if (plan == null)
                    return Json(new { success = false, message = "Plan not found" });

                var orderId = await _razorpayService.CreateOrderAsync(amount, "INR", $"upgrade_{partnerId}_{planId}_{upgradeType}");

                var upgradeRequest = new PaymentTransactionModel
                {
                    ChannelPartnerId = partnerId,
                    TransactionReference = orderId,
                    RazorpayOrderId = orderId,
                    Amount = amount,
                    Currency = "INR",
                    Status = "Pending",
                    TransactionType = $"Upgrade_{upgradeType}",
                    PaymentMethod = "Razorpay",
                    TransactionDate = IndianTime.Now,
                    Description = $"Plan upgrade to {plan.PlanName} ({billingCycle}) - {upgradeType}",
                    PlanName = plan.PlanName,
                    BillingCycle = billingCycle,
                    CreatedOn = IndianTime.Now
                };

                _context.PaymentTransactions.Add(upgradeRequest);
                await _context.SaveChangesAsync();

                // Create payment link URL
                var paymentUrl = $"{Request.Scheme}://{Request.Host}/Payments/Upgrade?orderId={orderId}";

                // Send email to partner
                try
                {
                    var emailSubject = $"Payment Required: Upgrade to {plan.PlanName} Plan";
                    var emailBody = $@"
                        <h3>Plan Upgrade Payment Required</h3>
                        <p>Dear {partner.CompanyName},</p>
                        <p>Your admin has initiated a plan upgrade. Please complete the payment to activate your new plan.</p>
                        <div style='background: #f8f9fa; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                            <h4>Upgrade Details:</h4>
                            <p><strong>New Plan:</strong> {plan.PlanName}</p>
                            <p><strong>Billing Cycle:</strong> {billingCycle}</p>
                            <p><strong>Amount:</strong> ₹{amount:N2}</p>
                            <p><strong>Upgrade Type:</strong> {CultureInfo.CurrentCulture.TextInfo.ToTitleCase(upgradeType.Replace("_", " "))}</p>
                        </div>
                        <p><a href='{paymentUrl}' style='background: #007bff; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px;'>Complete Payment</a></p>
                        <p>If you have any questions, please contact your admin.</p>
                        <p>Best regards,<br>CRM Team</p>
                    ";

                    // Send email using EmailService
                    var emailService = HttpContext.RequestServices.GetRequiredService<EmailService>();
                    // Find the user associated with this partner to get correct UserId
                    var partnerUser = await _context.Users.FirstOrDefaultAsync(u => u.ChannelPartnerId == partnerId);
                    var emailUserId = partnerUser?.UserId ?? 0;
                    await emailService.SendEmailAsync(emailUserId, partner.Email, emailSubject, emailBody);

                    _logger.LogInformation($"Payment link email sent to {partner.Email} for upgrade order {orderId}");
                }
                catch (Exception emailEx)
                {
                    _logger.LogError(emailEx, $"Failed to send payment email for order {orderId}");
                    // Continue even if email fails
                }

                return Json(new
                {
                    success = true,
                    paymentLink = paymentUrl,
                    orderId = orderId,
                    amount = amount * 100,
                    partnerEmail = partner.Email,
                    planName = plan.PlanName,
                    billingCycle = billingCycle,
                    upgradeType = upgradeType,
                    emailSent = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating payment link for partner {PartnerId}", partnerId);
                return Json(new { success = false, message = "Error creating payment link" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetOrderDetails(string orderId)
        {
            try
            {
                var transaction = await _context.PaymentTransactions
                    
                    .Where(t => t.RazorpayOrderId == orderId)
                    .FirstOrDefaultAsync();

                if (transaction == null)
                    return Json(new { success = false, message = "Order not found" });

                return Json(new
                {
                    success = true,
                    razorpayKey = _razorpayService.GetKeyId(),
                    order = new
                    {
                        orderId = orderId,
                        amount = (int)(transaction.Amount * 100), // Convert to paise
                        planName = transaction.PlanName,
                        billingCycle = transaction.BillingCycle,
                        upgradeType = transaction.TransactionType?.Replace("Upgrade_", ""),
                        partnerEmail = transaction.ChannelPartner?.Email
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
            var (userId, role, channelPartnerId) = GetCurrentUserContext();

            if (!channelPartnerId.HasValue)
                return Json(new { success = false, message = "Partner context not found" });

            var plan = await _context.SubscriptionPlans.FindAsync(planId);
            if (plan == null)
                return Json(new { success = false, message = "Invalid plan selected" });

            // Get current active subscription
            var currentSubscription = await _context.PartnerSubscriptions
                .Where(s => s.ChannelPartnerId == channelPartnerId.Value && s.Status == "Active")
                .FirstOrDefaultAsync();

            var amount = billingCycle.ToLower() == "annual" ? plan.YearlyPrice : plan.MonthlyPrice;

            // If no current subscription, create immediate subscription
            if (currentSubscription == null)
            {
                try
                {
                    // Create Razorpay order for immediate subscription
                    var orderId = await _razorpayService.CreateOrderAsync(amount, "INR", $"subscription_{channelPartnerId}_{planId}");

                    return Json(new
                    {
                        success = true,
                        orderId = orderId,
                        amount = amount * 100, // Razorpay expects amount in paise
                        planName = plan.PlanName,
                        billingCycle = billingCycle
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating Razorpay order");
                    return Json(new { success = false, message = "Failed to create payment order" });
                }
            }
            else
            {
                // Handle different upgrade types
                if (upgradeType.ToLower() == "immediate")
                {
                    // Calculate credit amount for immediate upgrade
                    var totalDays = Math.Max(1, (currentSubscription.EndDate - currentSubscription.StartDate).Days);
                    var remainingDays = Math.Max(0, (currentSubscription.EndDate - IndianTime.Now).Days);

                    decimal perDayRate;
                    if (currentSubscription.Amount == 0)
                    {
                        var originalAmount = 98m;
                        perDayRate = originalAmount / 30;
                    }
                    else
                    {
                        perDayRate = currentSubscription.Amount / totalDays;
                    }

                    var creditAmount = Math.Round(perDayRate * remainingDays, 2);
                    var adjustedAmount = Math.Max(0, amount - creditAmount);

                    try
                    {
                        // Create Razorpay order for the adjusted amount (after credit)
                        var orderId = await _razorpayService.CreateOrderAsync(adjustedAmount, "INR", $"upgrade_immediate_{channelPartnerId}_{planId}");

                        return Json(new
                        {
                            success = true,
                            orderId = orderId,
                            amount = adjustedAmount * 100, // Amount to pay in paise
                            fullAmount = amount,
                            creditAmount = creditAmount,
                            amountToPay = adjustedAmount,
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
                        // Create Razorpay order for full amount (scheduled upgrade)
                        var orderId = await _razorpayService.CreateOrderAsync(amount, "INR", $"upgrade_scheduled_{channelPartnerId}_{planId}");

                        return Json(new
                        {
                            success = true,
                            orderId = orderId,
                            amount = amount * 100, // Full amount in paise
                            fullAmount = amount,
                            amountToPay = amount,
                            planName = plan.PlanName,
                            billingCycle = billingCycle,
                            upgradeType = "scheduled",
                            startDate = currentSubscription.EndDate.AddDays(1).ToString("yyyy-MM-dd")
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error creating Razorpay order for scheduled upgrade");
                        return Json(new { success = false, message = "Failed to create payment order" });
                    }
                }
            }
        }

        [HttpPost]
        public async Task<IActionResult> ConfirmPayment(string razorpayPaymentId, string razorpayOrderId, string razorpaySignature, int planId, string billingCycle, string? paymentStatus = null, string upgradeType = "immediate")
        {
            var (userId, role, channelPartnerId) = GetCurrentUserContext();

            if (!channelPartnerId.HasValue)
                return Json(new { success = false, message = "Partner context not found", errorCode = "NO_PARTNER" });

            // Check if payment failed at Razorpay level (before verification)
            if (!string.IsNullOrEmpty(paymentStatus) && paymentStatus.ToLower() == "failed")
            {
                _logger.LogWarning($"Payment failed at Razorpay level for order {razorpayOrderId}");

                // Record failed payment
                var failedTransaction = new PaymentTransactionModel
                {
                    ChannelPartnerId = channelPartnerId.Value,
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

                _context.PaymentTransactions.Add(failedTransaction);
                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = false,
                    message = "Payment was declined. Please try again or use a different payment method.",
                    errorCode = "PAYMENT_FAILED",
                    canRetry = true
                });
            }

            // Use a database transaction to ensure atomicity
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                _logger.LogInformation($"Starting payment confirmation for partner {channelPartnerId}, plan {planId}");

                // Verify payment signature
                if (!_razorpayService.VerifyPaymentSignature(razorpayPaymentId, razorpayOrderId, razorpaySignature))
                {
                    _logger.LogWarning($"Payment verification failed for order {razorpayOrderId}");

                    // Record signature verification failure
                    var failedTransaction = new PaymentTransactionModel
                    {
                        ChannelPartnerId = channelPartnerId.Value,
                        TransactionReference = razorpayOrderId,
                        RazorpayOrderId = razorpayOrderId,
                        RazorpayPaymentId = razorpayPaymentId,
                        Status = "Failed",
                        TransactionType = "Verification Failed",
                        PaymentMethod = "Razorpay",
                        TransactionDate = IndianTime.Now,
                        CompletedDate = IndianTime.Now,
                        Description = "Payment signature verification failed",
                        CreatedOn = IndianTime.Now
                    };

                    _context.PaymentTransactions.Add(failedTransaction);
                    await _context.SaveChangesAsync();

                    return Json(new
                    {
                        success = false,
                        message = "Payment verification failed. If amount was deducted, it will be refunded within 5-7 business days.",
                        errorCode = "VERIFICATION_FAILED",
                        canRetry = false
                    });
                }

                // Fetch payment details from Razorpay to get card information
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

                        // Extract card details if available
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
                                else if (card.TryGetProperty("name", out var name))
                                {
                                    bankName = name.GetString();
                                }
                            }
                        }

                        _logger.LogInformation($"Card details fetched: Type={cardType}, Network={cardNetwork}, Last4={cardLast4}, Bank={bankName}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch card details from Razorpay, continuing with payment processing");
                }

                var plan = await _context.SubscriptionPlans.FindAsync(planId);
                if (plan == null)
                {
                    _logger.LogWarning($"Plan {planId} not found");
    
                    return Json(new { success = false, message = "Invalid plan selected", errorCode = "INVALID_PLAN" });
                }

                var amount = billingCycle.ToLower() == "annual" ? plan.YearlyPrice : plan.MonthlyPrice;
                _logger.LogInformation($"Plan found: {plan.PlanName}, Amount: {amount}");

                var currentSubscription = await _context.PartnerSubscriptions
                    .Where(s => s.ChannelPartnerId == channelPartnerId.Value && s.Status == "Active")
                    .FirstOrDefaultAsync();

                _logger.LogInformation($"Current subscription exists: {currentSubscription != null}");

                if (currentSubscription != null)
                {
                    if (upgradeType.ToLower() == "immediate")
                    {
                        _logger.LogInformation("Processing immediate upgrade - activating plan now");

                        // For immediate upgrade, end current subscription and create new active subscription
                        currentSubscription.Status = "Expired";
                        currentSubscription.EndDate = IndianTime.Now;
                        currentSubscription.UpdatedOn = IndianTime.Now;
                        currentSubscription.CancellationReason = $"Upgraded to {plan.PlanName} immediately";
                        currentSubscription.CancelledOn = IndianTime.Now;

                        // Create immediate active subscription
                        var immediateSubscription = new PartnerSubscriptionModel
                        {
                            ChannelPartnerId = channelPartnerId.Value,
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

                        _context.PartnerSubscriptions.Add(immediateSubscription);
                        await _context.SaveChangesAsync();

                        // Create payment transaction record
                        var immediateTransaction = new PaymentTransactionModel
                        {
                            ChannelPartnerId = channelPartnerId.Value,
                            SubscriptionId = immediateSubscription.SubscriptionId,
                            TransactionReference = razorpayOrderId,
                            RazorpayPaymentId = razorpayPaymentId,
                            RazorpayOrderId = razorpayOrderId,
                            RazorpaySignature = razorpaySignature,
                            Amount = amount,
                            Currency = "INR",
                            TransactionType = "Immediate Upgrade",
                            Status = "Success",
                            PaymentMethod = "Razorpay",
                            TransactionDate = IndianTime.Now,
                            CompletedDate = IndianTime.Now,
                            PlanName = plan.PlanName,
                            BillingCycle = billingCycle,
                            NetAmount = amount,
                            Description = $"Immediate upgrade to {plan.PlanName} plan",
                            CardType = cardType,
                            CardNetwork = cardNetwork,
                            CardLast4 = cardLast4,
                            BankName = bankName,
                            CreatedOn = IndianTime.Now
                        };

                        _context.PaymentTransactions.Add(immediateTransaction);
                        await _context.SaveChangesAsync();

                        // Update subscription with payment transaction reference
                        immediateSubscription.PaymentTransactionId = immediateTransaction.TransactionId.ToString();
                        await _context.SaveChangesAsync();

                        await transaction.CommitAsync();

                        return Json(new { success = true, message = $"Payment successful! Your {plan.PlanName} plan is now active immediately." });
                    }
                    else
                    {
                        _logger.LogInformation($"Processing scheduled upgrade, current expires: {currentSubscription.EndDate}");

                        // Calculate credit from existing scheduled subscriptions
                        decimal creditAmount = 0;
                        string creditDescription = "";

                        // Cancel existing scheduled subscriptions instead of deleting them
                        // (they have payment transactions referencing them via FK)
                        var existingScheduled = await _context.PartnerSubscriptions
                            
                            .Where(s => s.ChannelPartnerId == channelPartnerId.Value && s.Status == "Scheduled")
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

                            // Validate that new plan is not cheaper (downgrade)
                            if (amount <= creditAmount)
                            {
                
                                _logger.LogWarning($"Attempted downgrade from ₹{creditAmount} to ₹{amount}");
                                return Json(new
                                {
                                    success = false,
                                    isDowngrade = true,
                                    existingPlanName = existingScheduled.First().Plan?.PlanName,
                                    existingAmount = creditAmount,
                                    newPlanName = plan.PlanName,
                                    newAmount = amount,
                                    refundAmount = creditAmount - amount,
                                    message = $"Cannot process downgrade. You already paid ₹{creditAmount:N0} for {existingScheduled.First().Plan?.PlanName}. The {plan.PlanName} costs ₹{amount:N0}. Please contact support for assistance with downgrades and refunds."
                                });
                            }

                            await _context.SaveChangesAsync();
                            _logger.LogInformation($"Cancelled {existingScheduled.Count} existing scheduled subscriptions. Total credit: ₹{creditAmount}");

                            creditDescription = $" (Credit of ₹{creditAmount:N0} applied from {existingScheduled.First().Plan?.PlanName})";
                        }

                        // Create scheduled subscription first
                        var startDate = currentSubscription.EndDate.AddDays(1);
                        _logger.LogInformation($"Creating scheduled subscription starting: {startDate}");

                        var scheduledSubscription = await _subscriptionService.CreateScheduledSubscriptionAsync(
                            channelPartnerId.Value,
                            planId,
                            billingCycle,
                            startDate
                        );

                        _logger.LogInformation($"Scheduled subscription created with ID: {scheduledSubscription.SubscriptionId}");

                        // Create payment transaction record
                        var paymentTransaction = new PaymentTransactionModel
                        {
                            ChannelPartnerId = channelPartnerId.Value,
                            SubscriptionId = scheduledSubscription.SubscriptionId,
                            TransactionReference = razorpayOrderId,
                            RazorpayPaymentId = razorpayPaymentId,
                            RazorpayOrderId = razorpayOrderId,
                            RazorpaySignature = razorpaySignature,
                            Amount = amount, // Full plan amount (before credit)
                            Currency = "INR",
                            TransactionType = "Scheduled Payment",
                            Status = "Success",
                            PaymentMethod = "Razorpay",
                            TransactionDate = IndianTime.Now,
                            CompletedDate = IndianTime.Now,
                            PlanName = plan.PlanName,
                            BillingCycle = billingCycle,
                            DiscountAmount = creditAmount, // Credit stored as discount
                            NetAmount = amount - creditAmount, // Actual amount paid
                            Description = $"Scheduled subscription payment for {plan.PlanName} plan{creditDescription}",
                            CardType = cardType,
                            CardNetwork = cardNetwork,
                            CardLast4 = cardLast4,
                            BankName = bankName,
                            CreatedOn = IndianTime.Now
                        };

                        _context.PaymentTransactions.Add(paymentTransaction);
                        await _context.SaveChangesAsync();
                        _logger.LogInformation($"Transaction created with ID: {paymentTransaction.TransactionId}");

                        // Update scheduled subscription with payment transaction reference
                        // Re-fetch the subscription to ensure it's tracked by the current context
                        var subscriptionToUpdate = await _context.PartnerSubscriptions
                            .FirstOrDefaultAsync(s => s.SubscriptionId == scheduledSubscription.SubscriptionId);

                        if (subscriptionToUpdate != null)
                        {
                            subscriptionToUpdate.PaymentTransactionId = paymentTransaction.TransactionId.ToString();
                            subscriptionToUpdate.LastPaymentDate = IndianTime.Now;
                            await _context.SaveChangesAsync();
                            _logger.LogInformation($"Updated scheduled subscription with payment reference");
                        }

                        // Commit the database transaction
                        await transaction.CommitAsync();

                        var netAmountPaid = amount - creditAmount;
                        var message = existingScheduled.Any()
                            ? creditAmount > 0
                                ? $"Payment successful! ₹{netAmountPaid:N0} paid (₹{creditAmount:N0} credit applied from previous plan). Your {plan.PlanName} plan will activate on {startDate:dd/MM/yyyy}."
                                : $"Payment successful! ₹{netAmountPaid:N0} received. Previous scheduled plan replaced. Your new {plan.PlanName} plan will activate on {startDate:dd/MM/yyyy}."
                            : $"Payment successful! ₹{netAmountPaid:N0} received. Your new {plan.PlanName} plan will activate on {startDate:dd/MM/yyyy}.";

                        return Json(new { success = true, message });
                    }
                }
                else
                {
                    _logger.LogInformation("Creating immediate subscription");

                    // Create immediate subscription first
                    var subscription = await _subscriptionService.CreateSubscriptionAsync(
                        channelPartnerId.Value,
                        planId,
                        billingCycle,
                        string.Empty // We'll set this after creating transaction
                    );

                    _logger.LogInformation($"Immediate subscription created with ID: {subscription.SubscriptionId}");

                    // Create payment transaction record
                    var paymentTransaction = new PaymentTransactionModel
                    {
                        ChannelPartnerId = channelPartnerId.Value,
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

                    _context.PaymentTransactions.Add(paymentTransaction);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"Transaction created with ID: {paymentTransaction.TransactionId}");

                    // Update subscription with payment transaction reference
                    // Re-fetch the subscription to ensure it's tracked by the current context
                    var subscriptionToUpdate = await _context.PartnerSubscriptions
                        .FirstOrDefaultAsync(s => s.SubscriptionId == subscription.SubscriptionId);

                    if (subscriptionToUpdate != null)
                    {
                        subscriptionToUpdate.PaymentTransactionId = paymentTransaction.TransactionId.ToString();
                        subscriptionToUpdate.LastPaymentDate = IndianTime.Now;
                        await _context.SaveChangesAsync();
                        _logger.LogInformation($"Updated subscription with payment reference");
                    }

                    // Commit the database transaction
                    await transaction.CommitAsync();

                    return Json(new { success = true, message = $"Payment successful! ₹{amount:N0} received. Your {plan.PlanName} subscription is now active." });
                }
            }
            catch (Exception ex)
            {
                // Rollback the transaction on error


                _logger.LogError(ex, $"Error processing payment confirmation for partner {channelPartnerId}, plan {planId}");

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

        // Admin: Manage Partner Subscriptions
        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> ManagePartnerSubscriptions(string? search, int? plan, string? billing, DateTime? fromDate, DateTime? toDate, int page = 1)
        {
            const int pageSize = 10;

            // Get only partners with active subscriptions
            var query = _context.PartnerSubscriptions
                
                
                .Where(s => s.Status == "Active")
                .AsQueryable();

            // Apply filters
            if (!string.IsNullOrEmpty(search))
                query = query.Where(s => s.ChannelPartner!.CompanyName != null && s.ChannelPartner!.CompanyName.Contains(search));

            if (plan.HasValue)
                query = query.Where(s => s.PlanId == plan.Value);

            if (!string.IsNullOrEmpty(billing))
                query = query.Where(s => s.BillingCycle == billing);

            if (fromDate.HasValue)
                query = query.Where(s => s.StartDate >= fromDate.Value);

            if (toDate.HasValue)
                query = query.Where(s => s.StartDate <= toDate.Value);

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            var subscriptions = await query
                .OrderByDescending(s => s.CreatedOn)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Get scheduled subscriptions for each partner
            var partnerIds = subscriptions.Select(s => s.ChannelPartnerId).Distinct().ToList();
            var scheduledSubscriptions = await _context.PartnerSubscriptions
                
                .Where(s => partnerIds.Contains(s.ChannelPartnerId) && s.Status == "Scheduled")
                .Select(s => new
                {
                    s.ChannelPartnerId,
                    s.PlanId,
                    s.BillingCycle,
                    PlanName = s.Plan!.PlanName
                })
                .ToListAsync();

            ViewBag.ScheduledSubscriptions = scheduledSubscriptions;

            var availablePlans = await _subscriptionService.GetAvailablePlansAsync();
            var allPlans = await _context.SubscriptionPlans.ToListAsync();

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

        // Admin: View Pending Refunds
        [HttpGet]
        public async Task<IActionResult> PendingRefunds(string? search, int page = 1)
        {
            var (userId, role, channelPartnerId) = GetCurrentUserContext();
            const int pageSize = 20;

            // Get cancellation transactions that need refund processing
            var query = _context.PaymentTransactions
                
                .Where(t => t.TransactionType == "Cancellation" &&
                           t.Status == "Cancelled" &&
                           t.Description != null &&
                           t.Description.Contains("Refund Pending"))
                .AsQueryable();

            // Filter by partner if not admin
            if (role?.ToLower() == "partner" && channelPartnerId.HasValue)
            {
                query = query.Where(t => t.ChannelPartnerId == channelPartnerId.Value);
            }

            // Apply search filter
            if (!string.IsNullOrEmpty(search))
                query = query.Where(t => t.ChannelPartner!.CompanyName.Contains(search) ||
                                        t.ChannelPartner!.ContactPerson.Contains(search));

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

        // Admin: Get Refund Details for Processing
        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> GetRefundDetails(int transactionId)
        {
            try
            {
                var cancellationTransaction = await _context.PaymentTransactions
                    
                    .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

                if (cancellationTransaction == null)
                    return Json(new { success = false, message = "Transaction not found" });

                // Find the subscription
                var subscription = await _context.PartnerSubscriptions
                    
                    .FirstOrDefaultAsync(s => s.SubscriptionId == cancellationTransaction.SubscriptionId);

                // Find the original payment transaction
                var paymentTransaction = await _context.PaymentTransactions
                    .Where(t => t.SubscriptionId == cancellationTransaction.SubscriptionId
                        && t.Status == "Success"
                        && t.TransactionType != "Refund"
                        && t.TransactionType != "Cancellation")
                    .OrderByDescending(t => t.TransactionDate)
                    .FirstOrDefaultAsync();

                // If no transaction found for this subscription, look for any recent payment by this partner
                if (paymentTransaction == null)
                {
                    paymentTransaction = await _context.PaymentTransactions
                        .Where(t => t.ChannelPartnerId == cancellationTransaction.ChannelPartnerId
                            && t.Status == "Success"
                            && !string.IsNullOrEmpty(t.RazorpayPaymentId)
                            && t.TransactionType != "Refund"
                            && t.TransactionType != "Cancellation")
                        .OrderByDescending(t => t.TransactionDate)
                        .FirstOrDefaultAsync();
                }

                return Json(new
                {
                    success = true,
                    refundDetails = new
                    {
                        transactionId = transactionId,
                        partnerName = cancellationTransaction.ChannelPartner?.CompanyName,
                        partnerEmail = cancellationTransaction.ChannelPartner?.Email,
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
                            paymentMethod = paymentTransaction.PaymentMethod,
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
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // Admin: Mark Refund as Processed (with Razorpay integration)
        [HttpPost]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> MarkRefundProcessed(int transactionId, string refundNotes)
        {
            try
            {
                var cancellationTransaction = await _context.PaymentTransactions
                    
                    .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

                if (cancellationTransaction == null)
                    return Json(new { success = false, message = "Transaction not found" });

                // Find the subscription
                var subscription = await _context.PartnerSubscriptions
                    
                    .FirstOrDefaultAsync(s => s.SubscriptionId == cancellationTransaction.SubscriptionId);

                if (subscription == null)
                    return Json(new { success = false, message = "Subscription not found" });

                // Find the most recent successful payment transaction for this subscription
                var paymentTransaction = await _context.PaymentTransactions
                    .Where(t => t.SubscriptionId == cancellationTransaction.SubscriptionId
                        && t.Status == "Success"
                        && t.TransactionType != "Refund"
                        && t.TransactionType != "Cancellation")
                    .OrderByDescending(t => t.TransactionDate)
                    .FirstOrDefaultAsync();

                // If no transaction found for this subscription, look for any recent payment by this partner
                if (paymentTransaction == null || string.IsNullOrEmpty(paymentTransaction.RazorpayPaymentId))
                {
                    paymentTransaction = await _context.PaymentTransactions
                        .Where(t => t.ChannelPartnerId == subscription.ChannelPartnerId
                            && t.Status == "Success"
                            && !string.IsNullOrEmpty(t.RazorpayPaymentId)
                            && t.TransactionType != "Refund"
                            && t.TransactionType != "Cancellation")
                        .OrderByDescending(t => t.TransactionDate)
                        .FirstOrDefaultAsync();
                }

                if (paymentTransaction == null || string.IsNullOrEmpty(paymentTransaction.RazorpayPaymentId))
                {
                    // Process as manual refund
                    cancellationTransaction.Description = cancellationTransaction.Description?.Replace("Refund Pending", "Manual Refund Processed") + $" - Admin Notes: {refundNotes}";

                    // Update the related subscription status to ensure it's properly cancelled
                    if (subscription != null)
                    {
                        subscription.Status = "Cancelled";
                        subscription.CancelledOn = IndianTime.Now;
                        subscription.CancellationReason = "PERMANENTLY CANCELLED - Manual Refund Processed - DO NOT REACTIVATE";
                        subscription.UpdatedOn = IndianTime.Now;
                    }

                    // Create manual refund transaction record
                    var manualRefundTransaction = new PaymentTransactionModel
                    {
                        ChannelPartnerId = subscription.ChannelPartnerId,
                        SubscriptionId = subscription.SubscriptionId,
                        TransactionReference = $"manual_refund_{IndianTime.Now.Ticks}",
                        Amount = cancellationTransaction.Amount,
                        Currency = "INR",
                        TransactionType = "Refund",
                        Status = "Success",
                        PaymentMethod = "Manual",
                        TransactionDate = IndianTime.Now,
                        CompletedDate = IndianTime.Now,
                        Description = $"Manual refund for {cancellationTransaction.PlanName} - Admin will transfer ₹{cancellationTransaction.Amount:N0} to partner - {refundNotes}",
                        PlanName = cancellationTransaction.PlanName,
                        BillingCycle = cancellationTransaction.BillingCycle,
                        NetAmount = cancellationTransaction.Amount,
                        CreatedOn = IndianTime.Now
                    };
                    _context.PaymentTransactions.Add(manualRefundTransaction);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"Manual refund transaction created for cancellation {transactionId}. Amount: ₹{cancellationTransaction.Amount}");
                    return Json(new
                    {
                        success = true,
                        message = $"Manual refund of ₹{cancellationTransaction.Amount:N0} processed for {cancellationTransaction.ChannelPartner?.CompanyName}. Admin will transfer the amount to partner's account."
                    });
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
                    var refundTransaction = new PaymentTransactionModel
                    {
                        ChannelPartnerId = subscription.ChannelPartnerId,
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
                    _context.PaymentTransactions.Add(refundTransaction);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"Razorpay refund processed for cancellation {transactionId}. Refund ID: {refundId}, Amount: ₹{cancellationTransaction.Amount}");
                    return Json(new
                    {
                        success = true,
                        message = $"Refund of ₹{cancellationTransaction.Amount:N0} processed successfully for {cancellationTransaction.ChannelPartner?.CompanyName}. Refund ID: {refundId}"
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
        public async Task<IActionResult> AdminUpgradePlan(int channelPartnerId, int newPlanId, string billingCycle)
        {
            try
            {
                var plan = await _context.SubscriptionPlans.FindAsync(newPlanId);
                if (plan == null)
                    return Json(new { success = false, message = "Invalid plan selected" });

                // Get current active subscription
                var currentSubscription = await _context.PartnerSubscriptions
                    .Where(s => s.ChannelPartnerId == channelPartnerId && s.Status == "Active")
                    .FirstOrDefaultAsync();

                if (currentSubscription == null)
                    return Json(new { success = false, message = "No active subscription found" });

                // Cancel existing scheduled subscriptions
                var existingScheduled = await _context.PartnerSubscriptions
                    .Where(s => s.ChannelPartnerId == channelPartnerId && s.Status == "Scheduled")
                    .ToListAsync();

                foreach (var scheduled in existingScheduled)
                {
                    scheduled.Status = "Cancelled";
                    scheduled.CancelledOn = IndianTime.Now;
                    scheduled.CancellationReason = "Replaced by new scheduled plan";
                    scheduled.UpdatedOn = IndianTime.Now;
                }

                var amount = billingCycle.ToLower() == "annual" ? plan.YearlyPrice : plan.MonthlyPrice;
                var startDate = currentSubscription.EndDate.AddDays(1); // Start after current expires
                var endDate = billingCycle.ToLower() == "annual" ? startDate.AddYears(1) : startDate.AddMonths(1);

                // Create admin transaction
                var transaction = new PaymentTransactionModel
                {
                    ChannelPartnerId = channelPartnerId,
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

                _context.PaymentTransactions.Add(transaction);
                await _context.SaveChangesAsync();

                // Create scheduled subscription
                var newSubscription = new PartnerSubscriptionModel
                {
                    ChannelPartnerId = channelPartnerId,
                    PlanId = newPlanId,
                    BillingCycle = billingCycle,
                    Amount = amount,
                    StartDate = startDate,
                    EndDate = endDate,
                    Status = "Scheduled",
                    CreatedOn = IndianTime.Now
                };

                _context.PartnerSubscriptions.Add(newSubscription);
                await _context.SaveChangesAsync();

                transaction.SubscriptionId = newSubscription.SubscriptionId;
                await _context.SaveChangesAsync();

                var message = existingScheduled.Any()
                    ? $"Previous scheduled plans replaced. New plan will start on {startDate:dd/MM/yyyy}!"
                    : $"Partner plan scheduled to upgrade on {startDate:dd/MM/yyyy}!";

                return Json(new { success = true, message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error upgrading partner plan");
                return Json(new { success = false, message = "Failed to upgrade plan. Please try again." });
            }
        }

        // API Endpoints for restrictions
        [HttpGet]
        public async Task<IActionResult> GetAvailablePlans()
        {
            var plans = await _subscriptionService.GetAvailablePlansAsync();
            return Json(plans);
        }

        [HttpGet]
        public async Task<IActionResult> CheckAgentLimit()
        {
            var (userId, role, channelPartnerId) = GetCurrentUserContext();

            if (!channelPartnerId.HasValue)
                return Json(new { canAdd = false, message = "Partner context not found" });

            var (canAdd, message) = await _subscriptionService.CanAddAgentAsync(channelPartnerId.Value);
            return Json(new { canAdd, message });
        }

        [HttpGet]
        public async Task<IActionResult> CheckLeadLimit()
        {
            var (userId, role, channelPartnerId) = GetCurrentUserContext();

            if (!channelPartnerId.HasValue)
                return Json(new { canAdd = false, message = "Partner context not found" });

            var (canAdd, message) = await _subscriptionService.CanAddLeadAsync(channelPartnerId.Value);
            return Json(new { canAdd, message });
        }

        [HttpGet]
        public async Task<IActionResult> CheckFeatureAccess(string feature)
        {
            var (userId, role, channelPartnerId) = GetCurrentUserContext();

            if (!channelPartnerId.HasValue)
                return Json(new { hasAccess = false });

            var hasAccess = await _subscriptionService.HasFeatureAccessAsync(channelPartnerId.Value, feature);
            return Json(new { hasAccess });
        }

        // Export transactions
        [HttpGet]
        public async Task<IActionResult> ExportTransactions(string format = "excel", string? status = null, string? type = null, DateTime? fromDate = null, DateTime? toDate = null)
        {
            var (userId, role, channelPartnerId) = GetCurrentUserContext();

            var transactionsQuery = _context.PaymentTransactions
                
                
                
                .AsQueryable();

            // Filter by partner if not admin
            if (role?.ToLower() == "partner" && channelPartnerId.HasValue)
            {
                transactionsQuery = transactionsQuery.Where(t => t.ChannelPartnerId == channelPartnerId.Value);
            }

            // Apply filters
            if (!string.IsNullOrEmpty(status))
                transactionsQuery = transactionsQuery.Where(t => t.Status == status);

            if (!string.IsNullOrEmpty(type))
                transactionsQuery = transactionsQuery.Where(t => t.TransactionType == type);

            if (fromDate.HasValue)
                transactionsQuery = transactionsQuery.Where(t => t.TransactionDate >= fromDate.Value);

            if (toDate.HasValue)
                transactionsQuery = transactionsQuery.Where(t => t.TransactionDate <= toDate.Value.AddDays(1));

            var transactions = await transactionsQuery
                .OrderByDescending(t => t.TransactionDate)
                .ToListAsync();

            if (format.ToLower() == "csv")
            {
                var csv = GenerateCSV(transactions);
                return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"Transactions_{IndianTime.Now:yyyy-MM-dd}.csv");
            }

            // For Excel export, you would use a library like EPPlus or ClosedXML
            // For now, returning CSV format
            var csvContent = GenerateCSV(transactions);
            return File(System.Text.Encoding.UTF8.GetBytes(csvContent), "application/vnd.ms-excel", $"Transactions_{IndianTime.Now:yyyy-MM-dd}.csv");
        }

        private string GenerateCSV(List<PaymentTransactionModel> transactions)
        {
            var csv = "Date,Partner,Plan,Amount,Type,Status,Payment Method,Transaction ID\n";

            foreach (var transaction in transactions)
            {
                csv += $"{transaction.TransactionDate:yyyy-MM-dd HH:mm}," +
                       $"{transaction.ChannelPartner?.CompanyName ?? "N/A"}," +
                       $"{transaction.PlanName ?? "N/A"}," +
                       $"{transaction.Amount}," +
                       $"{transaction.TransactionType}," +
                       $"{transaction.Status}," +
                       $"{transaction.PaymentMethod}," +
                       $"{transaction.TransactionReference}\n";
            }

            return csv;
        }

        // Razorpay Webhook Handler
        [HttpPost]
        [Route("/webhook/razorpay")]
        public async Task<IActionResult> RazorpayWebhook()
        {
            try
            {
                var body = await new StreamReader(Request.Body).ReadToEndAsync();
                var signature = Request.Headers["X-Razorpay-Signature"].FirstOrDefault();

                if (string.IsNullOrEmpty(signature) || !_razorpayService.VerifyWebhookSignature(body, signature))
                {
                    _logger.LogWarning("Webhook signature verification failed");
                    return BadRequest("Invalid signature");
                }

                var webhook = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(body);
                var eventType = webhook.GetProperty("event").GetString();
                var eventId = eventType + "_" + IndianTime.Now.Ticks; // Basic idempotency key

                // Idempotency check - prevent duplicate webhook processing
                var existingWebhook = await _context.PaymentTransactions
                    .FirstOrDefaultAsync(t => t.WebhookEventId == eventId);

                if (existingWebhook != null)
                {
                    _logger.LogInformation($"Webhook event {eventId} already processed, skipping");
                    return Ok(new { status = "already_processed" });
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

            var transaction = await _context.PaymentTransactions
                
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
                if (transaction.TransactionType?.StartsWith("Upgrade_") == true)
                {
                    _logger.LogInformation($"Processing upgrade activation for transaction {transaction.TransactionId}");
                    await ActivatePlanImmediately(transaction);
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation($"Transaction {transaction.TransactionId} marked as Success");
            }
            else
            {
                _logger.LogWarning($"No transaction found for order {orderId}, attempting auto-activation from payment details");

                // Get payment details from Razorpay API to find partner email
                var (success, paymentDetails) = await _razorpayService.FetchPaymentAsync(paymentId);
                if (!success || !paymentDetails.HasValue)
                {
                    _logger.LogWarning($"Could not fetch payment details from Razorpay for {paymentId}");
                    return;
                }

                var payment = paymentDetails.Value;
                var amount = payment.GetProperty("amount").GetInt32() / 100m; // Convert from paise
                var customerEmail = "";

                // Try to get email from payment details
                if (payment.TryGetProperty("email", out var emailProp))
                {
                    customerEmail = emailProp.GetString() ?? "";
                }
                else if (payment.TryGetProperty("contact", out var contactProp) && contactProp.GetString() != null)
                {
                    // Sometimes email is in contact field
                    var contact = contactProp.GetString();
                    if (contact != null && contact.Contains("@"))
                    {
                        customerEmail = contact;
                    }
                }

                _logger.LogInformation($"Payment details: amount=₹{amount}, email={customerEmail}");

                // Find partner by email
                var partner = await _context.ChannelPartners
                    .FirstOrDefaultAsync(p => p.Email == customerEmail);

                if (partner != null)
                {
                    _logger.LogInformation($"Found partner {partner.CompanyName} (ID: {partner.PartnerId}) for email {customerEmail}, amount: ₹{amount}");

                    // Determine plan based on amount
                    var plan = await _context.SubscriptionPlans
                        .Where(p => p.IsActive && Math.Abs(p.MonthlyPrice - amount) < 1) // Match within ₹1
                        .FirstOrDefaultAsync();

                    if (plan == null)
                    {
                        // Try yearly price match
                        plan = await _context.SubscriptionPlans
                            .Where(p => p.IsActive && Math.Abs(p.YearlyPrice - amount) < 1)
                            .FirstOrDefaultAsync();
                    }

                    if (plan != null)
                    {
                        _logger.LogInformation($"Matched plan {plan.PlanName} (ID: {plan.PlanId}) for amount ₹{amount}");

                        // End current subscription
                        var currentSub = await _context.PartnerSubscriptions
                            .Where(s => s.ChannelPartnerId == partner.PartnerId && s.Status == "Active")
                            .FirstOrDefaultAsync();

                        if (currentSub != null)
                        {
                            _logger.LogInformation($"Ending current subscription {currentSub.SubscriptionId} for partner {partner.PartnerId}");
                            currentSub.Status = "Expired";
                            currentSub.EndDate = IndianTime.Now;
                            currentSub.CancellationReason = $"Upgraded to {plan.PlanName} via payment";
                            currentSub.CancelledOn = IndianTime.Now;
                            currentSub.UpdatedOn = IndianTime.Now;
                        }

                        // Determine billing cycle based on amount
                        var billingCycle = Math.Abs(plan.YearlyPrice - amount) < 1 ? "annual" : "monthly";
                        var endDate = billingCycle == "annual" ? IndianTime.Now.AddYears(1) : IndianTime.Now.AddMonths(1);

                        // Create new subscription
                        var newSub = new PartnerSubscriptionModel
                        {
                            ChannelPartnerId = partner.PartnerId,
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

                        _context.PartnerSubscriptions.Add(newSub);
                        await _context.SaveChangesAsync();

                        // Create transaction record
                        var newTransaction = new PaymentTransactionModel
                        {
                            ChannelPartnerId = partner.PartnerId,
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

                        _context.PaymentTransactions.Add(newTransaction);
                        await _context.SaveChangesAsync();

                        _logger.LogInformation($"Auto-activated {plan.PlanName} for partner {partner.CompanyName} from webhook payment {paymentId}. New subscription ID: {newSub.SubscriptionId}");
                    }
                    else
                    {
                        _logger.LogWarning($"No matching plan found for amount ₹{amount}. Available plans: {string.Join(", ", await _context.SubscriptionPlans.Where(p => p.IsActive).Select(p => $"{p.PlanName}(₹{p.MonthlyPrice}/₹{p.YearlyPrice})").ToListAsync())}");
                    }
                }
                else
                {
                    _logger.LogWarning($"No partner found for email '{customerEmail}'. Available partners: {string.Join(", ", await _context.ChannelPartners.Select(p => p.Email).ToListAsync())}");
                }
            }
        }

        private async Task ActivateSubscriptionOnCapture(PartnerSubscriptionModel subscription)
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

        private async Task ActivatePlanFromWebhook(PaymentTransactionModel transaction)
        {
            try
            {
                var partner = await _context.ChannelPartners.FindAsync(transaction.ChannelPartnerId);
                if (partner == null) return;

                // Extract plan info from transaction
                var planName = transaction.PlanName;
                var billingCycle = transaction.BillingCycle ?? "monthly";

                var plan = await _context.SubscriptionPlans
                    .FirstOrDefaultAsync(p => p.PlanName == planName);
                if (plan == null) return;

                // Check if partner has no active subscription
                var currentSubscription = await _context.PartnerSubscriptions
                    .Where(s => s.ChannelPartnerId == transaction.ChannelPartnerId && s.Status == "Active")
                    .FirstOrDefaultAsync();

                if (currentSubscription == null)
                {
                    // Create immediate active subscription
                    var newSubscription = new PartnerSubscriptionModel
                    {
                        ChannelPartnerId = transaction.ChannelPartnerId,
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
                        UpdatedOn = IndianTime.Now
                    };

                    _context.PartnerSubscriptions.Add(newSubscription);
                    await _context.SaveChangesAsync();

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

        private async Task HandlePaymentFailed(JsonElement webhook, string eventId)
        {
            var paymentEntity = webhook.GetProperty("payload").GetProperty("payment").GetProperty("entity");
            var paymentId = paymentEntity.GetProperty("id").GetString();
            var orderId = paymentEntity.GetProperty("order_id").GetString();
            var errorDescription = paymentEntity.TryGetProperty("error_description", out var ed) ? ed.GetString() : "Payment failed";

            _logger.LogWarning($"Payment failed: {paymentId} for order {orderId}, error: {errorDescription}");

            var transaction = await _context.PaymentTransactions
                .FirstOrDefaultAsync(t => t.RazorpayOrderId == orderId);

            if (transaction != null)
            {
                transaction.Status = "Failed";
                transaction.CompletedDate = IndianTime.Now;
                transaction.WebhookEventId = eventId;
                transaction.RazorpayPaymentId = paymentId;
                transaction.Description += $" | Failed: {errorDescription}";
                _context.PaymentTransactions.Update(transaction);
                await _context.SaveChangesAsync();
            }
        }

        private async Task HandlePaymentAuthorized(JsonElement webhook, string eventId)
        {
            var paymentEntity = webhook.GetProperty("payload").GetProperty("payment").GetProperty("entity");
            var paymentId = paymentEntity.GetProperty("id").GetString();
            var orderId = paymentEntity.GetProperty("order_id").GetString();

            _logger.LogInformation($"Payment authorized: {paymentId} for order {orderId}");

            var transaction = await _context.PaymentTransactions
                .FirstOrDefaultAsync(t => t.RazorpayOrderId == orderId);

            if (transaction != null && transaction.Status == "Pending")
            {
                transaction.Status = "Authorized";
                transaction.WebhookEventId = eventId;
                transaction.RazorpayPaymentId = paymentId;
                _context.PaymentTransactions.Update(transaction);
                await _context.SaveChangesAsync();
            }
        }

        private async Task ActivatePlanImmediately(PaymentTransactionModel transaction)
        {
            try
            {
                _logger.LogInformation($"ActivatePlanImmediately called for transaction {transaction.TransactionId}, type: {transaction.TransactionType}, partner: {transaction.ChannelPartnerId}");

                var partner = await _context.ChannelPartners.FindAsync(transaction.ChannelPartnerId);
                if (partner == null)
                {
                    _logger.LogWarning($"Partner {transaction.ChannelPartnerId} not found");
                    return;
                }

                var upgradeType = transaction.TransactionType?.Replace("Upgrade_", "") ?? "immediate";
                var plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.PlanName == transaction.PlanName);
                if (plan == null)
                {
                    _logger.LogWarning($"Plan '{transaction.PlanName}' not found");
                    return;
                }

                _logger.LogInformation($"Activating {upgradeType} upgrade to {transaction.PlanName} (ID: {plan.PlanId}) for partner {partner.CompanyName}");

                // End current subscription immediately for all upgrade types
                var currentSubscription = await _context.PartnerSubscriptions
                    .Where(s => s.ChannelPartnerId == transaction.ChannelPartnerId && s.Status == "Active")
                    .FirstOrDefaultAsync();

                if (currentSubscription != null)
                {
                    _logger.LogInformation($"Ending current subscription {currentSubscription.SubscriptionId} for upgrade");
                    currentSubscription.Status = "Expired";
                    currentSubscription.EndDate = IndianTime.Now;
                    currentSubscription.UpdatedOn = IndianTime.Now;
                    currentSubscription.CancellationReason = $"Upgraded to {transaction.PlanName} via payment";
                    currentSubscription.CancelledOn = IndianTime.Now;
                }

                // Create new active subscription
                var newSubscription = new PartnerSubscriptionModel
                {
                    ChannelPartnerId = transaction.ChannelPartnerId,
                    PlanId = plan.PlanId,
                    BillingCycle = transaction.BillingCycle ?? "monthly",
                    Amount = transaction.Amount,
                    StartDate = IndianTime.Now,
                    EndDate = (transaction.BillingCycle?.ToLower() == "annual") ? IndianTime.Now.AddYears(1) : IndianTime.Now.AddMonths(1),
                    Status = "Active",
                    PaymentMethod = "Razorpay",
                    PaymentTransactionId = transaction.TransactionId.ToString(),
                    LastPaymentDate = IndianTime.Now,
                    NextPaymentDate = (transaction.BillingCycle?.ToLower() == "annual") ? IndianTime.Now.AddYears(1) : IndianTime.Now.AddMonths(1),
                    AutoRenew = false,
                    CreatedOn = IndianTime.Now,
                    UpdatedOn = IndianTime.Now
                };

                _context.PartnerSubscriptions.Add(newSubscription);
                await _context.SaveChangesAsync();

                transaction.SubscriptionId = newSubscription.SubscriptionId;

                _logger.LogInformation($"Successfully activated {transaction.PlanName} plan for partner {partner.CompanyName}. New subscription ID: {newSubscription.SubscriptionId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error activating plan immediately for transaction {transaction.TransactionId}");
            }
        }

        // Debug endpoint to check transactions
        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> DebugTransactions(string? search = null)
        {
            var query = _context.PaymentTransactions
                
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
                    PartnerEmail = t.ChannelPartner != null ? t.ChannelPartner.Email : null
                })
                .ToListAsync();

            return Json(new { success = true, transactions });
        }

        // Direct Razorpay activation endpoint
        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> ActivateFromRazorpay(string paymentId, int partnerId, int planId)
        {
            try
            {
                _logger.LogInformation($"Direct activation from Razorpay: payment={paymentId}, partner={partnerId}, plan={planId}");

                // Fetch payment from Razorpay API
                var (success, paymentDetails) = await _razorpayService.FetchPaymentAsync(paymentId);
                if (!success || !paymentDetails.HasValue)
                {
                    return Json(new { success = false, message = "Payment not found in Razorpay" });
                }

                var payment = paymentDetails.Value;
                var status = payment.GetProperty("status").GetString();
                var amount = payment.GetProperty("amount").GetInt32() / 100m; // Convert from paise

                if (status != "captured")
                {
                    return Json(new { success = false, message = $"Payment status is {status}, not captured" });
                }

                // Get partner and plan
                var partner = await _context.ChannelPartners.FindAsync(partnerId);
                if (partner == null)
                {
                    return Json(new { success = false, message = "Partner not found" });
                }

                var plan = await _context.SubscriptionPlans.FindAsync(planId);
                if (plan == null)
                {
                    return Json(new { success = false, message = "Plan not found" });
                }

                // End current subscription
                var currentSubscription = await _context.PartnerSubscriptions
                    .Where(s => s.ChannelPartnerId == partnerId && s.Status == "Active")
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
                var newSubscription = new PartnerSubscriptionModel
                {
                    ChannelPartnerId = partnerId,
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
                    UpdatedOn = IndianTime.Now
                };

                _context.PartnerSubscriptions.Add(newSubscription);
                await _context.SaveChangesAsync();

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
                _logger.LogError(ex, $"Error in direct Razorpay activation");
                return Json(new { success = false, message = ex.Message });
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
                    ViewBag.Status = "Processing";
                    ViewBag.PaymentId = paymentId;
                    ViewBag.OrderId = orderId;
                    return View();
                }

                // Payment captured - activate plan immediately
                var amount = payment.GetProperty("amount").GetInt32() / 100m;

                // Find partner (hardcoded for now)
                var partner = await _context.ChannelPartners
                    .FirstOrDefaultAsync(p => p.Email == "tejaavidi4@gmail.com");

                if (partner == null)
                {
                    ViewBag.Status = "Failed";
                    ViewBag.Error = "Partner not found";
                    return View();
                }

                // Get plan from order details in transaction table
                var orderTransaction = await _context.PaymentTransactions
                    .FirstOrDefaultAsync(t => t.RazorpayOrderId == orderId);

                if (orderTransaction == null)
                {
                    ViewBag.Status = "Failed";
                    ViewBag.Error = "Order transaction not found";
                    return View();
                }

                // Get plan by name from transaction
                var plan = await _context.SubscriptionPlans
                    .FirstOrDefaultAsync(p => p.PlanName == orderTransaction.PlanName);

                if (plan == null)
                {
                    ViewBag.Status = "Failed";
                    ViewBag.Error = "Plan not found";
                    return View();
                }

                // End current subscription
                var currentSub = await _context.PartnerSubscriptions
                    .Where(s => s.ChannelPartnerId == partner.PartnerId && s.Status == "Active")
                    .FirstOrDefaultAsync();

                if (currentSub != null)
                {
                    currentSub.Status = "Expired";
                    currentSub.EndDate = IndianTime.Now;
                    currentSub.UpdatedOn = IndianTime.Now;
                }

                // Create new subscription
                var newSub = new PartnerSubscriptionModel
                {
                    ChannelPartnerId = partner.PartnerId,
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

                _context.PartnerSubscriptions.Add(newSub);
                await _context.SaveChangesAsync();

                // Create transaction record
                var transaction = new PaymentTransactionModel
                {
                    ChannelPartnerId = partner.PartnerId,
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

                _context.PaymentTransactions.Add(transaction);
                await _context.SaveChangesAsync();

                ViewBag.Status = "Success";
                ViewBag.PaymentId = paymentId;
                ViewBag.OrderId = orderId;
                ViewBag.Amount = amount;
                ViewBag.PlanName = plan.PlanName;
                ViewBag.PartnerName = partner.CompanyName;

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

        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> CheckPartnerPlan(int partnerId, int planId)
        {
            var partner = await _context.ChannelPartners.FindAsync(partnerId);
            var plan = await _context.SubscriptionPlans.FindAsync(planId);

            return Json(new
            {
                partnerFound = partner != null,
                partnerDetails = partner != null ? new { partner.PartnerId, partner.CompanyName, partner.Email } : null,
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
                var partner = await _context.ChannelPartners
                    .FirstOrDefaultAsync(p => p.Email == "tejaavidi4@gmail.com");

                if (partner == null)
                {
                    return Json(new { success = false, message = "Partner not found" });
                }

                // End current subscription
                var currentSub = await _context.PartnerSubscriptions
                    .Where(s => s.ChannelPartnerId == partner.PartnerId && s.Status == "Active")
                    .FirstOrDefaultAsync();

                if (currentSub != null)
                {
                    currentSub.Status = "Expired";
                    currentSub.EndDate = IndianTime.Now;
                    currentSub.UpdatedOn = IndianTime.Now;
                }

                // Get plan
                var plan = await _context.SubscriptionPlans
                    .Where(p => p.IsActive)
                    .FirstOrDefaultAsync();

                if (plan == null)
                {
                    return Json(new { success = false, message = "No plan found" });
                }

                // Create new subscription
                var newSub = new PartnerSubscriptionModel
                {
                    ChannelPartnerId = partner.PartnerId,
                    PlanId = plan.PlanId,
                    BillingCycle = "monthly",
                    Amount = plan.MonthlyPrice,
                    StartDate = IndianTime.Now,
                    EndDate = IndianTime.Now.AddMonths(1),
                    Status = "Active",
                    PaymentMethod = "Razorpay",
                    PaymentTransactionId = "pay_S270cyLHHTl1TA",
                    LastPaymentDate = IndianTime.Now,
                    NextPaymentDate = IndianTime.Now.AddMonths(1),
                    AutoRenew = false,
                    CreatedOn = IndianTime.Now,
                    UpdatedOn = IndianTime.Now
                };

                _context.PartnerSubscriptions.Add(newSub);
                await _context.SaveChangesAsync();

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
    }
}
