using CRM.Helpers;
using CRM.Models;
using CRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Controllers
{
    public class PublicLeadsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ILogger<PublicLeadsController> _logger;
        private readonly IWhatsAppService _whatsAppService;
        private readonly SubscriptionService _subscriptionService;

        public PublicLeadsController(AppDbContext db, ILogger<PublicLeadsController> logger, IWhatsAppService whatsAppService, SubscriptionService subscriptionService)
        {
            _db = db;
            _logger = logger;
            _whatsAppService = whatsAppService;
            _subscriptionService = subscriptionService;
        }

        // POST: Public endpoint to capture Express Interest
        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> CaptureInterest([FromBody] WebhookLeadModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return Json(new { success = false, message = "Please fill all required fields" });
                }

                // Save to Leads table
                var lead = new LeadModel
                {
                    Name = model.Name,
                    Contact = model.Contact,
                    PreferredLocation = model.PreferredLocation,
                    BHK = model.BHK,
                    Requirement = $"Budget: ₹{model.Budget}",
                    Source = "Website - Express Interest",
                    PropertyType = model.ProjectName,
                    Stage = "New",
                    Status = "Pending",
                    ExecutiveId = null, // Will be assigned later by admin from WebhookLeads/Index
                    CreatedOn = IndianTime.Now,
                    ChannelPartnerId = null, // Public leads go to admin
                    HandoverStatus = "Admin"
                };

                _db.Leads.Add(lead);
                await _db.SaveChangesAsync();

                // Send WhatsApp message to customer
                try
                {
                    string customerMessage = $"Thank you for showing interest in {model.ProjectName}!\n\n" +
                                           $"Our team will reach out to you shortly to discuss your requirements.\n\n" +
                                           $"Your Details:\n" +
                                           $"📍 Location: {model.PreferredLocation}\n" +
                                           $"🏠 BHK: {model.BHK}\n" +
                                           $"💰 Budget: ₹{model.Budget}\n\n" +
                                           $"Thank you for choosing us!";

                    await _whatsAppService.SendMessageAsync(model.Contact, customerMessage);
                }
                catch (Exception whatsappEx)
                {
                    _logger.LogWarning(whatsappEx, "Failed to send WhatsApp message to customer");
                }

                // Create notification for admin
                try
                {
                    var notification = new NotificationModel
                    {
                        Title = "New Lead - Express Interest",
                        Message = $"{model.Name} expressed interest in {model.ProjectName}. Phone: {model.Contact}, Location: {model.PreferredLocation}, BHK: {model.BHK}, Budget: ₹{model.Budget}",
                        Type = "NewLead",
                        Link = $"/Leads/Details/{lead.LeadId}",
                        CreatedOn = IndianTime.Now,
                        IsRead = false,
                        UserId = null // Visible to all admins
                    };
                    _db.Notifications.Add(notification);
                    await _db.SaveChangesAsync();
                }
                catch (Exception notifEx)
                {
                    _logger.LogWarning(notifEx, "Failed to create notification");
                }

                _logger.LogInformation($"New interest captured: {model.Name} - {model.ProjectName}");

                return Json(new
                {
                    success = true,
                    message = "Thank you for your interest! Our team will contact you soon."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error capturing interest");
                return Json(new { success = false, message = "Error submitting form. Please try again." });
            }
        }

        // POST: Public endpoint to capture Site Visit Request
        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> CaptureSiteVisit([FromBody] WebhookLeadModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return Json(new { success = false, message = "Please fill all required fields" });
                }

                // Save to Leads table
                var lead = new LeadModel
                {
                    Name = model.Name,
                    Contact = model.Contact,
                    PreferredLocation = model.PreferredLocation,
                    BHK = model.BHK,
                    Requirement = $"Budget: ₹{model.Budget}",
                    Source = "Website - Site Visit",
                    PropertyType = model.ProjectName,
                    Stage = "New",
                    Status = "Pending",
                    ExecutiveId = null, // Will be assigned later by admin from WebhookLeads/Index
                    CreatedOn = IndianTime.Now,
                    ChannelPartnerId = null, // Public leads go to admin
                    HandoverStatus = "Admin"
                };

                _db.Leads.Add(lead);
                await _db.SaveChangesAsync();

                // Send WhatsApp message to customer
                try
                {
                    string customerMessage = $"Thank you for scheduling a site visit at {model.ProjectName}!\n\n" +
                                           $"Our team will reach out to you shortly to confirm the visit date and time.\n\n" +
                                           $"Your Details:\n" +
                                           $"📍 Location: {model.PreferredLocation}\n" +
                                           $"🏠 BHK: {model.BHK}\n" +
                                           $"💰 Budget: ₹{model.Budget}\n\n" +
                                           $"We look forward to showing you around!";

                    await _whatsAppService.SendMessageAsync(model.Contact, customerMessage);
                }
                catch (Exception whatsappEx)
                {
                    _logger.LogWarning(whatsappEx, "Failed to send WhatsApp message to customer");
                }

                // Create notification for admin
                try
                {
                    var notification = new NotificationModel
                    {
                        Title = "New Site Visit Request",
                        Message = $"{model.Name} requested site visit for {model.ProjectName}. Phone: {model.Contact}, Location: {model.PreferredLocation}, BHK: {model.BHK}, Budget: ₹{model.Budget}",
                        Type = "SiteVisit",
                        Link = $"/Leads/Details/{lead.LeadId}",
                        CreatedOn = IndianTime.Now,
                        IsRead = false,
                        UserId = null // Visible to all admins
                    };
                    _db.Notifications.Add(notification);
                    await _db.SaveChangesAsync();
                }
                catch (Exception notifEx)
                {
                    _logger.LogWarning(notifEx, "Failed to create notification");
                }

                _logger.LogInformation($"New site visit request: {model.Name} - {model.ProjectName}");

                return Json(new
                {
                    success = true,
                    message = "Site visit scheduled! We'll confirm the date and time soon."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error capturing site visit");
                return Json(new { success = false, message = "Error submitting form. Please try again." });
            }
        }
    }
}
