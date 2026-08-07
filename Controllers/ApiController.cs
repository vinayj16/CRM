using CRM.Helpers;
using CRM.Models;
using CRM.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace CRM.Controllers
{
    [Route("api")]
    [ApiController]
    public class ApiController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly RazorpayService _razorpayService;
        private readonly ILogger<ApiController> _logger;

        public ApiController(AppDbContext db, IConfiguration config, RazorpayService razorpayService, ILogger<ApiController> logger)
        {
            _db = db;
            _config = config;
            _razorpayService = razorpayService;
            _logger = logger;
        }

        /// <summary>
        /// API Login - Returns JWT token
        /// POST /api/login
        /// </summary>
        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            var user = _db.Users.FirstOrDefault(u => u.Username == request.Username);
            
            if (user == null || !PasswordHelper.VerifyPassword(request.Password, user.Password))
            {
                return BadRequest(new { success = false, message = "Invalid credentials" });
            }

            var token = GenerateJwtToken(user);

            return Ok(new
            {
                success = true,
                message = "Login successful",
                token = token,
                user = new
                {
                    userId = user.UserId,
                    username = user.Username,
                    email = user.Email,
                    role = user.Role,
                    channelPartnerId = user.ChannelPartnerId
                }
            });
        }

        /// <summary>
        /// Get Subscription Plans
        /// GET /api/subscription/plans
        /// </summary>
        [HttpGet("subscription/plans")]
        public async Task<IActionResult> GetPlans()
        {
            var plans = await _db.SubscriptionPlans
                .Where(p => p.IsActive)
                .OrderBy(p => p.SortOrder)
                .Select(p => new
                {
                    p.PlanId,
                    p.PlanName,
                    p.MonthlyPrice,
                    p.YearlyPrice,
                    p.MaxLeadsPerMonth,
                    p.MaxAgents,
                    p.MaxStorageGB,
                    p.Description,
                    p.HasWhatsAppIntegration,
                    p.HasFacebookIntegration,
                    p.HasAdvancedReports,
                    p.SupportLevel
                })
                .ToListAsync();

            return Ok(new { success = true, data = plans });
        }

        /// <summary>
        /// Create Razorpay Order
        /// POST /api/subscription/create-order
        /// </summary>
        [HttpPost("subscription/create-order")]
        public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
        {
            try
            {
                var authHeader = Request.Headers["Authorization"].ToString();
                var user = ValidateToken(authHeader);
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                // Allow admin to specify channelPartnerId, or use logged-in partner's ID
                int? channelPartnerId = request.ChannelPartnerId ?? user.ChannelPartnerId;

                if (!channelPartnerId.HasValue)
                    return BadRequest(new { success = false, message = "Partner context not found. Please provide channelPartnerId." });

                var plan = await _db.SubscriptionPlans.FindAsync(request.PlanId);
                if (plan == null)
                    return NotFound(new { success = false, message = "Plan not found" });

                var amount = request.BillingCycle.ToLower() == "annual" ? plan.YearlyPrice : plan.MonthlyPrice;

                // Create Razorpay order
                var orderId = await _razorpayService.CreateOrderAsync(
                    amount, 
                    "INR", 
                    $"subscription_{channelPartnerId}_{request.PlanId}"
                );

                return Ok(new
                {
                    success = true,
                    orderId = orderId,
                    amount = amount * 100, // Razorpay expects paise
                    currency = "INR",
                    planName = plan.PlanName,
                    billingCycle = request.BillingCycle,
                    razorpayKeyId = _razorpayService.GetKeyId()
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Get Transactions
        /// GET /api/subscription/transactions
        /// </summary>
        [HttpGet("subscription/transactions")]
        public async Task<IActionResult> GetTransactions([FromQuery] string? type = null, [FromQuery] DateTime? fromDate = null, [FromQuery] DateTime? toDate = null)
        {
            try
            {
                var authHeader = Request.Headers["Authorization"].ToString();
                var user = ValidateToken(authHeader);
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var query = _db.PaymentTransactions.AsQueryable();

                // Filter by partner if not admin
                if (user.Role != "Admin" && user.ChannelPartnerId.HasValue)
                {
                    query = query.Where(t => t.ChannelPartnerId == user.ChannelPartnerId.Value);
                }

                // Apply filters
                if (!string.IsNullOrEmpty(type))
                {
                    query = query.Where(t => t.TransactionType == type);
                }

                if (fromDate.HasValue)
                {
                    query = query.Where(t => t.TransactionDate >= fromDate.Value);
                }

                if (toDate.HasValue)
                {
                    query = query.Where(t => t.TransactionDate <= toDate.Value);
                }

                var transactions = await query
                    .Include(t => t.ChannelPartner)
                    .OrderByDescending(t => t.TransactionDate)
                    .Select(t => new
                    {
                        t.TransactionId,
                        t.TransactionReference,
                        t.RazorpayOrderId,
                        t.RazorpayPaymentId,
                        t.Amount,
                        t.Currency,
                        t.TransactionType,
                        t.Status,
                        t.PaymentMethod,
                        t.TransactionDate,
                        t.CompletedDate,
                        t.Description,
                        PartnerName = t.ChannelPartner != null ? t.ChannelPartner.CompanyName : null
                    })
                    .ToListAsync();

                return Ok(new { success = true, data = transactions });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Process Refund
        /// POST /api/subscription/refund
        /// </summary>
        [HttpPost("subscription/refund")]
        public async Task<IActionResult> ProcessRefund([FromBody] RefundRequest request)
        {
            try
            {
                var authHeader = Request.Headers["Authorization"].ToString();
                var user = ValidateToken(authHeader);
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                if (user.Role != "Admin")
                    return Forbid();

                var subscription = await _db.PartnerSubscriptions
                    .Include(s => s.ChannelPartner)
                    .FirstOrDefaultAsync(s => s.SubscriptionId == request.SubscriptionId);

                if (subscription == null)
                    return NotFound(new { success = false, message = "Subscription not found" });

                // Find the payment transaction
                var transaction = await _db.PaymentTransactions
                    .Where(t => t.ChannelPartnerId == subscription.ChannelPartnerId
                        && t.Status == "Success"
                        && t.Amount == subscription.Amount
                        && t.TransactionDate >= subscription.StartDate.AddDays(-1)
                        && t.TransactionDate <= subscription.StartDate.AddDays(1))
                    .OrderByDescending(t => t.TransactionDate)
                    .FirstOrDefaultAsync();

                if (transaction == null || string.IsNullOrEmpty(transaction.TransactionReference))
                {
                    // Manual refund
                    subscription.CancellationReason = $"Cancelled by admin - Refund Processed Manually: ₹{subscription.Amount:N0}. {request.RefundNotes}";
                    subscription.UpdatedOn = IndianTime.Now;
                    _db.PartnerSubscriptions.Update(subscription);
                    await _db.SaveChangesAsync();

                    return Ok(new
                    {
                        success = true,
                        message = $"Manual refund of ₹{subscription.Amount:N0} marked as processed",
                        refundType = "Manual"
                    });
                }

                // Process Razorpay refund
                var (success, refundId, message) = await _razorpayService.CreateRefundAsync(
                    transaction.TransactionReference,
                    subscription.Amount,
                    request.RefundNotes
                );

                if (success)
                {
                    // Update subscription
                    subscription.CancellationReason = $"Cancelled by admin - Refund Processed: ₹{subscription.Amount:N0}. Refund ID: {refundId}. {request.RefundNotes}";
                    subscription.UpdatedOn = IndianTime.Now;
                    _db.PartnerSubscriptions.Update(subscription);

                    // Create refund transaction record
                    var refundTransaction = new PaymentTransactionModel
                    {
                        ChannelPartnerId = subscription.ChannelPartnerId,
                        TransactionReference = refundId,
                        Amount = subscription.Amount,
                        Currency = transaction.Currency ?? "INR",
                        TransactionType = "Refund",
                        Status = "Processed",
                        PaymentMethod = "Razorpay",
                        TransactionDate = IndianTime.Now,
                        CompletedDate = IndianTime.Now,
                        Description = $"Refund for Subscription #{request.SubscriptionId}. {request.RefundNotes}"
                    };
                    _db.PaymentTransactions.Add(refundTransaction);

                    await _db.SaveChangesAsync();

                    return Ok(new
                    {
                        success = true,
                        message = $"Refund of ₹{subscription.Amount:N0} processed successfully",
                        refundId = refundId,
                        refundType = "Razorpay",
                        details = message
                    });
                }
                else
                {
                    return BadRequest(new { success = false, message = $"Razorpay refund failed: {message}" });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // Helper methods
        private string GenerateJwtToken(UserModel user)
        {
            return JwtHelper.GenerateToken(user, _config, expiryHours: 8);
        }

        private UserModel? ValidateToken(string authHeader)
        {
            var tokenUser = JwtHelper.ValidateToken(authHeader, _config);
            if (tokenUser == null) return null;

            return new UserModel
            {
                UserId = tokenUser.UserId,
                Role = tokenUser.Role,
                ChannelPartnerId = tokenUser.ChannelPartnerId
            };
        }
    }

    // Request models
    public class LoginRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class CreateOrderRequest
    {
        public int PlanId { get; set; }
        public string BillingCycle { get; set; } = "monthly";
        public int? ChannelPartnerId { get; set; } // Optional: For admin to create order for specific partner
    }

    public class RefundRequest
    {
        public int SubscriptionId { get; set; }
        public string RefundNotes { get; set; } = "";
    }
}
