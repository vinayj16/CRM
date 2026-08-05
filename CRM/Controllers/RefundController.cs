using CRM.Helpers;
using CRM.Attributes;
using CRM.Models;
using CRM.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CRM.Controllers
{
    [RoleAuthorize("Admin")]
    public class RefundController : Controller
    {
        private readonly AppDbContext _context;
        private readonly RazorpayService _razorpayService;
        private readonly ILogger<RefundController> _logger;

        public RefundController(AppDbContext context, RazorpayService razorpayService, ILogger<RefundController> logger)
        {
            _context = context;
            _razorpayService = razorpayService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> ProcessRefund(int subscriptionId)
        {
            try
            {
            var subscription = await _context.PartnerSubscriptions
                .Include(s => s.ChannelPartner)
                .FirstOrDefaultAsync(s => s.SubscriptionId == subscriptionId);

                if (subscription == null)
                    return Json(new { success = false, message = "Subscription not found" });

                // Find the payment transaction
                var transaction = await _context.PaymentTransactions
                    .Where(t => t.SubscriptionId == subscriptionId && t.Status == "Success")
                    .OrderByDescending(t => t.TransactionDate)
                    .FirstOrDefaultAsync();

                if (transaction?.RazorpayPaymentId == null)
                    return Json(new { success = false, message = "Payment transaction not found" });

                // Process refund through Razorpay
                var (success, refundId, message) = await _razorpayService.CreateRefundAsync(
                    transaction.RazorpayPaymentId, 
                    subscription.Amount, 
                    "Admin processed refund"
                );

                if (success)
                {
                    // Update subscription status
                    subscription.CancellationReason = $"Refund Processed: ₹{subscription.Amount} - Refund ID: {refundId}";
                    subscription.UpdatedOn = IndianTime.Now;

                    // Create refund transaction record
                    var refundTransaction = new PaymentTransactionModel
                    {
                        ChannelPartnerId = subscription.ChannelPartnerId,
                        SubscriptionId = subscriptionId,
                        TransactionReference = refundId,
                        Amount = subscription.Amount,
                        Currency = "INR",
                        TransactionType = "Refund",
                        Status = "Success",
                        PaymentMethod = "Razorpay",
                        TransactionDate = IndianTime.Now,
                        CompletedDate = IndianTime.Now,
                        Description = $"Refund for {subscription.Plan?.PlanName} - {subscription.ChannelPartner?.CompanyName}",
                        CreatedOn = IndianTime.Now
                    };

                    _context.PaymentTransactions.Add(refundTransaction);
                    await _context.SaveChangesAsync();

                    return Json(new { success = true, message = $"Refund of ₹{subscription.Amount} processed successfully to original payment method" });
                }
                else
                {
                    return Json(new { success = false, message = $"Refund failed: {message}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing refund for subscription {subscriptionId}");
                return Json(new { success = false, message = "Error processing refund" });
            }
        }
    }
}