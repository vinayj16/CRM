using CRM.Helpers;
using Microsoft.AspNetCore.Mvc;
using CRM.Services;
using CRM.Models;
using CRM.Attributes;
using System.Security.Claims;
using System.Text.Json;

namespace CRM.Controllers
{
    [RoleAuthorize("Partner")]
    public class PartnerTransactionsController : Controller
    {
        private readonly RazorpayService _razorpayService;
        private readonly AppDbContext _context;
        private readonly ILogger<PartnerTransactionsController> _logger;

        public PartnerTransactionsController(RazorpayService razorpayService, AppDbContext context, ILogger<PartnerTransactionsController> logger)
        {
            _razorpayService = razorpayService;
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Resolves the ChannelPartner record for the current partner user.
        /// Prefers the ChannelPartnerId claim (always set on login), then falls back
        /// to the legacy UserId linkage on the ChannelPartner record. This prevents
        /// the "Channel partner not found" error when a partner's UserId linkage is
        /// missing or stale.
        /// </summary>
        private ChannelPartnerModel? GetChannelPartnerForUser(int partnerUserId)
        {
            // 1. Preferred: ChannelPartnerId claim set during login
            var cpIdClaim = User.FindFirst("ChannelPartnerId")?.Value;
            if (!string.IsNullOrEmpty(cpIdClaim) && int.TryParse(cpIdClaim, out int cpId) && cpId > 0)
            {
                var byClaim = _context.ChannelPartners.FirstOrDefault(cp => cp.PartnerId == cpId);
                if (byClaim != null)
                    return byClaim;
            }

            // 2. Fallback: legacy UserId linkage
            var byUser = _context.ChannelPartners.FirstOrDefault(cp => cp.UserId == partnerUserId);
            if (byUser != null)
                return byUser;

            // 3. Last resort: a partner record that references this user id anywhere
            return _context.ChannelPartners.FirstOrDefault(cp => cp.UserId == partnerUserId);
        }

        public async Task<IActionResult> Index(string? status, string? method, DateTime? fromDate, DateTime? toDate)
        {
            try
            {
                // Get current partner's user ID
                var partnerUserIdClaim = User.FindFirst("UserId")?.Value;
                if (string.IsNullOrEmpty(partnerUserIdClaim) || !int.TryParse(partnerUserIdClaim, out int partnerUserId))
                {
                    ViewBag.Error = "Partner user ID not found in session";
                    return View(new List<dynamic>());
                }

                // Resolve the channel partner robustly: prefer the ChannelPartnerId claim
                // (always set on login), then fall back to the UserId linkage.
                var channelPartner = GetChannelPartnerForUser(partnerUserId);
                if (channelPartner == null)
                {
                    ViewBag.Error = "Channel partner not found for this user";
                    return View(new List<dynamic>());
                }

                // Get partner's subscription transactions from database (removed Status filter)
                var partnerSubscriptions = _context.PaymentTransactions
                    .Where(st => st.ChannelPartnerId == channelPartner.PartnerId)
                    .Select(st => new { st.RazorpayPaymentId, st.Amount, st.CreatedOn })
                    .ToList();

                if (!partnerSubscriptions.Any())
                {
                    // No error message - just show empty state in view
                    return View(new List<dynamic>());
                }

                // Get all payments from Razorpay API
                List<dynamic> allPayments;
                List<dynamic> allRefunds;
                
                try
                {
                    allPayments = await _razorpayService.GetAllPaymentsAsync();
                }
                catch (Exception ex)
                {
                    ViewBag.Error = $"Unable to connect to Razorpay API: {ex.Message}. Please check your internet connection or try again later.";
                    return View(new List<dynamic>());
                }
                
                try
                {
                    allRefunds = await _razorpayService.GetAllRefundsAsync();
                }
                catch (Exception)
                {
                    // If refunds fail, continue with empty list
                    allRefunds = new List<dynamic>();
                }
                
                // Show ALL transactions for this partner, not just "Success" ones
                var partnerPaymentIds = partnerSubscriptions.Select(ps => ps.RazorpayPaymentId).Where(id => !string.IsNullOrEmpty(id)).ToHashSet();
                var partnerPayments = allPayments.Where(p => {
                    var paymentId = p.id?.ToString();
                    return !string.IsNullOrEmpty(paymentId) && partnerPaymentIds.Contains(paymentId);
                }).ToList();
                
                // Filter refunds that match partner's payment IDs
                var partnerRefunds = allRefunds.Where(r => {
                    var paymentId = r.payment_id?.ToString();
                    return !string.IsNullOrEmpty(paymentId) && partnerPaymentIds.Contains(paymentId);
                }).ToList();
                
                // Get payment IDs that have been refunded
                var refundedPaymentIds = partnerRefunds.Select(r => r.payment_id?.ToString()).Where(id => !string.IsNullOrEmpty(id)).ToHashSet();
                
                // Combine payments and refunds into a unified list
                var allTransactions = new List<dynamic>();
                
                // Add only payments that haven't been refunded
                foreach (var payment in partnerPayments)
                {
                    var paymentId = payment.id?.ToString();
                    if (!refundedPaymentIds.Contains(paymentId))
                    {
                        allTransactions.Add(new {
                            id = payment.id,
                            amount = payment.amount,
                            status = payment.status,
                            method = payment.method,
                            created_at = payment.created_at,
                            type = "payment"
                        });
                    }
                }
                
                // Add all refunds (these represent the final state)
                foreach (var refund in partnerRefunds)
                {
                    // Use original payment ID instead of refund ID for display
                    var displayId = refund.payment_id?.ToString() ?? refund.id?.ToString();
                    
                    allTransactions.Add(new {
                        id = displayId,
                        amount = refund.amount,
                        status = refund.status,
                        method = "refund",
                        created_at = refund.created_at,
                        type = "refund"
                    });
                }
                
                // Apply additional filters
                var filteredTransactions = allTransactions.AsEnumerable();
                
                if (!string.IsNullOrEmpty(status))
                    filteredTransactions = filteredTransactions.Where(t => t.status?.ToString() == status);
                
                if (!string.IsNullOrEmpty(method))
                    filteredTransactions = filteredTransactions.Where(t => t.method?.ToString() == method);
                
                if (fromDate.HasValue)
                {
                    var fromTimestamp = ((DateTimeOffset)fromDate.Value).ToUnixTimeSeconds();
                    filteredTransactions = filteredTransactions.Where(t => (long)t.created_at >= fromTimestamp);
                }
                
                if (toDate.HasValue)
                {
                    var toTimestamp = ((DateTimeOffset)toDate.Value.AddDays(1)).ToUnixTimeSeconds();
                    filteredTransactions = filteredTransactions.Where(t => (long)t.created_at <= toTimestamp);
                }
                
                // Sort by creation date (newest first)
                var finalTransactions = filteredTransactions.OrderByDescending(t => (long)t.created_at).ToList();
                
                ViewBag.Status = status;
                ViewBag.Method = method;
                ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
                ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
                ViewBag.PartnerUserId = partnerUserId;
                
                return View(finalTransactions);
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Failed to load transactions: {ex.Message}";
                return View(new List<dynamic>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetPaymentDetails(string paymentId)
        {
            try
            {
                // Check if it's a refund ID (starts with rfnd_)
                if (paymentId.StartsWith("rfnd_"))
                {
                    // Find the refund in our transaction list
                    var partnerUserIdClaim = User.FindFirst("UserId")?.Value;
                    if (int.TryParse(partnerUserIdClaim, out int partnerUserId))
                    {
                        var channelPartner = GetChannelPartnerForUser(partnerUserId);
                        if (channelPartner != null)
                        {
                            var partnerSubscriptions = _context.PaymentTransactions
                                .Where(st => st.ChannelPartnerId == channelPartner.PartnerId)
                                .Select(st => st.RazorpayPaymentId)
                                .Where(id => !string.IsNullOrEmpty(id))
                                .ToHashSet();
                            
                            var allRefunds = await _razorpayService.GetAllRefundsAsync();
                            var refund = allRefunds.FirstOrDefault(r => r.id?.ToString() == paymentId);
                            
                            if (refund != null)
                            {
                                // Get original payment details to show refund destination
                                var originalPaymentId = refund.payment_id?.ToString();
                                dynamic originalPayment = null;
                                
                                if (!string.IsNullOrEmpty(originalPaymentId))
                                {
                                    try
                                    {
                                        originalPayment = await _razorpayService.GetPaymentDetailsAsync(originalPaymentId);
                                    }
                                    catch { }
                                }
                                
                                return Json(new { 
                                    success = true, 
                                    data = new {
                                        id = refund.id,
                                        amount = refund.amount,
                                        status = refund.status,
                                        method = "refund",
                                        created_at = refund.created_at,
                                        email = originalPayment?.email ?? "N/A",
                                        contact = originalPayment?.contact ?? "N/A",
                                        type = "refund",
                                        original_payment_id = originalPaymentId,
                                        refund_method = originalPayment?.method ?? "N/A",
                                        card = originalPayment?.card
                                    }
                                });
                            }
                        }
                    }
                    
                    return Json(new { success = false, message = "Refund details not found" });
                }
                
                var paymentDetails = await _razorpayService.GetPaymentDetailsAsync(paymentId);
                return Json(new { success = true, data = paymentDetails });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Transaction details not available" });
            }
        }

        // Razorpay Webhook Handler for Payment Status Updates
        [HttpPost]
        [Route("/webhook/razorpay-payments")]
        public async Task<IActionResult> RazorpayPaymentWebhook()
        {
            try
            {
                var body = await new StreamReader(Request.Body).ReadToEndAsync();
                var signature = Request.Headers["X-Razorpay-Signature"].FirstOrDefault();

                if (string.IsNullOrEmpty(signature) || !_razorpayService.VerifyWebhookSignature(body, signature))
                {
                    _logger.LogWarning("Payment webhook signature verification failed");
                    return BadRequest("Invalid signature");
                }

                var webhook = JsonSerializer.Deserialize<JsonElement>(body);
                var eventType = webhook.GetProperty("event").GetString();
                var paymentEntity = webhook.GetProperty("payload").GetProperty("payment").GetProperty("entity");
                var paymentId = paymentEntity.GetProperty("id").GetString();
                var status = paymentEntity.GetProperty("status").GetString();

                _logger.LogInformation($"Payment webhook: {eventType}, Payment ID: {paymentId}, Status: {status}");

                // Update PaymentTransaction status to match Razorpay status
                var transaction = await _context.PaymentTransactions
                    .Include(t => t.Subscription)
                    .FirstOrDefaultAsync(t => t.RazorpayPaymentId == paymentId);

                if (transaction != null)
                {
                    var oldStatus = transaction.Status;
                    
                    // Map Razorpay status to our status
                    transaction.Status = status switch
                    {
                        "captured" => "Success",
                        "failed" => "Failed",
                        "created" => "Pending",
                        "authorized" => "Authorized",
                        "refunded" => "Refunded",
                        _ => transaction.Status
                    };
                    
                    transaction.UpdatedOn = IndianTime.Now;
                    
                    // Only activate subscription when payment is captured (not just created)
                    if (status == "captured" && oldStatus != "Success" && transaction.Subscription != null)
                    {
                        await ActivateSubscriptionOnCapture(transaction.Subscription);
                    }
                    _context.PaymentTransactions.Update(transaction);
                    
                    await _context.SaveChangesAsync();
                    
                    _logger.LogInformation($"Updated transaction {transaction.TransactionId} status from {oldStatus} to {transaction.Status}");
                }

                return Ok(new { status = "processed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing payment webhook");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        private async Task ActivateSubscriptionOnCapture(PartnerSubscriptionModel subscription)
        {
            try
            {
                // Only activate if subscription is not already active
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

        [HttpGet]
        public async Task<IActionResult> ExportTransactions(string format = "csv", string? status = null, string? method = null, DateTime? fromDate = null, DateTime? toDate = null)
        {
            try
            {
                var partnerUserIdClaim = User.FindFirst("UserId")?.Value;
                if (string.IsNullOrEmpty(partnerUserIdClaim) || !int.TryParse(partnerUserIdClaim, out int partnerUserId))
                {
                    return BadRequest("Partner user ID not found");
                }

                var channelPartner = GetChannelPartnerForUser(partnerUserId);
                if (channelPartner == null)
                {
                    return BadRequest("Channel partner not found");
                }

                // Get partner's subscription transactions (removed Status filter)
                var partnerSubscriptions = _context.PaymentTransactions
                    .Where(st => st.ChannelPartnerId == channelPartner.PartnerId)
                    .Select(st => st.RazorpayPaymentId)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToHashSet();

                var allPayments = await _razorpayService.GetAllPaymentsAsync();
                var partnerPayments = allPayments.Where(p => {
                    var paymentId = p.id?.ToString();
                    return !string.IsNullOrEmpty(paymentId) && partnerSubscriptions.Contains(paymentId);
                }).ToList();
                
                // Apply filters
                if (!string.IsNullOrEmpty(status))
                    partnerPayments = partnerPayments.Where(p => p.status?.ToString() == status).ToList();
                
                if (!string.IsNullOrEmpty(method))
                    partnerPayments = partnerPayments.Where(p => p.method?.ToString() == method).ToList();
                
                if (fromDate.HasValue)
                {
                    var fromTimestamp = ((DateTimeOffset)fromDate.Value).ToUnixTimeSeconds();
                    partnerPayments = partnerPayments.Where(p => (long)p.created_at >= fromTimestamp).ToList();
                }
                
                if (toDate.HasValue)
                {
                    var toTimestamp = ((DateTimeOffset)toDate.Value.AddDays(1)).ToUnixTimeSeconds();
                    partnerPayments = partnerPayments.Where(p => (long)p.created_at <= toTimestamp).ToList();
                }

                var csv = GenerateCSV(partnerPayments);
                return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"MyTransactions_{IndianTime.Now:yyyy-MM-dd}.csv");
            }
            catch (Exception ex)
            {
                return BadRequest("Failed to export transactions");
            }
        }

        private string GenerateCSV(List<dynamic> payments)
        {
            var csv = "Date,Payment ID,Amount (INR),Status,Method,Description\n";
            
            foreach (var payment in payments)
            {
                var date = DateTime.UnixEpoch.AddSeconds((long)payment.created_at).ToString("yyyy-MM-dd HH:mm");
                var amount = ((long)payment.amount / 100m).ToString("N2");
                
                csv += $"{date}," +
                       $"{payment.id}," +
                       $"{amount}," +
                       $"{payment.status}," +
                       $"{payment.method ?? "N/A"}," +
                       $"Payment Transaction\n";
            }
            
            return csv;
        }

        // Sync payment statuses with Razorpay (Admin function)
        [HttpPost]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> SyncPaymentStatuses()
        {
            try
            {
                var transactions = await _context.PaymentTransactions
                    .Where(t => !string.IsNullOrEmpty(t.RazorpayPaymentId) && t.Status != "Success")
                    .ToListAsync();

                int updated = 0;
                foreach (var transaction in transactions)
                {
                    try
                    {
                        var paymentDetails = await _razorpayService.GetPaymentDetailsAsync(transaction.RazorpayPaymentId!);
                        var razorpayStatus = paymentDetails.status?.ToString();
                        
                        if (!string.IsNullOrEmpty(razorpayStatus))
                        {
                            var newStatus = razorpayStatus switch
                            {
                                "captured" => "Success",
                                "failed" => "Failed",
                                "created" => "Pending",
                                "authorized" => "Authorized",
                                "refunded" => "Refunded",
                                _ => transaction.Status
                            };
                            
                            if (newStatus != transaction.Status)
                            {
                                transaction.Status = newStatus;
                                transaction.UpdatedOn = IndianTime.Now;
                                _context.PaymentTransactions.Update(transaction);
                                updated++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Failed to sync status for payment {transaction.RazorpayPaymentId}");
                    }
                }

                await _context.SaveChangesAsync();
                
                return Json(new { success = true, message = $"Synced {updated} payment statuses" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing payment statuses");
                return Json(new { success = false, message = "Failed to sync payment statuses" });
            }
        }
    }
}