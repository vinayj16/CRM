using CRM.Attributes;
using CRM.Helpers;
using CRM.MasterDb;
using CRM.Models;
using CRM.Services;
using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;

namespace CRM.Controllers
{
    [RoleAuthorize("Admin", "Partner", "Agent", "Sales")]

    public class ManageUsersController : Controller
    {
        private readonly AppDbContext _context;
        private readonly MasterDbContext _masterDb;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly Services.PermissionService _permissionService;
        private readonly SubscriptionService _subscriptionService;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ManageUsersController> _logger;

        public ManageUsersController(AppDbContext context, MasterDbContext masterDb, IHttpContextAccessor httpContextAccessor, Services.PermissionService permissionService, SubscriptionService subscriptionService, IWebHostEnvironment webHostEnvironment, IConfiguration configuration, ILogger<ManageUsersController> logger)
        {
            _context = context;
            _masterDb = masterDb;
            _httpContextAccessor = httpContextAccessor;
            _permissionService = permissionService;
            _subscriptionService = subscriptionService;
            _webHostEnvironment = webHostEnvironment;
            _configuration = configuration;
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

        /// <summary>
        /// Resolves the tenant (company) the current user belongs to, so any user they
        /// create is always bound to their own company. Prefers the user's DB record
        /// (source of truth), falling back to the middleware-provided TenantId.
        /// </summary>
        private int GetActingTenantId(int? userId)
        {
            if (userId.HasValue)
            {
                var tenantId = _context.Users.FirstOrDefault(u => u.UserId == userId.Value)?.TenantId ?? 0;
                if (tenantId > 0) return tenantId;
            }
            return HttpContext.Items["TenantId"] as int? ?? 0;
        }
        [Route("manageusers")]
        [Route("ManageUsers/Index")]
        public async Task<IActionResult> Index()
        {
            var (userId, role, channelPartnerId) = GetCurrentUserContext();

            IQueryable<UserModel> usersQuery = _context.Users.AsQueryable();

            if (role?.ToLower() == "partner")
            {
                // Partner sees only their agents (exclude themselves)
                usersQuery = usersQuery.Where(u => u.ChannelPartnerId == channelPartnerId && u.UserId != userId);
            }
            else if (role?.ToLower() == "admin")
            {
                // Admin sees only the users of their own company (tenant)
                var adminTenantId = GetActingTenantId(userId);
                if (adminTenantId > 0)
                {
                    usersQuery = usersQuery.Where(u => u.TenantId == adminTenantId);
                }
            }

            var users = await usersQuery.ToListAsync();

            // Filter roles based on current user
            //var allowedRoles = role?.ToLower() == "partner" 
            //    ? _context.RolePermissions.Select(r => r.RoleName).ToList()
            //    : _context.RolePermissions.Select(r => r.RoleName).ToList();
            var allowedRoles = role?.ToLower() == "partner"
                         ? _context.RolePermissions
                             .Where(r => r.ChannelPartnerId != null || r.RoleName == "Agent")
                             .Select(r => r.RoleName)
                             .ToList()
                         : _context.RolePermissions
                             .Where(r => r.ChannelPartnerId == null)
                             .Select(r => r.RoleName)
                             .ToList();


            var tenantIdClaim = User.FindFirst("TenantId")?.Value;
            bool hasPartnerFeature = false;

            if (!string.IsNullOrEmpty(tenantIdClaim) && int.TryParse(tenantIdClaim, out int tid))
            {
                var activeSub = _masterDb.TenantSubscriptions
                    .Where(s => s.TenantId == tid && (s.Status == "Active" || s.Status == "Trial"))
                    .OrderByDescending(s => s.StartDate)
                    .FirstOrDefault();

                if (activeSub != null)
                {
                    var activePlan = _masterDb.SaasPlans.FirstOrDefault(p => p.PlanId == activeSub.PlanId);
                    hasPartnerFeature = activePlan?.MaxPartners > 0;
                }
            }
            if (hasPartnerFeature)
            {
                ViewBag.Roles = allowedRoles;
            }
            else
            {
                ViewBag.Roles = allowedRoles
                    .Where(r => r != "Partner")
                    .ToList();
            }
            return View(users);
        }
        [HttpGet]
        [RoleAuthorize("Admin")]
        [Route("partnerapproval")]
        public async Task<IActionResult> PartnerApproval()
        {
            var partners = await _context.ChannelPartners
                .Where(p => p.Status != "Deleted")
                .OrderByDescending(p => p.CreatedOn)
                .ToListAsync();
            return View(partners);
        }

        [HttpPost]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> CreatePartner(ChannelPartnerModel model, List<IFormFile> DocumentFiles)
        {
            try
            {
                if (await _context.ChannelPartners.AnyAsync(p => p.Email == model.Email && p.Status != "Deleted"))
                    return Json(new { success = false, message = "A partner with this email already exists" });

                model.Status = "Approved";
                model.CreatedOn = IndianTime.Now;
                model.ApprovedOn = IndianTime.Now;

                var (userId, role, _) = GetCurrentUserContext();
                model.ApprovedBy = userId;

                model.Subdomain = await GenarateSubdomainAsync(model.CompanyName);
                _context.ChannelPartners.Add(model);
                await _context.SaveChangesAsync();

                var password = "Partner@" + model.PartnerId;
                var user = new UserModel
                {
                    Username = model.ContactPerson,
                    Email = model.Email,
                    Password = PasswordHelper.HashPassword(password),
                    Role = "Partner",
                    Phone = model.Phone,
                    IsActive = true,
                    ChannelPartnerId = model.PartnerId,
                    CreatedDate = IndianTime.Now
                };
                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                model.UserId = user.UserId;
                await _context.SaveChangesAsync();
                // Create UserProfile with partner details
                _context.UserProfiles.Add(new UserProfile
                {
                    UserId = user.UserId,
                    Username = model.ContactPerson,
                    Email = model.Email,
                    FirstName = model.ContactPerson,
                    PhoneNumber = model.Phone,
                    Address = model.Address,
                    Location = model.CompanyName
                });
                await _context.SaveChangesAsync();
                if (model.SelectedPlanId.HasValue)
                {
                    var plan = await _context.SubscriptionPlans.FindAsync(model.SelectedPlanId.Value);
                    if (plan != null)
                    {
                        var trialEnd = IndianTime.Now.AddDays(7);
                        _context.PartnerSubscriptions.Add(new PartnerSubscriptionModel
                        {
                            ChannelPartnerId = model.PartnerId,
                            PlanId = plan.PlanId,
                            PaymentMethod = "Trial",
                            BillingCycle = "Trial",
                            Amount = 0,
                            StartDate = IndianTime.Now,
                            EndDate = trialEnd,
                            Status = "Trial",
                            CreatedOn = IndianTime.Now,
                            CreatedBy = userId
                        });
                        await _context.SaveChangesAsync();
                        _ = SendPartnerWelcomeEmailAsync(model.Email, model.ContactPerson, model.Email, password, plan.PlanName, trialEnd);
                    }
                }

                if (DocumentFiles != null && DocumentFiles.Count > 0)
                    await SavePartnerDocumentsAsync(model.PartnerId, DocumentFiles);

                return Json(new { success = true, message = "Partner created successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> ApprovePartner(int partnerId)
        {
            try
            {
                var partner = await _context.ChannelPartners.FindAsync(partnerId);
                if (partner == null) return NotFound();

                var (userId, _, _) = GetCurrentUserContext();
                partner.Status = "Approved";
                partner.ApprovedBy = userId;
                partner.ApprovedOn = IndianTime.Now;
                _context.ChannelPartners.Update(partner);

                if (!partner.UserId.HasValue)
                {
                    // Check if user was already created during registration
                    var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == partner.Email && u.Role == "Partner");
                    if (existingUser != null)
                    {
                        existingUser.IsActive = true;
                        existingUser.ChannelPartnerId = partner.PartnerId;
                        partner.UserId = existingUser.UserId;
                        _context.Users.Update(existingUser);
                        _context.ChannelPartners.Update(partner);
                    }
                    else
                    {
                        var password = "Partner@" + partner.PartnerId;
                        var user = new UserModel
                        {
                            Username = partner.ContactPerson,
                            Email = partner.Email,
                            Password = PasswordHelper.HashPassword(password),
                            Role = "Partner",
                            Phone = partner.Phone,
                            IsActive = true,
                            ChannelPartnerId = partner.PartnerId,
                            CreatedDate = IndianTime.Now
                        };
                        _context.Users.Add(user);
                        await _context.SaveChangesAsync();
                        partner.UserId = user.UserId;
                        _context.ChannelPartners.Update(partner);
                        // Create UserProfile with partner details
                        _context.UserProfiles.Add(new UserProfile
                        {
                            UserId = user.UserId,
                            Username = partner.ContactPerson,
                            Email = partner.Email,
                            FirstName = partner.ContactPerson,
                            PhoneNumber = partner.Phone,
                            Address = partner.Address,
                            Location = partner.CompanyName
                        });
                        await _context.SaveChangesAsync();
                    }
                }
                else
                {
                    // User already linked - just activate
                    var linkedUser = await _context.Users.FindAsync(partner.UserId.Value);
                    if (linkedUser != null)
                    {
                        linkedUser.IsActive = true;
                        _context.Users.Update(linkedUser);
                    }
                }

                await _context.SaveChangesAsync();
                TempData["Success"] = "Partner approved successfully!";
                return RedirectToAction("PartnerApproval");
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error approving partner: " + ex.Message;
                return RedirectToAction("PartnerApproval");
            }
        }

        [HttpPost]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> RejectPartner(int partnerId)
        {
            try
            {
                var partner = await _context.ChannelPartners.FindAsync(partnerId);
                if (partner == null) return NotFound();

                partner.Status = "Rejected";
                _context.ChannelPartners.Update(partner);
                await _context.SaveChangesAsync();

                TempData["Success"] = "Partner rejected successfully.";
                return RedirectToAction("PartnerApproval");
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error rejecting partner: " + ex.Message;
                return RedirectToAction("PartnerApproval");
            }
        }

        // AJAX endpoint for modal population
        [HttpGet]
        public async Task<IActionResult> GetUser(int id)
        {
            var (userId, role, channelPartnerId) = GetCurrentUserContext();
            var tenantIdClaim = User.FindFirst("TenantId")?.Value;

            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return NotFound();

            bool hasPartnerFeature = false;

            if (!string.IsNullOrEmpty(tenantIdClaim) && int.TryParse(tenantIdClaim, out int tid))
            {
                var activeSub = _masterDb.TenantSubscriptions
                    .Where(s => s.TenantId == tid && (s.Status == "Active" || s.Status == "Trial"))
                    .OrderByDescending(s => s.StartDate)
                    .FirstOrDefault();

                if (activeSub != null)
                {
                    var activePlan = _masterDb.SaasPlans.FirstOrDefault(p => p.PlanId == activeSub.PlanId);
                    hasPartnerFeature = activePlan?.MaxPartners > 0;
                }
            }

            return Json(new
            {
                user.UserId,
                user.Username,
                user.Email,
                user.Phone,
                user.Role,
                user.IsActive
            });
        }
        [HttpPost]
        public async Task<IActionResult> AddUser(UserModel user, IFormFile? uploadFile)
        {
            if (ModelState.IsValid)
            {
                if (uploadFile != null)
                {
                    var uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads");
                    if (!Directory.Exists(uploadFolder))
                    {
                        Directory.CreateDirectory(uploadFolder);
                    }

                    var filePath = Path.Combine(uploadFolder, uploadFile.FileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await uploadFile.CopyToAsync(stream);
                    }
                }

                user.CreatedDate = DateTime.UtcNow;
                user.LastActivity = DateTime.UtcNow;

                var (userId, role, channelPartnerId) = GetCurrentUserContext();

                // Always bind the new user to the CURRENT user's own company (tenant).
                // Never trust a posted TenantId: an admin/partner may only create users
                // under their own company, otherwise new users land in TenantId=0 and are
                // invisible to their company (login, filters, dashboards, reports).
                user.TenantId = GetActingTenantId(userId);

                if (role?.ToLower() == "partner")
                {
                    user.ChannelPartnerId = channelPartnerId;

                    if (user.Role == "Sales" || user.Role == "Agent")
                    {
                        var (canAdd, message) = await _subscriptionService.CanAddAgentAsync(channelPartnerId.Value);

                        if (!canAdd)
                        {
                            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                            {
                                return Json(new { success = false, message = message, agentLimitReached = true });
                            }

                            return View(user);
                        }
                    }
                }

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                // Send welcome email to new team member
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var httpContext = _httpContextAccessor.HttpContext;
                        if (httpContext != null)
                        {
                            var scopeFactory = httpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();
                            using var scope = scopeFactory.CreateScope();
                            var emailService = scope.ServiceProvider.GetRequiredService<EmailService>();
                            var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
                            await emailService.SendTemplateEmailAsync(
                                "TeamMemberAdded",
                                user.Email,
                                userId ?? 0,
                                new Dictionary<string, string>
                                {
                                    ["CompanyName"] = "Your Company",
                                    ["Name"] = user.Username ?? "",
                                    ["Role"] = user.Role ?? "",
                                    ["Email"] = user.Email ?? "",
                                    ["LoginUrl"] = $"{baseUrl}/Account/Login",
                                    ["Year"] = DateTime.UtcNow.Year.ToString()
                                },
                                "Team");
                        }
                    }
                    catch { }
                });

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = true });
                }

