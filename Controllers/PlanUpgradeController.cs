using CRM.Helpers;
using Microsoft.AspNetCore.Mvc;
using CRM.Attributes;
using CRM.Models;
using CRM.Services;
using System.Text.Json;

namespace CRM.Controllers
{
    [RoleAuthorize("Admin")]
    public class PlanUpgradeController : Controller
    {
        private readonly AppDbContext _context;
        private readonly RazorpayService _razorpayService;
        private readonly IChannelPartnerContextService _contextService;
        private readonly ILogger<PlanUpgradeController> _logger;

        public PlanUpgradeController(
            AppDbContext context,
            RazorpayService razorpayService,
            IChannelPartnerContextService contextService,
            ILogger<PlanUpgradeController> logger)
        {
            _context = context;
            _razorpayService = razorpayService;
            _contextService = contextService;
            _logger = logger;
        }

        private (int? userId, string? role, int? channelPartnerId) GetCurrentUserContext()
        {
            return _contextService.GetCurrentUserContext(HttpContext);
        }

        [HttpGet]
        public async Task<IActionResult> GetUpgradeOptions(int partnerId)
        {
            try
            {
                _logger.LogInformation($"GetUpgradeOptions called for partnerId: {partnerId}");
                
                // Check if services are available
                if (_context == null)
                {
                    return Json(new { success = false, message = "Database context not available" });
                }
                
                var partner = await _context.ChannelPartners.FindAsync(partnerId);
                if (partner == null)
                {
                    _logger.LogWarning($"Partner not found: {partnerId}");
                    return Json(new { success = false, message = "Partner not found" });
                }

                var currentSubscription = await _context.PartnerSubscriptions
                    .Where(s => s.ChannelPartnerId == partnerId && s.Status == "Active")
                    .FirstOrDefaultAsync();

                var availablePlans = await _context.SubscriptionPlans
                    .Where(p => p.IsActive == true)
                    .OrderBy(p => p.SortOrder)
                    .ThenBy(p => p.MonthlyPrice)
                    .ToListAsync();

                _logger.LogInformation($"Found {availablePlans.Count} available plans");

                return Json(new
                {
                    success = true,
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
                        amount = currentSubscription.Amount,
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
                        maxStorageGB = p.MaxStorageGB,
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
                _logger.LogError(ex, "Error getting upgrade options for partner {PartnerId}", partnerId);
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
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

                switch (upgradeType.ToLower())
                {
                    case "existing":
                        return await CalculateExistingPlanUpgrade(currentSubscription, newPlan, newAmount, billingCycle);
                    
                    case "immediate":
                        return CalculateImmediateUpgrade(currentSubscription, newPlan, newAmount, billingCycle);
                    
                    case "scheduled":
                        return CalculateScheduledPlan(currentSubscription, newPlan, newAmount, billingCycle);
                    
                    default:
                        return Json(new { success = false, message = "Invalid upgrade type" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating upgrade for partner {PartnerId}", partnerId);
                return Json(new { success = false, message = "Error calculating upgrade" });
            }
        }

        private async Task<IActionResult> CalculateExistingPlanUpgrade(PartnerSubscriptionModel currentSubscription, SubscriptionPlanModel newPlan, decimal newAmount, string billingCycle)
        {
            if (currentSubscription == null)
                return Json(new { success = false, message = "No active subscription found" });

            // Calculate remaining amount from current plan
            var totalDays = (currentSubscription.EndDate - currentSubscription.StartDate).Days;
            var usedDays = (IndianTime.Now - currentSubscription.StartDate).Days;
            var remainingDays = Math.Max(0, totalDays - usedDays);
            
            var perDayRate = currentSubscription.Amount / totalDays;
            var remainingAmount = perDayRate * remainingDays;

            // Calculate upgrade plan per-day rate
            var upgradeDays = billingCycle.ToLower() == "annual" ? 365 : 30;
            var upgradePerDayRate = newAmount / upgradeDays;

            // Convert remaining amount to upgrade days
            var convertedDays = (int)(remainingAmount / upgradePerDayRate);
            var remainingAfterConversion = remainingAmount - (convertedDays * upgradePerDayRate);

            // Add 1 extra day if any remaining amount > 0
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
                    currentAmount = currentSubscription.Amount,
                    remainingDays = remainingDays,
                    remainingAmount = Math.Round(remainingAmount, 2),
                    newPlan = newPlan.PlanName,
                    newAmount = newAmount,
                    upgradePerDayRate = Math.Round(upgradePerDayRate, 2),
                    convertedDays = convertedDays,
                    upgradeStartDate = upgradeStartDate.ToString("yyyy-MM-dd"),
                    upgradeEndDate = upgradeEndDate.ToString("yyyy-MM-dd"),
                    paymentRequired = 0 // No additional payment for existing plan upgrade
                }
            });
        }

        private IActionResult CalculateImmediateUpgrade(PartnerSubscriptionModel currentSubscription, SubscriptionPlanModel newPlan, decimal newAmount, string billingCycle)
        {
            decimal adjustedAmount = newAmount;
            decimal creditAmount = 0;

            if (currentSubscription != null)
            {
                // Calculate remaining amount from current plan
                var totalDays = (currentSubscription.EndDate - currentSubscription.StartDate).Days;
                var usedDays = (IndianTime.Now - currentSubscription.StartDate).Days;
                var remainingDays = Math.Max(0, totalDays - usedDays);
                
                var perDayRate = currentSubscription.Amount / totalDays;
                creditAmount = perDayRate * remainingDays;
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
                    paymentRequired = newAmount // Full payment required for scheduled plan
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

                // Create Razorpay order
                var orderId = await _razorpayService.CreateOrderAsync(amount, "INR", $"upgrade_{partnerId}_{planId}_{upgradeType}");

                // Store upgrade request in database for tracking
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

                // Generate payment link (in real implementation, you'd use Razorpay's payment link API)
                var paymentLink = $"https://checkout.razorpay.com/v1/checkout.js?order_id={orderId}";

                return Json(new
                {
                    success = true,
                    paymentLink = paymentLink,
                    orderId = orderId,
                    amount = amount * 100, // Razorpay expects amount in paise
                    partnerEmail = partner.Email,
                    planName = plan.PlanName,
                    billingCycle = billingCycle,
                    upgradeType = upgradeType
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating payment link for partner {PartnerId}", partnerId);
                return Json(new { success = false, message = "Error creating payment link" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ProcessUpgrade(string razorpayPaymentId, string razorpayOrderId, string razorpaySignature, string upgradeType)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            
            try
            {
                // Verify payment signature
                if (!_razorpayService.VerifyPaymentSignature(razorpayPaymentId, razorpayOrderId, razorpaySignature))
                {
                    return Json(new { success = false, message = "Payment verification failed" });
                }

                // Find the upgrade request
                var upgradeRequest = await _context.PaymentTransactions
                    .Where(t => t.RazorpayOrderId == razorpayOrderId && t.Status == "Pending")
                    .FirstOrDefaultAsync();

                if (upgradeRequest == null)
                {
                    return Json(new { success = false, message = "Upgrade request not found" });
                }

                // Update payment transaction
                upgradeRequest.Status = "Success";
                upgradeRequest.RazorpayPaymentId = razorpayPaymentId;
                upgradeRequest.RazorpaySignature = razorpaySignature;
                upgradeRequest.CompletedDate = IndianTime.Now;
                _context.PaymentTransactions.Update(upgradeRequest);

                // Process the upgrade based on type
                var result = await ProcessUpgradeByType(upgradeRequest, upgradeType);
                if (!result.success)
                {

                    return Json(result);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new { success = true, message = "Upgrade processed successfully" });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error processing upgrade for order {OrderId}", razorpayOrderId);
                return Json(new { success = false, message = "Error processing upgrade" });
            }
        }

        [HttpPost]
        [Route("/webhook/upgrade-payment")]
        public async Task<IActionResult> UpgradePaymentWebhook()
        {
            try
            {
                var body = await new StreamReader(Request.Body).ReadToEndAsync();
                var signature = Request.Headers["X-Razorpay-Signature"].FirstOrDefault();

                if (string.IsNullOrEmpty(signature) || !_razorpayService.VerifyWebhookSignature(body, signature))
                {
                    _logger.LogWarning("Upgrade webhook signature verification failed");
                    return BadRequest("Invalid signature");
                }

                var webhook = JsonSerializer.Deserialize<JsonElement>(body);
                var eventType = webhook.GetProperty("event").GetString();

                _logger.LogInformation($"Processing upgrade webhook event: {eventType}");

                if (eventType == "payment.captured")
                {
                    var paymentEntity = webhook.GetProperty("payload").GetProperty("payment").GetProperty("entity");
                    var paymentId = paymentEntity.GetProperty("id").GetString();
                    var orderId = paymentEntity.GetProperty("order_id").GetString();

                    // Find upgrade transaction
                    var upgradeTransaction = await _context.PaymentTransactions
                        .Where(t => t.RazorpayOrderId == orderId && t.TransactionType.StartsWith("Upgrade_"))
                        .FirstOrDefaultAsync();

                    if (upgradeTransaction != null && upgradeTransaction.Status == "Pending")
                    {
                        // Extract upgrade type from transaction type
                        var upgradeType = upgradeTransaction.TransactionType.Replace("Upgrade_", "").ToLower();
                        
                        // Process the upgrade
                        upgradeTransaction.Status = "Success";
                        upgradeTransaction.RazorpayPaymentId = paymentId;
                        upgradeTransaction.CompletedDate = IndianTime.Now;

                        _context.PaymentTransactions.Update(upgradeTransaction);

                        var result = await ProcessUpgradeByType(upgradeTransaction, upgradeType);
                        if (result.success)
                        {
                            await _context.SaveChangesAsync();
                            _logger.LogInformation($"Upgrade processed successfully for order {orderId}");
                        }
                        else
                        {
                            _logger.LogError($"Failed to process upgrade for order {orderId}: {result.message}");
                        }
                    }
                }

                return Ok(new { status = "processed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing upgrade webhook");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        private async Task<(bool success, string message)> ProcessUpgradeByType(PaymentTransactionModel upgradeRequest, string upgradeType)
        {
            var partnerId = upgradeRequest.ChannelPartnerId;
            var planName = upgradeRequest.PlanName;
            var billingCycle = upgradeRequest.BillingCycle;

            // Find the plan
            var plan = await _context.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.PlanName == planName);

            if (plan == null)
                return (false, "Plan not found");

            var currentSubscription = await _context.PartnerSubscriptions
                .Where(s => s.ChannelPartnerId == partnerId && s.Status == "Active")
                .FirstOrDefaultAsync();

            switch (upgradeType.ToLower())
            {
                case "existing":
                    return await ProcessExistingPlanUpgrade(currentSubscription, plan, billingCycle, upgradeRequest);
                
                case "immediate":
                    return await ProcessImmediateUpgrade(currentSubscription, plan, billingCycle, upgradeRequest);
                
                case "scheduled":
                    return await ProcessScheduledPlan(currentSubscription, plan, billingCycle, upgradeRequest);
                
                default:
                    return (false, "Invalid upgrade type");
            }
        }

        [HttpPost]
        public async Task<IActionResult> ProcessRefund(int transactionId, string refundReason)
        {
            try
            {
                var transaction = await _context.PaymentTransactions
                    .Include(t => t.ChannelPartner)
                    .FirstOrDefaultAsync(t => t.TransactionId == transactionId && t.Status == "Success");

                if (transaction == null)
                    return Json(new { success = false, message = "Transaction not found or not eligible for refund" });

                if (string.IsNullOrEmpty(transaction.RazorpayPaymentId))
                    return Json(new { success = false, message = "No Razorpay payment ID found" });

                // Process Razorpay refund
                var (success, refundId, message) = await _razorpayService.CreateRefundAsync(
                    transaction.RazorpayPaymentId, 
                    transaction.Amount, 
                    refundReason
                );

                if (success)
                {
                    // Create refund transaction record
                    var refundTransaction = new PaymentTransactionModel
                    {
                        ChannelPartnerId = transaction.ChannelPartnerId,
                        SubscriptionId = transaction.SubscriptionId,
                        TransactionReference = refundId,
                        RazorpayPaymentId = refundId,
                        RazorpayOrderId = transaction.RazorpayOrderId,
                        Amount = transaction.Amount,
                        Currency = transaction.Currency ?? "INR",
                        TransactionType = "Refund",
                        Status = "Success",
                        PaymentMethod = "Razorpay",
                        TransactionDate = IndianTime.Now,
                        CompletedDate = IndianTime.Now,
                        Description = $"Refund for {transaction.Description} - {refundReason}",
                        PlanName = transaction.PlanName,
                        BillingCycle = transaction.BillingCycle,
                        NetAmount = transaction.Amount,
                        CreatedOn = IndianTime.Now
                    };

                    _context.PaymentTransactions.Add(refundTransaction);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"Refund processed for transaction {transactionId}. Refund ID: {refundId}");

                    return Json(new
                    {
                        success = true,
                        refundId = refundId,
                        message = $"Refund of ₹{transaction.Amount:N0} processed successfully. {message}"
                    });
                }
                else
                {
                    _logger.LogError($"Razorpay refund failed for transaction {transactionId}: {message}");
                    return Json(new { success = false, message = $"Refund failed: {message}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing refund for transaction {TransactionId}", transactionId);
                return Json(new { success = false, message = "Error processing refund" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetRefundableTransactions(int partnerId)
        {
            try
            {
                var refundableTransactions = await _context.PaymentTransactions
                    .Where(t => t.ChannelPartnerId == partnerId && 
                               t.Status == "Success" && 
                               t.TransactionType.StartsWith("Upgrade_") &&
                               !string.IsNullOrEmpty(t.RazorpayPaymentId))
                    .OrderByDescending(t => t.TransactionDate)
                    .Select(t => new
                    {
                        transactionId = t.TransactionId,
                        amount = t.Amount,
                        planName = t.PlanName,
                        billingCycle = t.BillingCycle,
                        transactionDate = t.TransactionDate.ToString("dd/MM/yyyy HH:mm"),
                        description = t.Description,
                        razorpayPaymentId = t.RazorpayPaymentId
                    })
                    .ToListAsync();

                return Json(new { success = true, transactions = refundableTransactions });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting refundable transactions for partner {PartnerId}", partnerId);
                return Json(new { success = false, message = "Error loading transactions" });
            }
        }

        private async Task<(bool success, string message)> ProcessExistingPlanUpgrade(PartnerSubscriptionModel currentSubscription, SubscriptionPlanModel newPlan, string billingCycle, PaymentTransactionModel upgradeRequest)
        {
            if (currentSubscription == null)
                return (false, "No active subscription found");

            // Calculate upgrade days (same logic as calculation)
            var totalDays = (currentSubscription.EndDate - currentSubscription.StartDate).Days;
            var usedDays = (IndianTime.Now - currentSubscription.StartDate).Days;
            var remainingDays = Math.Max(0, totalDays - usedDays);
            
            var perDayRate = currentSubscription.Amount / totalDays;
            var remainingAmount = perDayRate * remainingDays;

            var upgradeDays = billingCycle.ToLower() == "annual" ? 365 : 30;
            var upgradePerDayRate = (billingCycle.ToLower() == "annual" ? newPlan.YearlyPrice : newPlan.MonthlyPrice) / upgradeDays;

            var convertedDays = (int)(remainingAmount / upgradePerDayRate);
            var remainingAfterConversion = remainingAmount - (convertedDays * upgradePerDayRate);

            if (remainingAfterConversion > 0)
                convertedDays += 1;

            // Stop current subscription
            currentSubscription.Status = "Expired";
            currentSubscription.EndDate = IndianTime.Now;
            currentSubscription.UpdatedOn = IndianTime.Now;

            // Create new subscription with converted days
            var newSubscription = new PartnerSubscriptionModel
            {
                ChannelPartnerId = upgradeRequest.ChannelPartnerId,
                PlanId = newPlan.PlanId,
                BillingCycle = billingCycle,
                Amount = billingCycle.ToLower() == "annual" ? newPlan.YearlyPrice : newPlan.MonthlyPrice,
                StartDate = IndianTime.Now,
                EndDate = IndianTime.Now.AddDays(convertedDays),
                Status = "Active",
                PaymentMethod = "Razorpay",
                PaymentTransactionId = upgradeRequest.TransactionId.ToString(),
                LastPaymentDate = IndianTime.Now,
                NextPaymentDate = IndianTime.Now.AddDays(convertedDays),
                AutoRenew = false,
                CreatedOn = IndianTime.Now
            };

            _context.PartnerSubscriptions.Add(newSubscription);
            upgradeRequest.SubscriptionId = newSubscription.SubscriptionId;

            return (true, $"Existing plan upgraded to {newPlan.PlanName} for {convertedDays} days");
        }

        private async Task<(bool success, string message)> ProcessImmediateUpgrade(PartnerSubscriptionModel currentSubscription, SubscriptionPlanModel newPlan, string billingCycle, PaymentTransactionModel upgradeRequest)
        {
            // Stop current subscription if exists
            if (currentSubscription != null)
            {
                currentSubscription.Status = "Expired";
                currentSubscription.EndDate = IndianTime.Now;
                currentSubscription.UpdatedOn = IndianTime.Now;
            }

            // Create new subscription
            var startDate = IndianTime.Now;
            var endDate = billingCycle.ToLower() == "annual" ? startDate.AddYears(1) : startDate.AddMonths(1);

            var newSubscription = new PartnerSubscriptionModel
            {
                ChannelPartnerId = upgradeRequest.ChannelPartnerId,
                PlanId = newPlan.PlanId,
                BillingCycle = billingCycle,
                Amount = billingCycle.ToLower() == "annual" ? newPlan.YearlyPrice : newPlan.MonthlyPrice,
                StartDate = startDate,
                EndDate = endDate,
                Status = "Active",
                PaymentMethod = "Razorpay",
                PaymentTransactionId = upgradeRequest.TransactionId.ToString(),
                LastPaymentDate = IndianTime.Now,
                NextPaymentDate = endDate,
                AutoRenew = false,
                CreatedOn = IndianTime.Now
            };

            _context.PartnerSubscriptions.Add(newSubscription);
            upgradeRequest.SubscriptionId = newSubscription.SubscriptionId;

            return (true, $"Immediate upgrade to {newPlan.PlanName} activated");
        }

        private async Task<(bool success, string message)> ProcessScheduledPlan(PartnerSubscriptionModel currentSubscription, SubscriptionPlanModel newPlan, string billingCycle, PaymentTransactionModel upgradeRequest)
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

            var scheduledSubscription = new PartnerSubscriptionModel
            {
                ChannelPartnerId = upgradeRequest.ChannelPartnerId,
                PlanId = newPlan.PlanId,
                BillingCycle = billingCycle,
                Amount = billingCycle.ToLower() == "annual" ? newPlan.YearlyPrice : newPlan.MonthlyPrice,
                StartDate = startDate,
                EndDate = endDate,
                Status = "Scheduled",
                PaymentMethod = "Razorpay",
                PaymentTransactionId = upgradeRequest.TransactionId.ToString(),
                LastPaymentDate = IndianTime.Now,
                NextPaymentDate = endDate,
                AutoRenew = false,
                CreatedOn = IndianTime.Now
            };

            _context.PartnerSubscriptions.Add(scheduledSubscription);
            upgradeRequest.SubscriptionId = scheduledSubscription.SubscriptionId;

            return (true, $"Scheduled plan {newPlan.PlanName} will activate on {startDate:dd/MM/yyyy}");
        }
    }
}
