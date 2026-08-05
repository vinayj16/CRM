using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CRM.Services;
using CRM.Models;
using System.Security.Claims;

namespace CRM.Controllers
{
    [Authorize]
    public class WhatsAppController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IWhatsAppService _whatsAppService;
        private readonly SubscriptionService _subscriptionService;
        private readonly ILogger<WhatsAppController> _logger;

        public WhatsAppController(AppDbContext db, IWhatsAppService whatsAppService, SubscriptionService subscriptionService, ILogger<WhatsAppController> logger)
        {
            _db = db;
            _whatsAppService = whatsAppService;
            _subscriptionService = subscriptionService;
            _logger = logger;
        }

        private async Task<(int? channelPartnerId, bool hasAccess)> CheckWhatsAppAccessAsync()
        {
            var userIdClaim = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return (null, true); // Admin access by default

            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user?.ChannelPartnerId == null)
                return (null, true); // Admin access

            var hasAccess = await _subscriptionService.HasFeatureAccessAsync(user.ChannelPartnerId.Value, "whatsapp");
            return (user.ChannelPartnerId, hasAccess);
        }

        [HttpPost]
        public async Task<IActionResult> SendMessage(int leadId, string message, string messageType = "custom")
        {
            try
            {
                var (channelPartnerId, hasAccess) = await CheckWhatsAppAccessAsync();
                if (!hasAccess)
                {
                    return Json(new { 
                        success = false, 
                        message = "WhatsApp integration not available in your current plan.",
                        showUpgrade = true,
                        upgradeUrl = "/Subscription/MyPlan"
                    });
                }

                var lead = await _db.Leads.FindAsync(leadId);
                if (lead == null)
                {
                    return Json(new { success = false, message = "Lead not found" });
                }

                if (string.IsNullOrEmpty(lead.Contact))
                {
                    return Json(new { success = false, message = "Lead has no phone number" });
                }

                bool success = false;

                // Send based on message type
                switch (messageType.ToLower())
                {
                    case "lead_created":
                        success = await _whatsAppService.SendLeadCreatedMessageAsync(lead);
                        break;
                    case "lead_assigned":
                        var assignedUser = await _db.Users.FindAsync(lead.ExecutiveId);
                        var assignedName = assignedUser?.Username ?? "Our Team";
                        success = await _whatsAppService.SendLeadAssignedMessageAsync(lead, assignedName);
                        break;
                    case "custom":
                    default:
                        success = await _whatsAppService.SendMessageAsync(lead.Contact, message);
                        break;
                }

                if (success)
                {
                    return Json(new { success = true, message = "WhatsApp message sent successfully!" });
                }
                else
                {
                    return Json(new { success = false, message = "Failed to send WhatsApp message" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending WhatsApp message");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SendQuotationMessage(int quotationId)
        {
            try
            {
                var (channelPartnerId, hasAccess) = await CheckWhatsAppAccessAsync();
                if (!hasAccess)
                {
                    return Json(new { 
                        success = false, 
                        message = "WhatsApp integration not available in your current plan.",
                        showUpgrade = true,
                        upgradeUrl = "/Subscription/MyPlan"
                    });
                }

                var quotation = await _db.Quotations.FindAsync(quotationId);
                if (quotation == null)
                    return Json(new { success = false, message = "Quotation not found" });

                var lead = await _db.Leads.FindAsync(quotation.LeadId);
                if (lead == null)
                    return Json(new { success = false, message = "Lead not found" });

                var success = await _whatsAppService.SendQuotationSentMessageAsync(lead, quotationId);
                
                return Json(new { 
                    success = success, 
                    message = success ? "WhatsApp sent!" : "Failed to send WhatsApp" 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending quotation WhatsApp");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SendInvoiceMessage(int invoiceId)
        {
            try
            {
                var (channelPartnerId, hasAccess) = await CheckWhatsAppAccessAsync();
                if (!hasAccess)
                {
                    return Json(new { 
                        success = false, 
                        message = "WhatsApp integration not available in your current plan.",
                        showUpgrade = true,
                        upgradeUrl = "/Subscription/MyPlan"
                    });
                }

                var invoice = await _db.Invoices.FindAsync(invoiceId);
                if (invoice == null)
                    return Json(new { success = false, message = "Invoice not found" });

                var booking = await _db.Bookings.FindAsync(invoice.BookingId);
                if (booking == null)
                    return Json(new { success = false, message = "Booking not found" });

                var lead = await _db.Leads.FindAsync(booking.LeadId);
                if (lead == null)
                    return Json(new { success = false, message = "Lead not found" });

                var success = await _whatsAppService.SendInvoiceSentMessageAsync(lead, invoiceId);
                
                return Json(new { 
                    success = success, 
                    message = success ? "WhatsApp sent!" : "Failed to send WhatsApp" 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending invoice WhatsApp");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SendSiteVisitMessage(int leadId, DateTime visitDate)
        {
            try
            {
                var (channelPartnerId, hasAccess) = await CheckWhatsAppAccessAsync();
                if (!hasAccess)
                {
                    return Json(new { 
                        success = false, 
                        message = "WhatsApp integration not available in your current plan.",
                        showUpgrade = true,
                        upgradeUrl = "/Subscription/MyPlan"
                    });
                }

                var lead = await _db.Leads.FindAsync(leadId);
                if (lead == null)
                    return Json(new { success = false, message = "Lead not found" });

                var success = await _whatsAppService.SendSiteVisitScheduledMessageAsync(lead, visitDate);
                
                return Json(new { 
                    success = success, 
                    message = success ? "WhatsApp sent!" : "Failed to send WhatsApp" 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending site visit WhatsApp");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SendBookingMessage(int leadId, string propertyName)
        {
            try
            {
                var (channelPartnerId, hasAccess) = await CheckWhatsAppAccessAsync();
                if (!hasAccess)
                {
                    return Json(new { 
                        success = false, 
                        message = "WhatsApp integration not available in your current plan.",
                        showUpgrade = true,
                        upgradeUrl = "/Subscription/MyPlan"
                    });
                }

                var lead = await _db.Leads.FindAsync(leadId);
                if (lead == null)
                    return Json(new { success = false, message = "Lead not found" });

                var success = await _whatsAppService.SendBookingConfirmedMessageAsync(lead, propertyName);
                
                return Json(new { 
                    success = success, 
                    message = success ? "WhatsApp sent!" : "Failed to send WhatsApp" 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending booking WhatsApp");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetWhatsAppHistory(int leadId)
        {
            try
            {
                var logs = await _db.WhatsAppLogs
                    .Where(w => w.LeadId == leadId)
                    .OrderByDescending(w => w.SentOn)
                    .Take(20)
                    .Select(w => new
                    {
                        w.LogId,
                        w.Message,
                        w.Status,
                        SentOn = w.SentOn.ToString("dd MMM yyyy hh:mm tt"),
                        w.MessageType
                    })
                    .ToListAsync();

                return Json(logs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching WhatsApp history");
                return Json(new List<object>());
            }
        }
    }
}