                return RedirectToAction(nameof(Index));
            }

            return View(user);
        }

        [HttpPost]
        public async Task<IActionResult> EditUser(UserModel user)
        {
            var existingUser = await _context.Users.FirstOrDefaultAsync(x => x.UserId == user.UserId);

            if (existingUser == null)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = "User not found." });
                }

                return NotFound();
            }

            existingUser.Username = user.Username;
            existingUser.Email = user.Email;
            existingUser.Phone = user.Phone;
            existingUser.Role = user.Role;
            existingUser.IsActive = user.IsActive;

            if (!string.IsNullOrWhiteSpace(user.Password))
            {
                existingUser.Password = PasswordHelper.HashPassword(user.Password);
            }

            _context.Users.Update(existingUser);
            await _context.SaveChangesAsync();

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = true });
            }

            return RedirectToAction(nameof(Index));
        }
        [HttpPost]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var user = await _context.Users.FindAsync(id);

            if (user == null)
            {
                return Json(new { success = false, message = "User not found." });
            }

            user.IsActive = false;
            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }
        [HttpGet]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> GetPartner(int id)
        {
            var partner = await _context.ChannelPartners.FindAsync(id);
            if (partner == null)
                return Json(new { success = false, message = "Partner not found" });

            var subscription = await _context.PartnerSubscriptions
                .Where(s => s.ChannelPartnerId == id)
                .OrderByDescending(s => s.StartDate)
                .FirstOrDefaultAsync();

            // Look up plan separately (PartnerSubscriptionModel.Plan is [BsonIgnore])
            string planName = "";
            if (subscription != null)
            {
                var plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.PlanId == subscription.PlanId);
                planName = plan?.PlanName ?? "";
            }

            return Json(new
            {
                success = true,
                partnerId = partner.PartnerId,
                companyName = partner.CompanyName,
                contactPerson = partner.ContactPerson,
                email = partner.Email,
                phone = partner.Phone,
                address = partner.Address,
                commissionScheme = partner.CommissionScheme,
                planId = subscription?.PlanId,
                planName = subscription?.Plan?.PlanName,
                subscriptionStatus = subscription?.Status,
                endDate = subscription?.EndDate
            });
        }

        [HttpPost]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> UpdatePartner(int id, ChannelPartnerModel model, List<IFormFile> DocumentFiles)
        {
            try
            {
                var partner = await _context.ChannelPartners.FindAsync(id);
                if (partner == null)
                    return Json(new { success = false, message = "Partner not found" });

                partner.CompanyName = model.CompanyName;
                partner.ContactPerson = model.ContactPerson;
                partner.Email = model.Email;
                partner.Phone = model.Phone;
                partner.Address = model.Address;
                partner.CommissionScheme = model.CommissionScheme;
                partner.CommissionPercentage = model.CommissionPercentage;

                // Update subscription plan if changed
                if (model.SelectedPlanId.HasValue)
                {
                    var existingSub = await _context.PartnerSubscriptions
                        .Where(s => s.ChannelPartnerId == partner.PartnerId && (s.Status == "Active" || s.Status == "Trial"))
                        .OrderByDescending(s => s.StartDate)
                        .FirstOrDefaultAsync();

                    if (existingSub == null || existingSub.PlanId != model.SelectedPlanId.Value)
                    {
                        // Cancel existing subscription
                        if (existingSub != null)
                        {
                            existingSub.Status = "Cancelled";
                            existingSub.UpdatedOn = IndianTime.Now;
                            _context.PartnerSubscriptions.Update(existingSub);
                        }

                        var plan = await _context.SubscriptionPlans.FindAsync(model.SelectedPlanId.Value);
                        if (plan != null)
                        {
                            var (userId2, _, _) = GetCurrentUserContext();
                            var trialEnd = IndianTime.Now.AddDays(7);
                            _context.PartnerSubscriptions.Add(new PartnerSubscriptionModel
                            {
                                ChannelPartnerId = partner.PartnerId,
                                PlanId = plan.PlanId,
                                BillingCycle = "Trial",
                                Amount = 0,
                                StartDate = IndianTime.Now,
                                EndDate = trialEnd,
                                Status = "Trial",
                                CreatedOn = IndianTime.Now,
                                CreatedBy = userId2,
                                PaymentMethod = "Trial"
                            });
                        }
                    }
                }

                if (DocumentFiles != null && DocumentFiles.Count > 0)
                {
                    await SavePartnerDocumentsAsync(partner.PartnerId, DocumentFiles);
                    partner.Documents = "Uploaded";
                }

                _context.ChannelPartners.Update(partner);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Partner updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private async Task SavePartnerDocumentsAsync(int partnerId, List<IFormFile> files)
        {
            foreach (var file in files)
            {
                if (file.Length == 0) continue;
                byte[] fileContent;
                using (var ms = new MemoryStream())
                {
                    await file.CopyToAsync(ms);
                    fileContent = ms.ToArray();
                }
                var fileName = Path.GetFileName(file.FileName);
                _context.ChannelPartnerDocuments.Add(new ChannelPartnerDocumentModel
                {
                    ChannelPartnerId = partnerId,
                    PartnerId = partnerId,
                    FileName = fileName.Length > 255 ? fileName.Substring(0, 255) : fileName,
                    DocumentName = fileName,
                    DocumentType = "General",
                    DocumentTypeId = 1,
                    FilePath = fileName.Length > 500 ? fileName.Substring(0, 500) : fileName,
                    FileContent = fileContent,
                    FileSize = file.Length,
                    ContentType = (file.ContentType ?? "application/octet-stream").Length > 100 ? (file.ContentType ?? "application/octet-stream").Substring(0, 100) : (file.ContentType ?? "application/octet-stream"),
                    UploadedOn = IndianTime.Now,
                    VerificationStatus = "Pending",
                    DocumentStatus = "Pending"
                });
            }
            await _context.SaveChangesAsync();
        }

        [HttpPost]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> DeletePartner(int partnerId)
        {
            try
            {
                var partner = await _context.ChannelPartners.FindAsync(partnerId);
                if (partner == null)
                {
                    return Json(new { success = false, message = "Partner not found" });
                }

                // Instead of deleting, mark as inactive to preserve data integrity
                partner.Status = "Deleted";

                // Also deactivate associated user account
                if (partner.UserId.HasValue)
                {
                    var user = await _context.Users.FindAsync(partner.UserId.Value);
                    if (user != null)
                    {
                        user.IsActive = false;
                        _context.Users.Update(user);
                    }
                }

                _context.ChannelPartners.Update(partner);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Partner deleted successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error deleting partner: " + ex.Message });
            }
        }

        // ── Roles CRUD ──────────────────────────────────────────────

        //[HttpGet]
        //[RoleAuthorize("Admin")]
        //[Route("roles")]
        //public IActionResult Roles()
        //{

        //    var roles = _context.RolePermissions.OrderByDescending(r => r.CreatedAt).ToList();
        //    return View(roles);
        //}
        //[HttpGet]
        //[RoleAuthorize("Admin")]
        //public IActionResult Roles()
        //{
        //    var (userId, role, channelPartnerId) = GetCurrentUserContext();

        //    IQueryable<RolePermission> query = _context.RolePermissions;

        //    if (role?.ToLower() == "partner")
        //    {
        //        // Partner sees standard roles + only their own custom roles
        //        var standardRoles = new[] { "Admin", "Partner", "Agent" };
        //        query = query.Where(r => standardRoles.Contains(r.RoleName) || r.ChannelPartnerId == channelPartnerId);
        //    }
        //    else if (role?.ToLower() == "admin")
        //    {
        //        // Admin sees standard roles + only admin-created custom roles (ChannelPartnerId is null)
        //        var standardRoles = new[] { "Admin", "Partner", "Agent" };
        //        query = query.Where(r => standardRoles.Contains(r.RoleName) || r.ChannelPartnerId == null);
        //    }

        //    var roles = query.OrderByDescending(r => r.CreatedAt).ToList();
        //    return View(roles);
        //}
        [HttpGet]
        [RoleAuthorize("Admin")]
        public IActionResult Roles()
        {
            var (userId, role, channelPartnerId) = GetCurrentUserContext();

            IQueryable<RolePermission> query = _context.RolePermissions.AsQueryable();

            var tenantIdClaim = User.FindFirst("TenantId")?.Value;
            bool hasPartnerFeature = false;

            if (!string.IsNullOrEmpty(tenantIdClaim) && int.TryParse(tenantIdClaim, out int tid))
            {
                var activeSub = _masterDb.TenantSubscriptions
                    .Where(s => s.TenantId == tid && (s.Status == "Active" || s.Status == "Trial"))
                    .OrderByDescending(s => s.StartDate)
                    .FirstOrDefault();

                if (activeSub != null)
                {
                    var activePlan = _masterDb.SaasPlans.FirstOrDefault(p => p.PlanId == activeSub.PlanId);
                    hasPartnerFeature = activePlan?.MaxPartners > 0;
                }
            }

            var roleLower = role?.ToLower();

            if (roleLower == "partner")
            {
                var standardRoles = new[] { "Admin", "Partner", "Agent" };

                query = query.Where(r =>
                    standardRoles.Contains(r.RoleName) ||
                    r.ChannelPartnerId == channelPartnerId
                );
            }
            else if (roleLower == "admin")
            {
                if (hasPartnerFeature)
                {
                    var standardRoles = new[] { "Admin", "Partner", "Agent" };

                    query = query.Where(r =>
                        standardRoles.Contains(r.RoleName) ||
                        r.ChannelPartnerId == null
                    );
                }
                else
                {
                    var standardRoles = new[] { "Admin", "Agent" };

                    query = query.Where(r =>
                        standardRoles.Contains(r.RoleName) ||
                        (r.ChannelPartnerId == null && r.RoleName != "Partner")
                    );
                }
            }

            var roles = query
                .OrderByDescending(r => r.CreatedAt)
                .ToList();

            return View(roles);
        }
        [HttpPost]
        [RoleAuthorize("Admin")]
        public IActionResult AddRoles(RolePermission model)
        {
            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
            try
            {
                if (string.IsNullOrWhiteSpace(model.RoleName))
                    return isAjax ? Json(new { success = false, message = "Role name is required" }) : (IActionResult)BadRequest();

                var (userId, role, channelPartnerId) = GetCurrentUserContext();

                if (model.Id > 0)
                {
                    var existing = _context.RolePermissions.Find(model.Id);
                    if (existing == null)
                        return isAjax ? Json(new { success = false, message = "Role not found" }) : (IActionResult)NotFound();
                    existing.RoleName = model.RoleName;
                    existing.AllowedModules = model.AllowedModules;
                }
                else
                {
                    if (_context.RolePermissions.Any(r => r.RoleName == model.RoleName))
                        return isAjax ? Json(new { success = false, message = "Role already exists" }) : (IActionResult)BadRequest();
                    model.CreatedAt = IndianTime.Now;
                    model.ChannelPartnerId = channelPartnerId;
                    _context.RolePermissions.Add(model);
                }

                _context.SaveChanges();
                if (isAjax) return Json(new { success = true });
                TempData["Success"] = "Role saved successfully!";
                return RedirectToAction("Roles");
            }
            catch (Exception ex)
            {
                if (isAjax) return Json(new { success = false, message = ex.Message });
                TempData["Error"] = ex.Message;
                return RedirectToAction("Roles");
            }
        }

        [HttpGet]
        [RoleAuthorize("Admin")]
        public IActionResult GetRole(int id)
        {
            var role = _context.RolePermissions.Find(id);
            if (role == null) return Json(new { success = false, message = "Role not found" });
            return Json(new { success = true, id = role.Id, roleName = role.RoleName, allowedModules = role.AllowedModules });
        }

        [HttpGet]
        [RoleAuthorize("Admin")]
        public IActionResult DeleteRoles(int id)
        {
            var role = _context.RolePermissions.Find(id);
            if (role != null)
            {
                _context.RolePermissions.Remove(role);
                _context.SaveChanges();
                TempData["Success"] = "Role deleted successfully!";
            }
            return RedirectToAction("Roles");
        }

        // ── Permission Management ───────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> RolePermissions(string roleName)
        {
            if (string.IsNullOrEmpty(roleName))
                return BadRequest("Role name is required");

            // Custom roles with AllowedModules don't need page-level permissions
            var standardRoles = new[] { "Admin", "Partner", "Agent", "Sales" };
            if (!standardRoles.Contains(roleName, StringComparer.OrdinalIgnoreCase))
            {
                var roleRecord = _context.RolePermissions.FirstOrDefault(r => r.RoleName == roleName);
                if (roleRecord != null && !string.IsNullOrEmpty(roleRecord.AllowedModules))
                {
                    TempData["Info"] = "Custom roles use module-based access. Edit the role to change allowed modules.";
                    return RedirectToAction("Roles");
                }
            }

            var (userId, role, channelPartnerId) = GetCurrentUserContext();

            var modules = await _permissionService.GetModulesWithPagesAsync();
            var permissions = await _permissionService.GetPermissionsAsync();

            // Get existing role permissions with partner context
            var rolePermissions = new Dictionary<int, Dictionary<string, bool>>();
            foreach (var module in modules)
            {
                foreach (var page in module.Pages)
                {
                    var pagePermissions = await _permissionService.GetRolePermissionsAsync(roleName, page.PageId, channelPartnerId);
                    rolePermissions[page.PageId] = pagePermissions;
                }
            }

            ViewBag.RoleName = roleName;
            ViewBag.Modules = modules;
            ViewBag.Permissions = permissions;
            ViewBag.RolePermissions = rolePermissions;
            ViewBag.ChannelPartnerId = channelPartnerId;
            ViewBag.IsPartner = role?.ToLower() == "partner";

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> SaveRolePermissions()
        {
            try
            {
                var roleName = Request.Form["roleName"].ToString();
                if (string.IsNullOrEmpty(roleName))
                {
                    return Json(new { success = false, message = "Role name is required" });
                }

                // Parse permissions from form data
                var permissions = new Dictionary<int, Dictionary<int, bool>>();
                var debugInfo = new List<string>();

                foreach (var key in Request.Form.Keys)
                {
                    debugInfo.Add($"Key: {key}, Value: {Request.Form[key]}");

                    if (key.StartsWith("permissions["))
                    {
                        // Extract pageId and permissionId from key like "permissions[1][2]"
                        var matches = System.Text.RegularExpressions.Regex.Match(key, @"permissions\[(\d+)\]\[(\d+)\]");
                        if (matches.Success)
                        {
                            var pageId = int.Parse(matches.Groups[1].Value);
                            var permissionId = int.Parse(matches.Groups[2].Value);
                            var isGranted = Request.Form[key].ToString().ToLower() == "true";

                            if (!permissions.ContainsKey(pageId))
                                permissions[pageId] = new Dictionary<int, bool>();

                            permissions[pageId][permissionId] = isGranted;
                        }
                    }
                }

                // Debug: Log what we parsed
                var permissionCount = permissions.Sum(p => p.Value.Count);

                if (permissionCount == 0)
                {
                    return Json(new { success = false, message = $"No permissions parsed. Debug: {string.Join(", ", debugInfo)}" });
                }

                var currentUser = GetCurrentUserContext();
                await _permissionService.SaveRolePermissionsAsync(roleName, permissions, currentUser.UserId?.ToString() ?? "System", currentUser.ChannelPartnerId);

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Json(new { success = true, message = $"Permissions saved successfully! Processed {permissionCount} permissions." });

                TempData["Success"] = "Permissions saved successfully!";
                return RedirectToAction("Roles");
            }
            catch (Exception ex)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Json(new { success = false, message = ex.Message });

                TempData["Error"] = "Failed to save permissions: " + ex.Message;
                return RedirectToAction("Roles");
            }
        }

        [HttpGet]

        [Route("partnerdetails/{id}")]
        public IActionResult PartnerDetails(string id)
        {
            var decodedId = IdObfuscator.Decode(id);
            if (decodedId == null)
            {
                return NotFound();
            }

            ViewBag.EncodedId = id;
            var (userId, role, channelPartnerId) = GetCurrentUserContext();

            // If id is 0, it's a direct admin agent viewing their own details
            if (decodedId.Value == 0)
            {
                return RedirectToAction("Index", "Profile");
            }

            // Admin can view any partner, Partner/Agent can only view their own
            if (role?.ToLower() != "admin" && channelPartnerId != decodedId.Value)
            {
                return RedirectToAction("AccessDenied", "Home");
            }

            var partner = _context.ChannelPartners
                .Where(p => p.Status != "Deleted")
                .Select(p => new ChannelPartnerModel
                {
                    PartnerId = p.PartnerId,
                    CompanyName = p.CompanyName ?? "",
                    ContactPerson = p.ContactPerson ?? "",
                    Email = p.Email ?? "",
                    Phone = p.Phone ?? "",
                    Address = p.Address ?? "",
                    CommissionScheme = p.CommissionScheme ?? "",
                    Documents = p.Documents ?? "",
                    Status = p.Status ?? "Pending",
                    CreatedOn = p.CreatedOn,
                    ApprovedBy = p.ApprovedBy,
                    ApprovedOn = p.ApprovedOn,
                    UserId = p.UserId,
                    CommissionPercentage = p.CommissionPercentage,
                    SubscriptionPlan = p.SubscriptionPlan ?? "Basic"
                })
                .FirstOrDefault(p => p.PartnerId == decodedId.Value);

            if (partner == null)
            {
                return NotFound();
            }

            ViewBag.Documents = _context.ChannelPartnerDocuments
                .Where(d => d.ChannelPartnerId == decodedId.Value)
                .OrderByDescending(d => d.UploadedOn)
                .ToList();

            // Get lead statistics for this partner
            var totalLeads = _context.Leads.Count(l => l.ChannelPartnerId == decodedId.Value);
            var processingLeads = _context.Leads.Count(l => l.ChannelPartnerId == decodedId.Value && l.Status == "Processing");
            var convertedLeads = _context.Leads.Count(l => l.ChannelPartnerId == decodedId.Value && l.Status == "Converted");
            var closedLeads = _context.Leads.Count(l => l.ChannelPartnerId == decodedId.Value && l.Status == "Closed");

            ViewBag.TotalLeads = totalLeads;
            ViewBag.ProcessingLeads = processingLeads;
            ViewBag.ConvertedLeads = convertedLeads;
            ViewBag.ClosedLeads = closedLeads;
            ViewBag.IsAdmin = role?.ToLower() == "admin";

            return View(partner);
        }
        [HttpGet]
        [RoleAuthorize("Admin")]

        //[Route("partnerdocuments/downloadall/{partnerId}")]
        public async Task<IActionResult> DownloadAllDocuments(string partnerId)
        {
            var decodedId = IdObfuscator.Decode(partnerId);
            if (decodedId == null)
            {
                return NotFound();
            }

            ViewBag.EncodedId = partnerId;
            var documents = await _context.ChannelPartnerDocuments.Where(d => d.ChannelPartnerId == decodedId.Value).ToListAsync();
            if (documents == null || !documents.Any())
            {
                return NotFound();
            }
            using (var memoryStream = new MemoryStream())
            {
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
                {
                    foreach (var doc in documents)
                    {
                        var entry = archive.CreateEntry(doc.FileName);
                        using (var entryStream = entry.Open())
                        {
                            await entryStream.WriteAsync(doc.FileContent, 0, doc.FileContent.Length);
                        }
                    }
                }
                return File(memoryStream.ToArray(), "application/zip", $"partner_{partnerId}_Documents.zip");
            }
        }

        [HttpGet]
        [RoleAuthorize("Admin")]
        public IActionResult DownloadPartnerDocument(int documentId)
        {
            var document = _context.ChannelPartnerDocuments.Find(documentId);
            if (document == null)
            {
                return NotFound();
            }

            return File(document.FileContent, document.ContentType, document.FileName);
        }

        [HttpPost]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> UploadPartnerDocument(int partnerId, string documentName, string documentType, IFormFile documentFile)
        {
            if (documentFile == null || documentFile.Length == 0)
            {
                return BadRequest("No file uploaded");
            }

            var partner = await _context.ChannelPartners.FindAsync(partnerId);
            if (partner == null)
            {
                return NotFound("Partner not found");
            }

            // Check storage limits for partner
            var (canUpload, storageMessage, currentUsageGB, limitGB) = await _subscriptionService.CanUploadFileAsync(partnerId, documentFile.Length);
            if (!canUpload)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    // Get available plans for upgrade options
                    var availablePlans = await _subscriptionService.GetAvailablePlansAsync();
                    return Json(new
                    {
                        success = false,
                        storageLimitReached = true,
                        message = storageMessage,
                        currentUsageGB = currentUsageGB,
                        limitGB = limitGB,
                        availablePlans = availablePlans.Select(p => new
                        {
                            planId = p.PlanId,
                            planName = p.PlanName,
                            monthlyPrice = p.MonthlyPrice,
                            yearlyPrice = p.YearlyPrice,
                            maxStorageGB = p.MaxStorageGB
                        }).ToList()
                    });
                }

                return BadRequest(storageMessage);
            }

            // Read file into byte array
            byte[] fileContent;
            using (var memoryStream = new MemoryStream())
            {
                await documentFile.CopyToAsync(memoryStream);
                fileContent = memoryStream.ToArray();
            }

            var document = new ChannelPartnerDocumentModel
            {
                ChannelPartnerId = partnerId,
                PartnerId = partnerId,
                FileName = Path.GetFileName(documentFile.FileName).Length > 255 ? Path.GetFileName(documentFile.FileName).Substring(0, 255) : Path.GetFileName(documentFile.FileName),
                DocumentName = documentName ?? Path.GetFileName(documentFile.FileName),
                DocumentType = documentType ?? "General",
                DocumentTypeId = 1,
                FilePath = Path.GetFileName(documentFile.FileName).Length > 500 ? Path.GetFileName(documentFile.FileName).Substring(0, 500) : Path.GetFileName(documentFile.FileName),
                FileContent = fileContent,
                FileSize = documentFile.Length,
                ContentType = (documentFile.ContentType ?? "application/octet-stream").Length > 100 ? (documentFile.ContentType ?? "application/octet-stream").Substring(0, 100) : (documentFile.ContentType ?? "application/octet-stream"),
                UploadedOn = IndianTime.Now,
                VerificationStatus = "Pending",
                DocumentStatus = "Pending"
            };

            _context.ChannelPartnerDocuments.Add(document);
            await _context.SaveChangesAsync();

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Ok(new { success = true, message = "Document uploaded successfully" });
            }

            return RedirectToAction("PartnerDetails", new { id = partnerId });
        }

        // P0-D3: Partner Document Verification Endpoints
        [HttpPost]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> ApprovePartnerDocument(int documentId)
        {
            try
            {
                var (userId, role, channelPartnerId) = GetCurrentUserContext();
                if (!userId.HasValue)
                {
                    return Json(new { success = false, message = "User not authenticated" });
                }

                var document = await _context.ChannelPartnerDocuments.FindAsync(documentId);
                if (document == null)
                {
                    return Json(new { success = false, message = "Document not found" });
                }

                document.VerificationStatus = "Approved";
                document.VerifiedBy = userId.Value;
                document.VerifiedOn = IndianTime.Now;
                document.RejectionReason = null;

                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Document approved successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost]
        [RoleAuthorize("Admin")]
        public async Task<IActionResult> RejectPartnerDocument(int documentId, string reason)
        {
            try
            {
                var (userId, role, channelPartnerId) = GetCurrentUserContext();
                if (!userId.HasValue)
                {
                    return Json(new { success = false, message = "User not authenticated" });
                }

                if (string.IsNullOrWhiteSpace(reason))
                {
                    return Json(new { success = false, message = "Rejection reason is required" });
                }

                var document = await _context.ChannelPartnerDocuments.FindAsync(documentId);
                if (document == null)
                {
                    return Json(new { success = false, message = "Document not found" });
                }

                document.VerificationStatus = "Rejected";
                document.VerifiedBy = userId.Value;
                document.VerifiedOn = IndianTime.Now;
                document.RejectionReason = reason;

                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Document rejected successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Sends welcome email with login credentials to the partner
        /// </summary>
        /// 


        private async Task<bool> SendPartnerWelcomeEmailAsync(string email, string contactPerson, string username, string password, string planName, DateTime trialEndDate)
        {
            try
            {
                var httpContext = _httpContextAccessor.HttpContext;
                if (httpContext == null) return false;

                var scopeFactory = httpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();
                using var scope = scopeFactory.CreateScope();
                var emailService = scope.ServiceProvider.GetRequiredService<EmailService>();
                // Scope the company name to the current tenant so the welcome email carries the right company
                var actingTenantId = httpContext.Items["TenantId"] as int? ?? 0;
                var companyName = actingTenantId > 0
                    ? (_context.Settings.FirstOrDefault(s => s.SettingKey == "CompanyName" && s.ChannelPartnerId == null && s.TenantId == actingTenantId)?.SettingValue
                        ?? _context.Settings.FirstOrDefault(s => s.SettingKey == "CompanyName" && s.ChannelPartnerId == null)?.SettingValue
                        ?? "Real Estate CRM")
                    : (_context.Settings.FirstOrDefault(s => s.SettingKey == "CompanyName" && s.ChannelPartnerId == null)?.SettingValue ?? "Real Estate CRM");

                var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";

                return await emailService.SendTemplateEmailAsync(
                    "PartnerWelcome",
                    email,
                    0,
                    new Dictionary<string, string>
                    {
                        ["CompanyName"] = companyName,
                        ["Name"] = contactPerson ?? "",
                        ["Username"] = username ?? "",
                        ["Password"] = password ?? "",
                        ["PlanName"] = planName ?? "",
                        ["TrialEndDate"] = trialEndDate.ToString("MMM dd, yyyy"),
                        ["LoginUrl"] = $"{baseUrl}/Account/Login",
                        ["Year"] = IndianTime.Now.Year.ToString()
                    },
                    "Partner");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send welcome email: {ex.Message}");
                return false;
            }
        }

        private async Task<string> GenarateSubdomainAsync(string companyName)
        {
            if (string.IsNullOrWhiteSpace(companyName))
                return null;
            var subdomain = System.Text.RegularExpressions.Regex.Replace(companyName.ToLower(), @"[^a-z0-9]", "");
            if (string.IsNullOrEmpty(subdomain))
                return null;
            var existing = await _context.ChannelPartners.AnyAsync(p => p.Subdomain == subdomain && p.Status != "Deleted");
            if (existing)
            {
                var counter = 1;
                while (await _context.ChannelPartners.AnyAsync(p => p.Subdomain == $"{subdomain}{counter}" && p.Status != "Deleted"))
                {
                    counter++;
                }
                subdomain = subdomain + counter;
            }
            return subdomain;
        }
    }
}