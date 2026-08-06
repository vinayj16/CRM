using CRM.Helpers;
using CRM.MasterDb;
using CRM.Models;
using CRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Security.Claims;

public class ContactFormModel
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

namespace CRM.Controllers
{
    public class HomeController : Controller
    {
        [Authorize]
        public IActionResult TeamDashboard()
        {
            var role = User?.FindFirst(ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid ?? "0", out int userId);
            var username = User?.FindFirst(ClaimTypes.Name)?.Value ?? User?.FindFirst("name")?.Value ?? "User";
            ViewBag.Username = username;

            var currentUser = _context.Users.FirstOrDefault(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;
            ViewBag.CompanyName = BrandingResolver.ResolveCompanyName(_context, channelPartnerId);
            ViewBag.CompanyLogo = BrandingResolver.ResolveCompanyLogo(_context, channelPartnerId);

            return View();
        }

        [Authorize]
        public IActionResult SalesOverview()
        {
            return View();
        }
        private readonly ILogger<HomeController> _logger;
        private readonly AppDbContext _context;
        private readonly MasterDbContext _masterDb;
        private readonly INotificationService _notificationService;
        private readonly IWebHostEnvironment _env;
        private readonly Services.EmailService _emailService;
        private readonly Services.ITenantService _tenantService;

        public HomeController(ILogger<HomeController> logger, AppDbContext context, MasterDbContext masterDb, INotificationService notificationService, IWebHostEnvironment env, Services.EmailService emailService, Services.ITenantService tenantService)
        {
            _logger = logger;
            _context = context;
            _masterDb = masterDb;
            _notificationService = notificationService;
            _env = env;
            _emailService = emailService;
            _tenantService = tenantService;
        }

        // Shared referral wallet endpoint - accessible to all authenticated users
        // (the layout dropdown calls this for every role; SuperAdminController is SA-only).
        // tenantId is derived from the caller's claims so users can only ever read
        // their own tenant's referral data (never another tenant's).
        [Authorize]
        public async Task<IActionResult> GetReferralWallet(int tenantId = 0)
        {
            var claimTenant = User?.FindFirst("TenantId")?.Value;
            if (!int.TryParse(claimTenant, out int callerTenantId) || callerTenantId <= 0)
            {
                return Json(new { success = false, message = "Tenant not determined" });
            }
            // Never trust client-supplied tenantId - always scope to the caller's own tenant
            tenantId = callerTenantId;
            var earnings = await _masterDb.ReferralEarnings
                .Where(r => r.TenantId == tenantId && !r.IsUsed)
                .ToListAsync();

            var balance = earnings.Sum(e => e.Amount);

            var referralEarnings = await _masterDb.ReferralEarnings
                .Where(r => r.TenantId == tenantId && r.Type == "Referrer")
                .OrderByDescending(r => r.CreatedOn)
                .ToListAsync();

            var referredTenantIds = referralEarnings.Where(r => r.ReferredTenantId.HasValue).Select(r => r.ReferredTenantId!.Value).Distinct().ToList();
            var referredTenants = await _masterDb.Tenants.Where(t => referredTenantIds.Contains(t.TenantId)).ToListAsync();

            var referrals = referralEarnings.Select(r => new
            {
                r.Id,
                r.Amount,
                r.Description,
                r.IsUsed,
                JoinedCompany = r.ReferredTenantId.HasValue
                    ? (referredTenants.FirstOrDefault(t => t.TenantId == r.ReferredTenantId.Value)?.CompanyName ?? "")
                    : "",
                JoinedOn = r.CreatedOn.ToString("MMM dd, yyyy")
            }).ToList();

            var tenant = await _masterDb.Tenants.FindAsync(tenantId);

            return Json(new
            {
                success = true,
                balance,
                referralCode = tenant?.Referral ?? "",
                referrals
            });
        }

        [Authorize]
        [Route("home")]
        [Route("Home/Index")]
        public IActionResult Index()
        {
            var role = User?.FindFirst(ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid ?? "0", out int userId);
            var username = User?.FindFirst(ClaimTypes.Name)?.Value ?? User?.FindFirst("name")?.Value ?? "User";
            ViewBag.Username = username;

            var currentUser = _context.Users.FirstOrDefault(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;
            ViewBag.CompanyName = BrandingResolver.ResolveCompanyName(_context, channelPartnerId);
            ViewBag.CompanyLogo = BrandingResolver.ResolveCompanyLogo(_context, channelPartnerId);

            if (role?.ToLower() == "admin" || role?.ToLower() == "managerrr")
            {
                return View("AdminDashboard");
            }
            else if (role?.ToLower() == "partner")
            {
                return View("PartnerDashboard");
            }
            else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
            {
                return View("SalesDashboard");
            }

            return RedirectToAction("Landing");
        }


        [AllowAnonymous]
        public IActionResult Landing()
        {
            bool HasCustomBranding = false;

            // ?? Use TenantService instead of claims (works for subdomain + impersonation)
            int tid = _tenantService.GetTenantId();

            if (tid > 0)
            {
                var activeSub = _masterDb.TenantSubscriptions
                    .Where(s => s.TenantId == tid && (s.Status == "Active" || s.Status == "Trial"))
                    .OrderByDescending(s => s.StartDate)
                    .FirstOrDefault();

                if (activeSub != null)
                {
                    var activePlan = _masterDb.SaasPlans.FirstOrDefault(p => p.PlanId == activeSub.PlanId);
                    HasCustomBranding = activePlan?.HasCustomBranding ?? false;
                }
            }

            // ? 1. If no subdomain ? SaaS Landing
            if (!_tenantService.IsResolved())
            {
                return View("SaasLanding");
            }

            // ? 2. If logged in (including impersonation) ? Profile
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Profile");
            }

            // ? 3. Subdomain + NOT logged in
            if (HasCustomBranding)
            {
                var settings = _context.Settings
                    .Where(s => s.ChannelPartnerId == null)
                    .GroupBy(s => s.SettingKey)
                    .ToDictionary(g => g.Key, g => g.First().SettingValue ?? "");

                // Branding
                var branding = _context.Branding.FirstOrDefault() ?? new BrandingModel();
                ViewBag.Branding = branding;

                var properties = _context.Properties
                    .Where(p => p.IsActive == true)
                    .Take(6)
                    .ToList();

                var propertyData = properties.Select(p =>
                {
                    var flats = _context.PropertyFlats.Where(f => f.PropertyId == p.PropertyId).ToList();
                    var availableFlats = flats.Count(f => f.Status == "Available");
                    var prices = flats.Where(f => f.Price.HasValue).Select(f => f.Price.Value).ToList();
                    var uploads = _context.PropertyUploads.Where(u => u.PropertyId == p.PropertyId).ToList();
                    var imageIds = uploads.Select(u => u.UploadId).ToList();

#pragma warning disable CS8629
                    return new
                    {
                        p.PropertyId,
                        p.PropertyName,
                        p.Location,
                        p.Price,
                        p.AreaSqft,
                        p.PropertyImage,
                        p.CreatedOn,
                        p.Developer,
                        FlatsCount = flats.Count,
                        AvailableFlats = availableFlats,
                        MinPrice = prices.Any() ? prices.Min() : p.Price ?? 100000m,
                        MaxPrice = prices.Any() ? prices.Max() : p.Price ?? 500000m,
                        Images = imageIds
                    };
#pragma warning restore CS8629
                }).ToList();

                ViewBag.Properties = propertyData;
                ViewBag.LeadsCount = _context.Leads.AsQueryable().Count();
                ViewBag.ProjectsCount = _context.Properties.Where(p => p.IsActive).Count();
                ViewBag.Testimonials = _context.Testimonials
                    .Where(t => t.IsActive)
                    .OrderByDescending(t => t.CreatedOn)
                    .ToList();

                return View("~/Views/Home/Landing.cshtml", settings);
            }
            else
            {
                // ? No branding ? go to Login (NOT Profile)
                return RedirectToAction("Login", "Account");
            }
        }

        [AllowAnonymous]
        public IActionResult ProjectDetails(int id)
        {
            var property = _context.Properties.FirstOrDefault(p => p.PropertyId == id && p.IsActive);
            if (property == null)
            {
                return NotFound();
            }

            var flats = _context.PropertyFlats.Where(f => f.PropertyId == id).ToList();
            var uploads = _context.PropertyUploads.Where(u => u.PropertyId == id).ToList();
            var settings = _context.Settings.Where(s => s.ChannelPartnerId == null).AsEnumerable().GroupBy(s => s.SettingKey).ToDictionary(g => g.Key, g => g.First().SettingValue ?? "");

            // Get branding data
            var branding = _context.Branding.FirstOrDefault();
            ViewBag.CompanyLogo = branding?.CompanyLogo;
            ViewBag.LogoDisplayStyle = branding?.LogoDisplayStyle ?? "LogoOnly";
            ViewBag.CompanyName = settings.ContainsKey("CompanyName") && !string.IsNullOrEmpty(settings["CompanyName"]) ? settings["CompanyName"] : "PropTech CRM";

            var projectData = new
            {
                property.PropertyId,
                property.PropertyName,
                property.Location,
                property.Price,
                property.AreaSqft,
                property.Developer,
                property.CreatedOn,
                FlatsCount = flats.Count,
                AvailableFlats = flats.Where(f => f.Status == "Available").Count(),
                Images = uploads.Select(u => u.UploadId).ToList(),
                Flats = flats
            };

            ViewBag.Settings = settings;
            return View(projectData);
        }

        [AllowAnonymous]
        public IActionResult GetPropertyImage(int id)
        {
            var upload = _context.PropertyUploads.FirstOrDefault(u => u.UploadId == id);
            if (upload?.FileBytes != null)
            {
                return File(upload.FileBytes, upload.ContentType ?? "image/jpeg");
            }
            return NotFound();
        }

        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> SubmitInterest([FromForm] ProjectInterest model)
        {
            try
            {
                if (ModelState.IsValid)
                {
                    _context.ProjectInterests.Add(model);
                    await _context.SaveChangesAsync();
                    return Json(new { success = true });
                }
                return Json(new { success = false, message = "Invalid data" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting project interest");
                return Json(new { success = false, message = "Server error" });
            }
        }

        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> SendContactEmail([FromBody] ContactFormModel model)
        {
            try
            {
                if (string.IsNullOrEmpty(model.Name) || string.IsNullOrEmpty(model.Email) || string.IsNullOrEmpty(model.Message))
                {
                    return Json(new { success = false, message = "Please fill all required fields" });
                }

                var companyEmail = "maheswarim257@gmail.com";
                var companyName = _context.Settings.FirstOrDefault(s => s.SettingKey == "CompanyName")?.SettingValue ?? "CRM";

                var adminUser = _context.Users.FirstOrDefault(u => u.Role == "Admin");
                var (fromEmail, password) = adminUser != null ? await _emailService.GetEmailCredentials(adminUser.UserId) : (null, null);

                if (string.IsNullOrEmpty(fromEmail) || string.IsNullOrEmpty(password))
                {
                    return Json(new { success = false, message = "Email settings not configured" });
                }

                // Create email body
                var emailBody = $@"
                    <h3>New Contact Form Submission</h3>
                    <p><strong>Name:</strong> {model.Name}</p>
                    <p><strong>Email:</strong> {model.Email}</p>
                    <p><strong>Subject:</strong> {model.Subject}</p>
                    <p><strong>Message:</strong></p>
                    <p>{model.Message.Replace("\n", "<br/>")}</p>
                    <hr/>
                    <p><small>This message was sent from the contact form on {companyName} website.</small></p>
                ";

                // Send email using SMTP
                using (var client = new System.Net.Mail.SmtpClient("smtp.gmail.com", 587))
                {
                    client.EnableSsl = true;
                    client.Credentials = new System.Net.NetworkCredential(fromEmail, password);

                    var mailMessage = new System.Net.Mail.MailMessage
                    {
                        From = new System.Net.Mail.MailAddress(fromEmail, $"{companyName} Contact Form"),
                        Subject = $"Contact Form: {model.Subject}",
                        Body = emailBody,
                        IsBodyHtml = true
                    };

                    mailMessage.To.Add(companyEmail);
                    mailMessage.ReplyToList.Add(new System.Net.Mail.MailAddress(model.Email, model.Name));

                    await client.SendMailAsync(mailMessage);
                }

                _logger.LogInformation($"Contact form email sent from {model.Name} ({model.Email}): {model.Subject}");
                return Json(new { success = true, message = "Thank you for your message. We will get back to you soon!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending contact email: {Error}", ex.Message);
                return Json(new { success = false, message = "Error sending message. Please try again." });
            }
        }

        [AllowAnonymous]
        public IActionResult Privacy()
        {
            var branding = _context.Branding.FirstOrDefault() ?? new BrandingModel();
            var settings = _context.Settings.Where(s => s.ChannelPartnerId == null).AsEnumerable().GroupBy(s => s.SettingKey).ToDictionary(g => g.Key, g => g.First().SettingValue ?? "");
            ViewBag.Branding = branding;
            ViewBag.Settings = settings;
            return View();
        }

        // =============================================
        // Inquiry Form Submission (Landing Page "Get Started")
        // Stores in Master DB Inquiries table
        // =============================================
        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> SubmitInquiry([FromBody] InquiryFormModel model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.CompanyName) ||
                    string.IsNullOrWhiteSpace(model.Email))
                {
                    return Json(new { success = false, message = "Please fill all required fields" });
                }

                var inquiry = new MasterDb.Models.InquiryModel
                {
                    CompanyName = model.CompanyName,
                    ContactPerson = model.ContactPerson,
                    Email = model.Email,
                    Phone = model.Phone,
                    Message = model.Message,
                    SelectedPlan = string.IsNullOrWhiteSpace(model.SelectedPlan) ? null : model.SelectedPlan,
                    SelectedPlanId = model.SelectedPlanId,
                    SelectedPlanName = string.IsNullOrWhiteSpace(model.SelectedPlanName) ? null : model.SelectedPlanName,
                    Status = "New",
                    ReferralCode = string.IsNullOrWhiteSpace(model.ReferralCode) ? "" : model.ReferralCode,
                    CreatedOn = DateTime.UtcNow
                };

                _masterDb.Inquiries.Add(inquiry);
                await _masterDb.SaveChangesAsync();

                _logger.LogInformation($"New inquiry from {model.CompanyName} ({model.Email}) - Plan: {model.SelectedPlanName}");

                // Notify the Super Admin(s) about the new inquiry via email
                try
                {
                    var superAdmins = await _masterDb.SuperAdmins
                        .Where(s => s.IsActive)
                        .ToListAsync();

                    var planInfo = string.IsNullOrWhiteSpace(model.SelectedPlanName) ? "Not specified" : model.SelectedPlanName;

                    var emailBody = $@"
                        <div style='font-family:Segoe UI, Arial, sans-serif; max-width:600px; margin:auto;'>
                            <h2 style='color:#1a6fa8;'>New Inquiry Received</h2>
                            <p>A new <strong>Get Started</strong> inquiry has been submitted:</p>
                            <table style='border-collapse:collapse; width:100%;'>
                                <tr><td style='padding:6px 10px; font-weight:700; color:#155c8c;'>Company</td><td style='padding:6px 10px;'>{model.CompanyName}</td></tr>
                                <tr><td style='padding:6px 10px; font-weight:700; color:#155c8c;'>Contact Person</td><td style='padding:6px 10px;'>{model.ContactPerson}</td></tr>
                                <tr><td style='padding:6px 10px; font-weight:700; color:#155c8c;'>Email</td><td style='padding:6px 10px;'>{model.Email}</td></tr>
                                <tr><td style='padding:6px 10px; font-weight:700; color:#155c8c;'>Phone</td><td style='padding:6px 10px;'>{model.Phone}</td></tr>
                                <tr><td style='padding:6px 10px; font-weight:700; color:#155c8c;'>Selected Plan</td><td style='padding:6px 10px;'>{planInfo}</td></tr>
                                <tr><td style='padding:6px 10px; font-weight:700; color:#155c8c;'>Referral Code</td><td style='padding:6px 10px;'>{model.ReferralCode}</td></tr>
                                <tr><td style='padding:6px 10px; font-weight:700; color:#155c8c; vertical-align:top;'>Message</td><td style='padding:6px 10px;'>{model.Message}</td></tr>
                            </table>
                            <p style='margin-top:16px;'>
                                <a href='{Request.Scheme}://{Request.Host}/SuperAdmin/Inquiries'
                                   style='background:#1a6fa8; color:#fff; padding:10px 18px; border-radius:6px; text-decoration:none;'>
                                    View in Super Admin Dashboard
                                </a>
                            </p>
                        </div>";

                    foreach (var sa in superAdmins)
                    {
                        if (!string.IsNullOrWhiteSpace(sa.Email))
                        {
                            await _emailService.SendEmailAsync(
                                0,
                                sa.Email,
                                $"New Get Started Inquiry - {model.CompanyName}",
                                emailBody,
                                templateName: null,
                                category: "Inquiry");
                        }
                    }
                }
                catch (Exception emailEx)
                {
                    // Do not fail the inquiry submission if email notification fails
                    _logger.LogWarning(emailEx, "Failed to send inquiry notification email to Super Admin");
                }

                return Json(new
                {
                    success = true,
                    message = "Thank you! Our team will contact you shortly."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting inquiry");

                return Json(new
                {
                    success = false,
                    message = "Something went wrong. Please try again."
                });
            }
        }

        [AllowAnonymous]
        public IActionResult RefundPolicy()
        {
            var branding = _context.Branding.FirstOrDefault() ?? new BrandingModel();
            var settings = _context.Settings.Where(s => s.ChannelPartnerId == null).AsEnumerable().GroupBy(s => s.SettingKey).ToDictionary(g => g.Key, g => g.First().SettingValue ?? "");
            ViewBag.Branding = branding;
            ViewBag.Settings = settings;
            return View();
        }

        [Authorize]
        public IActionResult Support()
        {
            var branding = _context.Branding.FirstOrDefault() ?? new BrandingModel();
            var settings = _context.Settings.Where(s => s.ChannelPartnerId == null).AsEnumerable().GroupBy(s => s.SettingKey).ToDictionary(g => g.Key, g => g.First().SettingValue ?? "");
            ViewBag.Branding = branding;
            ViewBag.Settings = settings;
            return View();
        }

        [AllowAnonymous]
        public IActionResult Maintenance(string? message)
        {
            ViewBag.MaintenanceMessage = message ?? "System is currently under scheduled maintenance. We will be back shortly.";
            return View();
        }

        [AllowAnonymous]
        public IActionResult ContactAdmin(string? msg)
        {
            ViewBag.Message = msg ?? "Your organization's subscription has expired or is inactive. Please contact your administrator to renew the subscription.";
            return View();
        }

        [Authorize]
        public IActionResult HelpCenter()
        {
            var branding = _context.Branding.FirstOrDefault() ?? new BrandingModel();
            var settings = _context.Settings.Where(s => s.ChannelPartnerId == null).AsEnumerable().GroupBy(s => s.SettingKey).ToDictionary(g => g.Key, g => g.First().SettingValue ?? "");
            ViewBag.Branding = branding;
            ViewBag.Settings = settings;
            return View();
        }

        [AllowAnonymous]
        public IActionResult Terms()
        {
            var branding = _context.Branding.FirstOrDefault() ?? new BrandingModel();
            var settings = _context.Settings.Where(s => s.ChannelPartnerId == null).AsEnumerable().GroupBy(s => s.SettingKey).ToDictionary(g => g.Key, g => g.First().SettingValue ?? "");
            ViewBag.Branding = branding;
            ViewBag.Settings = settings;
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        [AllowAnonymous]
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                StatusCode = 500,
                Title = "Oops! Something went wrong.",
                UserMessage = "We will fix this as soon as possible."
            });
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        [AllowAnonymous]
        [Route("home/statuscode/{code:int}")]
        public IActionResult StatusCodePage(int code)
        {
            var model = new ErrorViewModel
            {
                RequestId = HttpContext.TraceIdentifier,
                StatusCode = code,
                Title = code == 404 ? "Page not found" : "Oops! Something went wrong.",
                UserMessage = code == 404
                    ? "The page you are looking for does not exist or has been moved."
                    : "We will fix this as soon as possible."
            };

            Response.StatusCode = code;
            return View("Error", model);
        }

        public IActionResult AccessDenied()
        {
            return View();
        }


        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetUnreadNotificationCount()
        {
            try
            {
                var userIdClaim = User.FindFirst("UserId")?.Value;
                var userRoleClaim = User.FindFirst(ClaimTypes.Role)?.Value;

                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                {
                    return Json(new { count = 0 });
                }

                var userRole = userRoleClaim ?? "Agent";
                var count = await _notificationService.GetUnreadCountAsync(userId, userRole);

                return Json(new { count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting unread notification count");
                return Json(new { count = 0 });
            }
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetAdminNotifications()
        {
            try
            {
                // Get current user ID from claims (using "UserId" custom claim, not ClaimTypes.NameIdentifier)
                var userIdClaim = User.FindFirst("UserId")?.Value;
                var userRoleClaim = User.FindFirst(ClaimTypes.Role)?.Value;

                _logger.LogInformation($"Claims - UserId: {userIdClaim}, Role: {userRoleClaim}");

                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                {
                    _logger.LogWarning("GetAdminNotifications called without valid user ID");
                    return Json(new List<object>());
                }

                // Get user role
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? "Agent";

                _logger.LogInformation($"Loading notifications for UserId={userId}, Role={userRole}");

                // Get notifications using the service
                var notifications = await _notificationService.GetUserNotificationsAsync(userId, userRole);

                _logger.LogInformation($"Found {notifications.Count} notifications for UserId={userId}");

                var result = notifications.Select(n => new
                {
                    n.NotificationId,
                    n.Title,
                    n.Message,
                    n.Type,
                    n.Priority,
                    n.IsRead,
                    CreatedOn = n.CreatedOn.ToString("MMM dd, yyyy hh:mm tt"),
                    n.Link,
                    n.RelatedEntityId,
                    n.RelatedEntityType
                }).ToList();

                return Json(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading notifications");
                return Json(new List<object>());
            }
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> MarkNotificationRead(int notificationId)
        {
            try
            {
                await _notificationService.MarkAsReadAsync(notificationId);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking notification as read");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> MarkAllNotificationsRead()
        {
            try
            {
                var userIdClaim = User.FindFirst("UserId")?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                {
                    return Json(new { success = false, message = "User not found" });
                }

                await _notificationService.MarkAllAsReadAsync(userId);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking all notifications as read");
                return Json(new { success = false, message = ex.Message });
            }
        }


        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetDashboardData([FromQuery] int months = 6)
        {
            var role = User?.FindFirst(ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid ?? "0", out int userId);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;

            var query = _context.Leads.AsQueryable();
            if (role?.ToLower() == "partner")
            {
                query = query.Where(l => l.ChannelPartnerId == channelPartnerId);
            }
            // Admin sees ALL leads - no filter
            else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
            {
                query = query.Where(l => l.ExecutiveId == userId);
            }

            var totalLeads = await query.CountAsync();
            var facebookLeads = await query.CountAsync(l => l.Source == "Facebook Webhook" || l.Source == "Facebook API");

            var leads = await query.ToListAsync();

            var monthlyLeads = Enumerable.Range(0, months).Select(i =>
            {
                var date = IndianTime.Now.AddMonths(-i);
                var count = leads.Count(l => l.CreatedOn.Month == date.Month && l.CreatedOn.Year == date.Year);
                return new { month = date.ToString("MMM yy"), count };
            }).Reverse().ToList();

            var sources = leads.GroupBy(l => l.Source ?? "Unknown")
                .Select(g => new { source = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToList();

            var stages = new[] { "New", "Office Meeting", "Site Visit Requested", "Site Visit Done", "Quotation", "Quotation Sent", "Negotiation", "Booked" };
            var pipeline = stages.Select(stage => new { stage, count = leads.Count(l => l.Stage == stage) }).ToList();

            var newLeads = leads.OrderByDescending(l => l.CreatedOn).Take(5)
                .Select(l => new { l.LeadId, l.Name, l.Contact, l.Stage, CreatedOn = l.CreatedOn.ToString("MMM dd, yyyy") }).ToList();

            var bookingsQuery = _context.Bookings.AsQueryable();
            var paymentsQuery = _context.Payments.AsQueryable();
            var expensesQuery = _context.Expenses.AsQueryable();
            var revenuesQuery = _context.Revenues.AsQueryable();

            if (role?.ToLower() == "partner")
            {
                // Partner sees only their data and their agents' data
                var partnerLeadIds = await _context.Leads.Where(l => l.ChannelPartnerId == channelPartnerId).Select(l => l.LeadId).ToListAsync();
                bookingsQuery = bookingsQuery.Where(b => partnerLeadIds.Contains(b.LeadId));
                var partnerBookingIds = await bookingsQuery.Select(b => b.BookingId).ToListAsync();
                paymentsQuery = paymentsQuery.Where(p => partnerBookingIds.Contains(p.BookingId));
                expensesQuery = expensesQuery.Where(e => e.ChannelPartnerId == channelPartnerId);
                revenuesQuery = revenuesQuery.Where(r => r.ChannelPartnerId == channelPartnerId);
            }
            else if (role?.ToLower() == "admin")
            {
                // Admin sees only admin data and admin agents' data (NOT channel partners)
                expensesQuery = expensesQuery.Where(e => e.ChannelPartnerId == null);
                revenuesQuery = revenuesQuery.Where(r => r.ChannelPartnerId == null);
                bookingsQuery = bookingsQuery.Where(b => b.ChannelPartnerId == null);
                var adminBookingIds = await bookingsQuery.Select(b => b.BookingId).ToListAsync();
                paymentsQuery = paymentsQuery.Where(p => adminBookingIds.Contains(p.BookingId));
            }
            else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
            {
                var myLeadIds = leads.Select(l => l.LeadId).ToList();
                bookingsQuery = bookingsQuery.Where(b => myLeadIds.Contains(b.LeadId));
                var myBookingIds = await bookingsQuery.Select(b => b.BookingId).ToListAsync();
                paymentsQuery = paymentsQuery.Where(p => myBookingIds.Contains(p.BookingId));
            }

            // Calculate revenue from Revenues table + Payments + Razorpay subscription transactions
            var revenueFromTable = await revenuesQuery.SumAsync(r => (decimal?)r.Amount) ?? 0;
            var revenueFromPayments = await paymentsQuery.SumAsync(p => (decimal?)p.Amount) ?? 0;

            // Include successful Razorpay subscription payments
            var razorpayRevenue = await _context.PaymentTransactions
                .Where(t => t.Status == "Success" && t.TransactionType != "Refund" && t.TransactionType != "Cancellation")
                .SumAsync(t => (decimal?)t.Amount) ?? 0;

            var totalRevenue = revenueFromTable + revenueFromPayments + razorpayRevenue;
            var totalExpenses = await expensesQuery.SumAsync(e => (decimal?)e.Amount) ?? 0;
            var totalProfit = totalRevenue - totalExpenses;

            var payments = await paymentsQuery.ToListAsync();
            var expenses = await expensesQuery.ToListAsync();
            var revenues = await revenuesQuery.ToListAsync();
            var razorpayTransactions = await _context.PaymentTransactions
                .Where(t => t.Status == "Success" && t.TransactionType != "Refund" && t.TransactionType != "Cancellation")
                .ToListAsync();

            var revenueExpenses = Enumerable.Range(0, months).Select(i =>
            {
                var date = IndianTime.Now.AddMonths(-i);
                var revenueFromPayments = payments.Where(p => p.PaymentDate.Month == date.Month && p.PaymentDate.Year == date.Year).Sum(p => (decimal?)p.Amount) ?? 0;
                var revenueFromTable = revenues.Where(r => r.Date.Month == date.Month && r.Date.Year == date.Year).Sum(r => (decimal?)r.Amount) ?? 0;
                var razorpayMonthly = razorpayTransactions.Where(t => t.TransactionDate.Month == date.Month && t.TransactionDate.Year == date.Year).Sum(t => (decimal?)t.Amount) ?? 0;

                var revenue = revenueFromPayments + revenueFromTable + razorpayMonthly;
                var expenseAmount = expenses.Where(e => e.Date.Month == date.Month && e.Date.Year == date.Year).Sum(e => (decimal?)e.Amount) ?? 0;

                return new { month = date.ToString("MMM yy"), revenue, expenses = expenseAmount };
            }).Reverse().ToList();

            // Combine booking payments + Razorpay subscription payments for transactions list
            var recentTransactions = payments
                .Select(p => new { p.PaymentId, p.Amount, PaymentDate = p.PaymentDate.ToString("MMM dd, yyyy"), p.PaymentMethod })
                .Union(razorpayTransactions.Select(t => new { PaymentId = t.TransactionId, t.Amount, PaymentDate = t.TransactionDate.ToString("MMM dd, yyyy"), t.PaymentMethod }))
                .OrderByDescending(x => x.PaymentDate)
                .Take(5)
                .ToList();

            return Json(new { totalLeads, facebookLeads, monthlyLeads, sources, pipeline, newLeads, totalRevenue, totalExpenses, totalProfit, revenueExpenses, recentTransactions });
        }


        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetSalesDashboardData([FromQuery] int months = 6)
        {
            var role = User?.FindFirst(ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid ?? "0", out int userId);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;

            var myLeads = channelPartnerId.HasValue
                ? await _context.Leads
                    .Where(l => l.ChannelPartnerId == channelPartnerId && (l.PartnerAssignedAgentUserId ?? l.ExecutiveId) == userId)
                    .ToListAsync()
                : await _context.Leads.Where(l => l.ExecutiveId == userId).ToListAsync();
            var myLeadIds = myLeads.Select(l => l.LeadId).ToList();
            var myBookings = await _context.Bookings.Where(b => myLeadIds.Contains(b.LeadId)).ToListAsync();
            var myBookingIds = myBookings.Select(b => b.BookingId).ToList();
            var myPayments = await _context.Payments.Where(p => myBookingIds.Contains(p.BookingId)).ToListAsync();

            var revenuesQuery = _context.Revenues.AsQueryable();
            var expensesQuery = _context.Expenses.AsQueryable();
            var isPartnerTeam = channelPartnerId.HasValue &&
                (role?.ToLower() == "partner" || role?.ToLower() == "sales" || role?.ToLower() == "agent");

            // Keep dashboard financials in the same scope as the signed-in user.
            if (role?.ToLower() == "partner")
            {
                revenuesQuery = revenuesQuery.Where(r => r.ChannelPartnerId == channelPartnerId);
                expensesQuery = expensesQuery.Where(e => e.ChannelPartnerId == channelPartnerId);
            }
            else if (role?.ToLower() == "admin")
            {
                revenuesQuery = revenuesQuery.Where(r => r.ChannelPartnerId == null);
                expensesQuery = expensesQuery.Where(e => e.ChannelPartnerId == null);
            }
            else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
            {
                if (channelPartnerId.HasValue)
                {
                    revenuesQuery = revenuesQuery.Where(r => r.ChannelPartnerId == channelPartnerId);
                    expensesQuery = expensesQuery.Where(e => e.ChannelPartnerId == channelPartnerId);
                }
                else
                {
                    revenuesQuery = revenuesQuery.Where(r => r.ChannelPartnerId == null);
                    expensesQuery = expensesQuery.Where(e => e.ChannelPartnerId == null);
                }
            }

            var myRevenues = await revenuesQuery
                .Where(r => !(r.Type == "Booking" &&
                              (r.Description ?? "").Contains("Total Booked Amount") &&
                              (r.Description ?? "").Contains("from Bookings")))
                .ToListAsync();
            var revenueFromTable = myRevenues.Sum(r => (decimal?)r.Amount) ?? 0;
            var revenueFromPayments = myPayments.Sum(p => (decimal?)p.Amount) ?? 0;
            var partnerCommission = 0m;
            if (isPartnerTeam)
            {
                partnerCommission = await _context.ChannelPartnerCommissionLogs
                    .Where(c => c.PartnerId == channelPartnerId.Value)
                    .SumAsync(c => (decimal?)c.FixedCommissionAmount) ?? 0m;
            }
            // For partner-side dashboards, earnings should reflect partner revenue/commission,
            // not gross booking collections to avoid inflated totals.
            var totalEarning = isPartnerTeam
                ? (revenueFromTable + partnerCommission)
                : (revenueFromTable + revenueFromPayments + partnerCommission);
            var totalExpenses = await expensesQuery.SumAsync(e => (decimal?)e.Amount) ?? 0;
            var totalProfit = totalEarning - totalExpenses;

            var salesReport = Enumerable.Range(0, months).Select(i =>
            {
                var date = IndianTime.Now.AddMonths(-i);
                var sales = myBookings.Count(b => b.CreatedOn.Month == date.Month && b.CreatedOn.Year == date.Year);
                return new { month = date.ToString("MMM yy"), sales };
            }).Reverse().ToList();

            var recentBookings = myBookings.OrderByDescending(b => b.CreatedOn).Take(5)
                .Select(b => new { b.BookingId, b.BookingAmount, b.Status, CreatedOn = b.CreatedOn.ToString("MMM dd, yyyy") }).ToList();

            var (paidCount, pendingCount, overdueCount) = CalculateSalesStatusCounts(myBookings);

            return Json(new { totalEarning, totalExpenses, totalProfit, salesReport, recentBookings, salesStatus = new { paidCount, pendingCount, overdueCount } });
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetTeamDashboardData()
        {
            var role = User?.FindFirst(ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid ?? "0", out int userId);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;

            var usersQuery = _context.Users.Where(u => u.Role == "Sales" || u.Role == "Agent");
            if (role?.ToLower() == "partner")
                usersQuery = usersQuery.Where(u => u.ChannelPartnerId == channelPartnerId);
            else if (role?.ToLower() == "admin")
                usersQuery = usersQuery.Where(u => u.ChannelPartnerId == null);

            var totalTeamMembers = await usersQuery.CountAsync();
            var newTeamMembers = await usersQuery.CountAsync(u => u.CreatedDate >= IndianTime.Now.AddDays(-7));
            var totalChannelPartners = 0;

            if (role?.ToLower() == "admin")
            {
                totalChannelPartners = await _context.ChannelPartners.CountAsync();
            }

            var teamPerformance = await usersQuery
                .Select(u => new { u.UserId, u.Username, leadsCount = _context.Leads.Count(l => l.ExecutiveId == u.UserId), bookingsCount = _context.Bookings.Count(b => _context.Leads.Any(l => l.LeadId == b.LeadId && l.ExecutiveId == u.UserId)) })
                .OrderByDescending(x => x.bookingsCount).Take(10).ToListAsync();

            var topPerformers = teamPerformance.Take(5).ToList();

            return Json(new { totalTeamMembers, newTeamMembers, totalChannelPartners, teamPerformance, topPerformers });
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetAllLeads()
        {
            var role = User?.FindFirst(ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid ?? "0", out int userId);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;

            var query = _context.Leads.AsQueryable();
            if (role?.ToLower() == "partner")
                query = query.Where(l => l.ChannelPartnerId == channelPartnerId);
            else if (role?.ToLower() == "admin")
                query = query.Where(l => l.ChannelPartnerId == null);
            else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
            {
                if (channelPartnerId.HasValue)
                {
                    query = query.Where(l => l.ChannelPartnerId == channelPartnerId && (l.PartnerAssignedAgentUserId ?? l.ExecutiveId) == userId);
                }
                else
                {
                    query = query.Where(l => l.ExecutiveId == userId);
                }
            }

            var allLeads = await query.OrderByDescending(l => l.CreatedOn)
                .Select(l => new
                {
                    l.LeadId,
                    encodedId = IdObfuscator.Encode(l.LeadId), // ? ADD THIS
                    l.Name,
                    l.Contact,
                    l.Stage,
                    CreatedOn = l.CreatedOn.ToString("MMM dd, yyyy")
                })
                .ToListAsync();

            return Json(allLeads);
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetAllTransactions()
        {
            var role = User?.FindFirst(ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid ?? "0", out int userId);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;

            var paymentsQuery = _context.Payments.AsQueryable();

            if (role?.ToLower() == "partner")
            {
                var partnerLeadIds = await _context.Leads.Where(l => l.ChannelPartnerId == channelPartnerId).Select(l => l.LeadId).ToListAsync();
                var partnerBookingIds = await _context.Bookings.Where(b => partnerLeadIds.Contains(b.LeadId)).Select(b => b.BookingId).ToListAsync();
                paymentsQuery = paymentsQuery.Where(p => partnerBookingIds.Contains(p.BookingId));
            }
            else if (role?.ToLower() == "admin")
            {
                var adminLeadIds = await _context.Leads.Where(l => l.ChannelPartnerId == null || l.HandoverStatus == "ReadyToBook" || l.HandoverStatus == "HandedOver").Select(l => l.LeadId).ToListAsync();
                var adminBookingIds = await _context.Bookings.Where(b => adminLeadIds.Contains(b.LeadId)).Select(b => b.BookingId).ToListAsync();
                paymentsQuery = paymentsQuery.Where(p => adminBookingIds.Contains(p.BookingId));
            }
            else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
            {
                var myLeadIds = channelPartnerId.HasValue
                    ? await _context.Leads.Where(l => l.ChannelPartnerId == channelPartnerId && (l.PartnerAssignedAgentUserId ?? l.ExecutiveId) == userId).Select(l => l.LeadId).ToListAsync()
                    : await _context.Leads.Where(l => l.ExecutiveId == userId).Select(l => l.LeadId).ToListAsync();
                var myBookingIds = await _context.Bookings.Where(b => myLeadIds.Contains(b.LeadId)).Select(b => b.BookingId).ToListAsync();
                paymentsQuery = paymentsQuery.Where(p => myBookingIds.Contains(p.BookingId));
            }

            var allTransactions = await paymentsQuery.OrderByDescending(p => p.PaymentDate)
                .Select(p => new { p.PaymentId, p.Amount, PaymentDate = p.PaymentDate.ToString("MMM dd, yyyy"), p.PaymentMethod })
                .ToListAsync();

            return Json(allTransactions);
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetSalesOverviewData()
        {
            try
            {
                var role = User?.FindFirst(ClaimTypes.Role)?.Value;
                var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(uid ?? "0", out int userId);
                var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
                var channelPartnerId = currentUser?.ChannelPartnerId;
                var isPartner = role != null && role.ToLower() == "partner";
                var isPartnerTeam = channelPartnerId.HasValue &&
                    (role?.ToLower() == "partner" || role?.ToLower() == "sales" || role?.ToLower() == "agent");

                var allBookings = _context.Bookings.AsQueryable();
                var paymentsQuery = _context.Payments.AsQueryable();
                var expensesQuery = _context.Expenses.AsQueryable();
                var revenuesQuery = _context.Revenues.AsQueryable();

                if (role?.ToLower() == "partner")
                {
                    var partnerLeadIds = await _context.Leads
                        .Where(l => l.ChannelPartnerId == channelPartnerId)
                        .Select(l => l.LeadId)
                        .ToListAsync();

                    allBookings = allBookings.Where(b => partnerLeadIds.Contains(b.LeadId));
                    var partnerBookingIds = await allBookings.Select(b => b.BookingId).ToListAsync();
                    paymentsQuery = paymentsQuery.Where(p => partnerBookingIds.Contains(p.BookingId));
                    expensesQuery = expensesQuery.Where(e => e.ChannelPartnerId == channelPartnerId);
                    revenuesQuery = revenuesQuery.Where(r => r.ChannelPartnerId == channelPartnerId);
                }
                else if (role?.ToLower() == "admin")
                {
                    expensesQuery = expensesQuery.Where(e => e.ChannelPartnerId == null);
                    revenuesQuery = revenuesQuery.Where(r => r.ChannelPartnerId == null);
                    allBookings = allBookings.Where(b => b.ChannelPartnerId == null);
                    var adminBookingIds = await allBookings.Select(b => b.BookingId).ToListAsync();
                    paymentsQuery = paymentsQuery.Where(p => adminBookingIds.Contains(p.BookingId));
                }
                else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
                {
                    var myLeadIds = channelPartnerId.HasValue
                        ? await _context.Leads.Where(l => l.ChannelPartnerId == channelPartnerId && (l.PartnerAssignedAgentUserId ?? l.ExecutiveId) == userId).Select(l => l.LeadId).ToListAsync()
                        : await _context.Leads.Where(l => l.ExecutiveId == userId).Select(l => l.LeadId).ToListAsync();
                    allBookings = allBookings.Where(b => myLeadIds.Contains(b.LeadId));
                    var myBookingIds = await allBookings.Select(b => b.BookingId).ToListAsync();
                    paymentsQuery = paymentsQuery.Where(p => myBookingIds.Contains(p.BookingId));

                    if (channelPartnerId.HasValue)
                    {
                        expensesQuery = expensesQuery.Where(e => e.ChannelPartnerId == channelPartnerId);
                        revenuesQuery = revenuesQuery.Where(r => r.ChannelPartnerId == channelPartnerId);
                    }
                    else
                    {
                        expensesQuery = expensesQuery.Where(e => e.ChannelPartnerId == null);
                        revenuesQuery = revenuesQuery.Where(r => r.ChannelPartnerId == null);
                    }
                }

                var bookings = await allBookings.ToListAsync();
                var revenueFromTable = await revenuesQuery
                    .Where(r => !(r.Type == "Booking" &&
                                  (r.Description ?? "").Contains("Total Booked Amount") &&
                                  (r.Description ?? "").Contains("from Bookings")))
                    .SumAsync(r => (decimal?)r.Amount) ?? 0;
                var revenueFromPayments = await paymentsQuery.SumAsync(p => (decimal?)p.Amount) ?? 0;
                var partnerCommission = 0m;
                if (isPartnerTeam)
                {
                    partnerCommission = await _context.ChannelPartnerCommissionLogs
                        .Where(c => c.PartnerId == channelPartnerId.Value)
                        .SumAsync(c => (decimal?)c.FixedCommissionAmount) ?? 0m;
                }
                // For partner-side dashboards, earnings should reflect partner revenue/commission,
                // not gross booking collections to avoid inflated totals.
                var totalEarning = isPartnerTeam
                    ? (revenueFromTable + partnerCommission)
                    : (revenueFromTable + revenueFromPayments + partnerCommission);
                var totalExpenses = await expensesQuery.SumAsync(e => (decimal?)e.Amount) ?? 0;
                var totalProfit = totalEarning - totalExpenses;

                var payments = await paymentsQuery.ToListAsync();
                var revenues = await revenuesQuery.ToListAsync();
                var salesReport = Enumerable.Range(0, 6).Select(i =>
                {
                    var date = IndianTime.Now.AddMonths(-i);
                    var sales = bookings.Count(b => b.CreatedOn.Month == date.Month && b.CreatedOn.Year == date.Year);
                    return new { month = date.ToString("MMM"), sales };
                }).Reverse().ToList();

                var recentBookings = bookings.OrderByDescending(b => b.CreatedOn).Take(5)
                    .Select(b => new
                    {
                        b.BookingId,
                        b.BookingAmount,
                        b.Status,
                        CreatedOn = b.CreatedOn.ToString("MMM dd, yyyy")
                    }).ToList();

                var (paidCount, pendingCount, overdueCount) = CalculateSalesStatusCounts(bookings);

                return Json(new { totalEarning, totalExpenses, totalProfit, salesReport, recentBookings, salesStatus = new { paidCount, pendingCount, overdueCount } });
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetPartnerDashboardData([FromQuery] int months = 6)
        {
            try
            {
                // Use username from claim (more reliable than UserId which may be 0 in MongoDB)
                var username = User?.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(username))
                {
                    return Json(new { totalLeads = 0, totalCommission = 0m, conversionRate = 0m, monthlyRevenue = 0m, leadPerformance = new object[0], leadStatus = new { newLeads = 0, contacted = 0, qualified = 0, converted = 0 }, commissionTrend = new object[0], recentLeads = new object[0] });
                }
                var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
                var channelPartnerId = currentUser?.ChannelPartnerId;

                // Runtime fallback: if the user's ChannelPartnerId is null or 0, look up a ChannelPartner record by UserId
                if ((!channelPartnerId.HasValue || channelPartnerId.Value == 0) && currentUser != null)
                {
                    var partnerRecord = await _context.ChannelPartners
                        .Where(cp => cp.UserId == currentUser.UserId && cp.PartnerId > 0)
                        .OrderByDescending(cp => cp.PartnerId)
                        .FirstOrDefaultAsync();
                    if (partnerRecord != null)
                    {
                        channelPartnerId = partnerRecord.PartnerId;
                    }
                }

                if (!channelPartnerId.HasValue)
                {
                    return Json(new
                    {
                        totalLeads = 0,
                        totalCommission = 0m,
                        conversionRate = 0m,
                        monthlyRevenue = 0m,
                        leadPerformance = new object[0],
                        leadStatus = new { newLeads = 0, contacted = 0, qualified = 0, converted = 0 },
                        commissionTrend = new object[0],
                        recentLeads = new object[0]
                    });
                }

                var leads = await _context.Leads
                    .Where(l => l.ChannelPartnerId == channelPartnerId.Value)
                    .ToListAsync();

                var leadIds = leads.Select(l => l.LeadId).ToList();

                var bookings = await _context.Bookings
                    .Where(b => b.ChannelPartnerId == channelPartnerId.Value || leadIds.Contains(b.LeadId))
                    .ToListAsync();

                var bookingIds = bookings.Select(b => b.BookingId).ToList();

                var payments = await _context.Payments
                    .Where(p => bookingIds.Contains(p.BookingId))
                    .ToListAsync();

                var commissionLogs = await _context.ChannelPartnerCommissionLogs
                    .Where(c => c.PartnerId == channelPartnerId.Value)
                    .ToListAsync();

                var totalLeads = leads.Count;
                var convertedLeadCount = bookings
                    .Where(b => !string.Equals(b.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                    .Select(b => b.LeadId)
                    .Distinct()
                    .Count();
                var conversionRate = totalLeads > 0 ? Math.Round((decimal)convertedLeadCount * 100m / totalLeads, 2) : 0m;

                var totalCommission = commissionLogs.Sum(c => c.FixedCommissionAmount);

                var monthStart = new DateTime(IndianTime.Now.Year, IndianTime.Now.Month, 1);
                var monthlyRevenue = commissionLogs
                    .Where(c => c.SaleDate >= monthStart)
                    .Sum(c => c.FixedCommissionAmount);

                var leadPerformance = Enumerable.Range(0, months)
                    .Select(i => IndianTime.Now.AddMonths(-(months - 1 - i)))
                    .Select(monthDate =>
                    {
                        var start = new DateTime(monthDate.Year, monthDate.Month, 1);
                        var end = start.AddMonths(1);

                        var monthLeads = leads.Count(l => l.CreatedOn >= start && l.CreatedOn < end);
                        var monthConversions = bookings.Count(b => b.BookingDate >= start && b.BookingDate < end && !string.Equals(b.Status, "Cancelled", StringComparison.OrdinalIgnoreCase));

                        return new
                        {
                            month = monthDate.ToString("MMM"),
                            leads = monthLeads,
                            conversions = monthConversions
                        };
                    })
                    .ToList();

                var leadStatus = new
                {
                    newLeads = leads.Count(l => string.Equals(l.Stage, "New", StringComparison.OrdinalIgnoreCase)),
                    contacted = leads.Count(l => (l.Stage ?? string.Empty).Contains("Contact", StringComparison.OrdinalIgnoreCase)),
                    qualified = leads.Count(l => (l.Stage ?? string.Empty).Contains("Qual", StringComparison.OrdinalIgnoreCase)),
                    converted = convertedLeadCount
                };

                var commissionTrend = Enumerable.Range(0, months)
                    .Select(i => IndianTime.Now.AddMonths(-(months - 1 - i)))
                    .Select(monthDate => new
                    {
                        month = monthDate.ToString("MMM"),
                        commission = commissionLogs
                            .Where(c => c.SaleDate.Month == monthDate.Month && c.SaleDate.Year == monthDate.Year)
                            .Sum(c => c.FixedCommissionAmount)
                    })
                    .ToList();

                var recentLeads = leads
                    .OrderByDescending(l => l.CreatedOn)
                    .Take(10)
                    .Select(l =>
                    {
                        var latestBooking = bookings
                            .Where(b => b.LeadId == l.LeadId)
                            .OrderByDescending(b => b.BookingDate)
                            .FirstOrDefault();

                        return new
                        {
                            name = l.Name ?? "Unnamed Lead",
                            status = l.Stage ?? l.Status ?? "New",
                            value = latestBooking?.TotalAmount ?? 0m,
                            date = l.CreatedOn.ToString("yyyy-MM-dd")
                        };
                    })
                    .ToList();

                return Json(new
                {
                    totalLeads,
                    totalCommission,
                    conversionRate,
                    monthlyRevenue,
                    leadPerformance,
                    leadStatus,
                    commissionTrend,
                    recentLeads
                });
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        private static (int paidCount, int pendingCount, int overdueCount) CalculateSalesStatusCounts(IEnumerable<BookingModel> bookings)
        {
            var paidCount = 0;
            var pendingCount = 0;
            var overdueCount = 0;

            foreach (var booking in bookings)
            {
                var status = (booking.Status ?? string.Empty).Trim().ToLowerInvariant();

                if (status == "paid" || status == "completed" || status == "confirmed" || status == "success" || status == "closed")
                {
                    paidCount++;
                }
                else if (status == "overdue" || status == "expired" || status == "cancelled" || status == "canceled" || status == "failed")
                {
                    overdueCount++;
                }
                else
                {
                    pendingCount++;
                }
            }

            return (paidCount, pendingCount, overdueCount);
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetPlanUsage()
        {
            try
            {
                var tenantIdClaim = User.FindFirst("TenantId")?.Value;
                if (string.IsNullOrEmpty(tenantIdClaim) || !int.TryParse(tenantIdClaim, out int tid))
                    return Json(new { success = false, message = "No tenant context" });

                // Get active subscription
                var subscription = await _masterDb.TenantSubscriptions
                    .FirstOrDefaultAsync(s => s.TenantId == tid && (s.Status == "Active" || s.Status == "Trial"));

                if (subscription == null)
                    return Json(new { success = false, message = "No active subscription" });

                // Look up plan
                var plan = await _masterDb.SaasPlans.FirstOrDefaultAsync(p => p.PlanId == subscription.PlanId);
                if (plan == null)
                    return Json(new { success = false, message = "Plan not found" });

                // Count current usage
                var currentUsers = await _context.Users.CountAsync();
                var currentAgents = await _context.Users.CountAsync(u => u.Role == "Sales" || u.Role == "Agent");
                var monthStart = new DateTime(IndianTime.Now.Year, IndianTime.Now.Month, 1);
                var monthEnd = monthStart.AddMonths(1);
                var leadsThisMonth = await _context.Leads.CountAsync(l => l.CreatedOn >= monthStart && l.CreatedOn < monthEnd);
                var currentPartners = await _context.ChannelPartners.CountAsync();

                var usage = new
                {
                    planName = plan.PlanName,
                    planType = plan.PlanType,
                    billingCycle = subscription.BillingCycle,
                    status = subscription.Status,
                    endDate = subscription.EndDate.ToString("yyyy-MM-dd"),
                    daysRemaining = (subscription.EndDate - IndianTime.Now).Days,
                    users = new
                    {
                        current = currentUsers,
                        max = plan.MaxUsers,
                        unlimited = plan.MaxUsers == -1,
                        percent = plan.MaxUsers > 0 ? (int)((double)currentUsers / plan.MaxUsers * 100) : 0
                    },
                    agents = new
                    {
                        current = currentAgents,
                        max = plan.MaxAgents,
                        unlimited = plan.MaxAgents == -1,
                        percent = plan.MaxAgents > 0 ? (int)((double)currentAgents / plan.MaxAgents * 100) : 0
                    },
                    leads = new
                    {
                        current = leadsThisMonth,
                        max = plan.MaxLeadsPerMonth,
                        unlimited = plan.MaxLeadsPerMonth == -1,
                        percent = plan.MaxLeadsPerMonth > 0 ? (int)((double)leadsThisMonth / plan.MaxLeadsPerMonth * 100) : 0
                    },
                    partners = new
                    {
                        current = currentPartners,
                        max = plan.MaxPartners,
                        unlimited = plan.MaxPartners == -1,
                        percent = plan.MaxPartners > 0 ? (int)((double)currentPartners / plan.MaxPartners * 100) : 0
                    },
                    features = new
                    {
                        hasWhatsApp = plan.HasWhatsAppIntegration,
                        hasFacebook = plan.HasFacebookIntegration,
                        hasEmail = plan.HasEmailIntegration,
                        hasAdvancedReports = plan.HasAdvancedReports,
                        hasCustomBranding = plan.HasCustomBranding,
                        hasPrioritySupport = plan.HasPrioritySupport,
                        hasCustomAPI = plan.HasCustomAPIAccess,
                        hasImpersonation = plan.HasImpersonation,
                        hasLeadScoring = plan.HasLeadScoring,
                        hasSiteVisitManagement = plan.HasSiteVisitManagement,
                        hasDocumentManagement = plan.HasDocumentManagement,
                        hasInventoryManagement = plan.HasInventoryManagement,
                        hasCampaignManagement = plan.HasCampaignManagement,
                        hasLegalManagement = plan.HasLegalManagement,
                        hasInvoiceAutomation = plan.HasInvoiceAutomation,
                        hasQuotationManagement = plan.HasQuotationManagement,
                        hasWorkflowAutomation = plan.HasWorkflowAutomation,
                        hasCustomerPortal = plan.HasCustomerPortal,
                        hasAIScoring = plan.HasAIScoring,
                        hasAIChatbot = plan.HasAIChatbot,
                        hasMobileApp = plan.HasMobileApp,
                        hasTwoFactorAuth = plan.HasTwoFactorAuth,
                        hasCallIntegration = plan.HasCallIntegration,
                        hasSmsIntegration = plan.HasSmsIntegration,
                        hasMultiLanguage = plan.HasMultiLanguage,
                        hasGpsTracking = plan.HasGpsTracking,
                        maxSiteVisitsPerMonth = plan.MaxSiteVisitsPerMonth,
                        maxDocuments = plan.MaxDocuments,
                        maxProperties = plan.MaxProperties,
                        maxQuotationsPerMonth = plan.MaxQuotationsPerMonth,
                        maxEmailCampaigns = plan.MaxEmailCampaigns
                    }
                };

                return Json(new { success = true, usage });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetDashboardSettings()
        {
            try
            {
                var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(uid, out int userId))
                    return Json(new { success = false, message = "User not found" });

                var settings = _context.UserDashboardSettings.FirstOrDefault(s => s.UserId == userId);
                if (settings == null)
                {
                    // Return defaults
                    return Json(new { success = true, settings = new UserDashboardSetting { UserId = userId } });
                }

                return Json(new { success = true, settings });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> SaveDashboardSettings([FromBody] UserDashboardSetting model)
        {
            try
            {
                var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(uid, out int userId))
                    return Json(new { success = false, message = "User not found" });

                model.UserId = userId;

                var existing = _context.UserDashboardSettings.FirstOrDefault(s => s.UserId == userId);
                if (existing != null)
                {
                    // Update all properties
                    existing.ShowStatsCards = model.ShowStatsCards;
                    existing.ShowLeadGrowthChart = model.ShowLeadGrowthChart;
                    existing.ShowTrafficSources = model.ShowTrafficSources;
                    existing.ShowSalesPipeline = model.ShowSalesPipeline;
                    existing.ShowLeadsList = model.ShowLeadsList;
                    existing.ShowRevenueExpensesChart = model.ShowRevenueExpensesChart;
                    existing.ShowTransactionsList = model.ShowTransactionsList;
                    existing.ShowQuickAccess = model.ShowQuickAccess;
                    existing.ShowPlanUsage = model.ShowPlanUsage;
                    existing.ShowSalesStats = model.ShowSalesStats;
                    existing.ShowSalesChart = model.ShowSalesChart;
                    existing.ShowSalesStatus = model.ShowSalesStatus;
                    existing.ShowSalesBookings = model.ShowSalesBookings;
                    existing.ShowPartnerStats = model.ShowPartnerStats;
                    existing.ShowPartnerLeadChart = model.ShowPartnerLeadChart;
                    existing.ShowPartnerLeadStatus = model.ShowPartnerLeadStatus;
                    existing.ShowPartnerCommissions = model.ShowPartnerCommissions;
                    existing.ShowUpcomingFollowups = model.ShowUpcomingFollowups;
                    existing.ShowRecentActivities = model.ShowRecentActivities;
                    existing.ShowTeamPerformance = model.ShowTeamPerformance;
                    existing.ShowTopPerformers = model.ShowTopPerformers;
                    existing.ModifiedOn = DateTime.UtcNow;
                }
                else
                {
                    model.CreatedOn = DateTime.UtcNow;
                    _context.UserDashboardSettings.Add(model);
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Dashboard settings saved" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetPartnerSubscriptionStatus()
        {
            try
            {
                var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(uid, out int userId))
                    return Json(new { hasSubscription = false });

                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
                if (user?.ChannelPartnerId == null)
                    return Json(new { hasSubscription = false });

                var subscription = await _context.PartnerSubscriptions
                    .Where(s => s.ChannelPartnerId == user.ChannelPartnerId && (s.Status == "Active" || s.Status == "Trial"))
                    .FirstOrDefaultAsync();

                if (subscription == null)
                    return Json(new { hasSubscription = false });

                // Look up plan separately since .Include() is no-op on MongoDbSet
                var plan = subscription.PlanId > 0
                    ? await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.PlanId == subscription.PlanId)
                    : null;

                var daysUntilExpiry = (subscription.EndDate - IndianTime.Now).Days;

                return Json(new
                {
                    hasSubscription = true,
                    planName = plan?.PlanName,
                    billingCycle = subscription.BillingCycle,
                    amount = subscription.Amount,
                    startDate = subscription.StartDate.ToString("yyyy-MM-dd"),
                    endDate = subscription.EndDate.ToString("yyyy-MM-dd"),
                    daysUntilExpiry = daysUntilExpiry,
                    isTrial = subscription.BillingCycle == "Trial"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting partner subscription status");
                return Json(new { hasSubscription = false, error = ex.Message });
            }
        }

    }
}
