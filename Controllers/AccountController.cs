using CRM.Helpers;
using CRM.MasterDb;
using CRM.Models;
using CRM.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;

namespace CRM.Controllers
{
    public class AccountController : Controller
    {
        private readonly AppDbContext _db;
        private readonly MasterDbContext _masterDb;
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly Services.EmailService _emailService;
        private readonly Services.INotificationService _notificationService;
        private readonly Services.FcmService _fcmService;
        private readonly ILogger<AccountController> _logger;
        private readonly IWebHostEnvironment _env;

        public AccountController(AppDbContext db, MasterDbContext masterDb, IConfiguration config, IHttpContextAccessor httpContextAccessor, Services.EmailService emailService, Services.INotificationService notificationService, Services.FcmService fcmService, ILogger<AccountController> logger, IWebHostEnvironment env)
        {
            _db = db;
            _masterDb = masterDb;
            _config = config;
            _httpContextAccessor = httpContextAccessor;
            _emailService = emailService;
            _notificationService = notificationService;
            _fcmService = fcmService;
            _logger = logger;
            _env = env;
        }


        public IActionResult Register()
        {
            bool adminExists = _db.Users.Any(u => u.Role == "Admin");

            // Check if tenant's plan has Partner feature
            bool hasPartnerFeature = false;
            var tenantIdClaim = User.FindFirst("TenantId")?.Value;
            if (!string.IsNullOrEmpty(tenantIdClaim) && int.TryParse(tenantIdClaim, out int tid))
            {
                var activeSub = _masterDb.TenantSubscriptions
                    .Where(s => s.TenantId == tid && (s.Status == "Active" || s.Status == "Trial"))
                    .OrderByDescending(s => s.StartDate)
                    .FirstOrDefault();

                if (activeSub != null)
                {
                    var activePlan = _masterDb.SaasPlans.FirstOrDefault(p => p.PlanId == activeSub.PlanId);
                    hasPartnerFeature = activePlan != null && activePlan.MaxPartners > 0;
                }
            }

            if (adminExists)
            {
                var rolesQuery = _db.RolePermissions
                    .Where(r => r.RoleName == "Agent" || r.RoleName == "Partner")
                    .Select(r => r.RoleName)
                    .Distinct();
                ViewBag.Roles = hasPartnerFeature
                    ? rolesQuery.ToList()
                    : rolesQuery.Where(r => r != "Partner").ToList();
            }
            else
            {
                ViewBag.Roles = new List<string> { "Admin" };
            }

            var subdomainPId = HttpContext.Items["SubdomainPartnerId"] as int?;
            ViewBag.CompanyLogo = BrandingResolver.ResolveCompanyLogo(_db, subdomainPId);
            ViewBag.CompanyName = BrandingResolver.ResolveCompanyName(_db, subdomainPId, HttpContext.Items["SubdomainPartnerName"] as string);

            return View();
        }


        [HttpPost]
        public async Task<IActionResult> Register([FromForm] RegisterModel model)
        {
            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
            bool adminExists = _db.Users.Any(u => u.Role == "Admin");

            // Helper to repopulate ViewBag on validation failure
            void SetViewBag()
            {
                ViewBag.Roles = adminExists
                    ? _db.RolePermissions.Where(r => r.RoleName != "Admin").Select(r => r.RoleName).Distinct().ToList()
                    : new List<string> { "Admin" };
                var b = _db.Branding.AsNoTracking().FirstOrDefault();
                ViewBag.CompanyLogo = b?.CompanyLogo;
            }

            // Validation helper - returns JSON for AJAX, View for normal
            IActionResult ValidationError(string message)
            {
                if (isAjax) return Json(new { success = false, message });
                ViewBag.Message = message;
                SetViewBag();
                return View();
            }

            try
            {
                // Fallback: manually bind from form if model binding fails
                if (model == null)
                {
                    model = new RegisterModel
                    {
                        Username = Request.Form["Username"],
                        Email = Request.Form["Email"],
                        Phone = Request.Form["Phone"],
                        Password = Request.Form["Password"],
                        Role = Request.Form["Role"]
                    };
                    // Bind file uploads
                    foreach (var file in Request.Form.Files)
                    {
                        switch (file.Name)
                        {
                            case "AgentAadhar": model.AgentAadhar = file; break;
                            case "AgentPAN": model.AgentPAN = file; break;
                            case "AgentResume": model.AgentResume = file; break;
                            case "AgentExperienceLetter": model.AgentExperienceLetter = file; break;
                            case "PartnerBusinessReg": model.PartnerBusinessReg = file; break;
                            case "PartnerTaxCert": model.PartnerTaxCert = file; break;
                            case "PartnerIDProof": model.PartnerIDProof = file; break;
                            case "PartnerAadhar": model.PartnerAadhar = file; break;
                            case "PartnerPAN": model.PartnerPAN = file; break;
                            case "PartnerResume": model.PartnerResume = file; break;
                            case "PartnerExperienceLetter": model.PartnerExperienceLetter = file; break;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(model.Username))
                    return ValidationError("Username is required.");

                if (string.IsNullOrWhiteSpace(model.Email))
                    return ValidationError("Email is required.");

                if (string.IsNullOrWhiteSpace(model.Phone))
                    return ValidationError("Phone is required.");

                if (string.IsNullOrWhiteSpace(model.Password))
                    return ValidationError("Password is required.");

                if (string.IsNullOrWhiteSpace(model.Role))
                    return ValidationError("Please select a role.");

                if (adminExists && model.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                    return ValidationError("Admin user already exists. You cannot create another Admin.");

                if (_db.Users.Any(u => u.Username == model.Username))
                    return ValidationError("Username already exists!");

                if (_db.Users.Any(u => u.Email == model.Email))
                    return ValidationError("Email already exists!");

                bool isAgent = model.Role.Equals("Agent", StringComparison.OrdinalIgnoreCase) || model.Role.Equals("Sales", StringComparison.OrdinalIgnoreCase);
                bool isPartner = model.Role.Equals("Partner", StringComparison.OrdinalIgnoreCase);

                //if (isAgent && (model.AgentAadhar == null || model.AgentPAN == null || model.AgentResume == null))
                //    return ValidationError("Aadhar, PAN and Resume are required for Agent registration.");

                //if (isPartner && (model.PartnerBusinessReg == null || model.PartnerTaxCert == null || model.PartnerIDProof == null
                //    || model.PartnerAadhar == null || model.PartnerPAN == null))
                //    return ValidationError("Business Registration, Tax Certificate, ID Proof, Aadhar, PAN and Resume are required for Partner registration.");

                var isFirstUser = !_db.Users.Any();
                bool requiresApproval = isAgent || isPartner;

                var newUser = new UserModel
                {
                    Username = model.Username,
                    Email = model.Email,
                    Phone = model.Phone,
                    // Password = model.Password, // Plain text - uncomment this and comment below to disable hashing
                    Password = PasswordHelper.HashPassword(model.Password), // Hashed password
                    Role = model.Role,
                    IsActive = !requiresApproval,
                    CreatedDate = IndianTime.Now
                };

                _db.Users.Add(newUser);
                await _db.SaveChangesAsync();

                if (isAgent)
                {
                    var agent = new AgentModel
                    {
                        FullName = model.Username,
                        Email = model.Email,
                        Phone = model.Phone ?? "",
                        AgentType = "Commission",
                        CommissionRules = "0.0% of sale",
                        Status = "Pending",
                        CreatedOn = IndianTime.Now
                    };
                    _db.Agents.Add(agent);
                    await _db.SaveChangesAsync();

                    //var agentDocs = new List<(IFormFile file, string type)> {
                    //    (model.AgentAadhar!, "Aadhar"),
                    //    (model.AgentPAN!, "PAN"),
                    //    (model.AgentResume!, "Resume")
                    //};
                    //if (model.AgentExperienceLetter != null)
                    //    agentDocs.Add((model.AgentExperienceLetter, "Experience Letter"));
                    var agentDocs = new List<(IFormFile file, string type)>();
                    if (model.AgentAadhar != null) agentDocs.Add((model.AgentAadhar, "Aadhar"));
                    if (model.AgentPAN != null) agentDocs.Add((model.AgentPAN, "PAN"));
                    if (model.AgentResume != null) agentDocs.Add((model.AgentResume, "Resume"));
                    if (model.AgentExperienceLetter != null) agentDocs.Add((model.AgentExperienceLetter, "Experience Letter"));

                    foreach (var (file, type) in agentDocs)
                    {
                        using var ms = new MemoryStream();
                        await file.CopyToAsync(ms);
                        _db.AgentDocuments.Add(new AgentDocumentModel
                        {
                            AgentId = agent.AgentId,
                            FileName = Path.GetFileName(file.FileName),
                            DocumentName = type,
                            DocumentType = type,
                            FileContent = ms.ToArray(),
                            FileSize = file.Length,
                            ContentType = file.ContentType ?? "application/octet-stream",
                            UploadedOn = IndianTime.Now,
                            VerificationStatus = "Pending"
                        });
                    }
                    await _db.SaveChangesAsync();
                }
                else if (isPartner)
                {
                    var partner = new ChannelPartnerModel
                    {
                        CompanyName = !string.IsNullOrEmpty(model.CompanyName) ? model.CompanyName : model.Username,
                        ContactPerson = model.Username,
                        Email = model.Email,
                        Phone = model.Phone ?? "",
                        CommissionScheme = "0% of sale",
                        Status = "Pending",
                        CreatedOn = IndianTime.Now
                    };
                    _db.ChannelPartners.Add(partner);
                    await _db.SaveChangesAsync();

                    // Generate subdomain
                    var subdomain = System.Text.RegularExpressions.Regex.Replace((partner.CompanyName ?? "").ToLower().Trim(), "[^a-z0-9]", "");

                    if (string.IsNullOrEmpty(subdomain))
                    {


                        // Check duplicate subdomain
                        if (await _db.ChannelPartners.AnyAsync(p => p.Subdomain == subdomain && p.PartnerId != partner.PartnerId && p.Status != "Deleted"))
                        {
                            var cnt = 1;
                            var originalSubdomain = subdomain;

                            while (await _db.ChannelPartners.AnyAsync(p =>
                                   p.Subdomain == subdomain + cnt &&
                                   p.Status != "Deleted"))
                                cnt++;
                            subdomain += cnt;
                        }
                        partner.Subdomain = subdomain;
                        await _db.SaveChangesAsync();
                    }

                    // Insert CompanyName into Settings table
                    _db.Settings.Add(new SettingsModel
                    {
                        SettingKey = "CompanyName",
                        SettingValue = partner.CompanyName,
                        ChannelPartnerId = partner.PartnerId,
                        ModifiedOn = IndianTime.Now
                    });

                    await _db.SaveChangesAsync();

                    //var partnerDocs = new List<(IFormFile file, string type)> {
                    //    (model.PartnerBusinessReg!, "Business Registration"),
                    //    (model.PartnerTaxCert!, "Tax Certificate"),
                    //    (model.PartnerIDProof!, "ID Proof"),
                    //    (model.PartnerAadhar!, "Aadhar"),
                    //    (model.PartnerPAN!, "PAN"),

                    //};
                    //if (model.PartnerResume != null)
                    //    partnerDocs.Add((model.PartnerResume, "Resume"));
                    //if (model.PartnerExperienceLetter != null)
                    //    partnerDocs.Add((model.PartnerExperienceLetter, "Experience Letter"));
                    var partnerDocs = new List<(IFormFile file, string type)>();
                    if (model.PartnerBusinessReg != null) partnerDocs.Add((model.PartnerBusinessReg, "Business Registration"));
                    if (model.PartnerTaxCert != null) partnerDocs.Add((model.PartnerTaxCert, "Tax Certificate"));
                    if (model.PartnerIDProof != null) partnerDocs.Add((model.PartnerIDProof, "ID Proof"));
                    if (model.PartnerAadhar != null) partnerDocs.Add((model.PartnerAadhar, "Aadhar"));
                    if (model.PartnerPAN != null) partnerDocs.Add((model.PartnerPAN, "PAN"));
                    if (model.PartnerResume != null) partnerDocs.Add((model.PartnerResume, "Resume"));
                    if (model.PartnerExperienceLetter != null) partnerDocs.Add((model.PartnerExperienceLetter, "Experience Letter"));

                    foreach (var (file, type) in partnerDocs)
                    {
                        using var ms = new MemoryStream();
                        await file.CopyToAsync(ms);
                        _db.ChannelPartnerDocuments.Add(new ChannelPartnerDocumentModel
                        {
                            ChannelPartnerId = partner.PartnerId,
                            PartnerId = partner.PartnerId,
                            FileName = Path.GetFileName(file.FileName),
                            DocumentName = type,
                            DocumentType = type,
                            DocumentTypeId = 1,
                            FilePath = Path.GetFileName(file.FileName),
                            FileContent = ms.ToArray(),
                            FileSize = file.Length,
                            ContentType = file.ContentType ?? "application/octet-stream",
                            UploadedOn = IndianTime.Now,
                            VerificationStatus = "Pending",
                            DocumentStatus = "Pending"
                        });
                    }
                    await _db.SaveChangesAsync();

                    // Link user and partner together
                    newUser.ChannelPartnerId = partner.PartnerId;
                    partner.UserId = newUser.UserId;
                    await _db.SaveChangesAsync();
                }

                if (isFirstUser)
                {
                    // Seeding is disabled - no longer auto-seeds on startup
                }
                // Notify all admins about new pending registration
                if (requiresApproval)
                {
                    var roleType = isAgent ? "Agent" : "Partner";
                    var approvalLink = isAgent ? "/List" : "/ManageUsers/PartnerApproval";
                    var adminUsers = await _db.Users.Where(u => u.Role == "Admin" && u.IsActive).ToListAsync();

                    foreach (var admin in adminUsers)
                    {
                        await _notificationService.CreateNotificationAsync(
                            $"New {roleType} Registration",
                            $"{model.Username} has registered as {roleType} and is pending approval.",
                            "Approval", admin.UserId, approvalLink, null, roleType, "High");

                        try
                        {
                            await _fcmService.SendNotificationToUser(
                                admin.UserId,
                                $"New {roleType} Registration",
                                $"{roleType} Registration Pending, {model.Username} is pending approval.",
                                approvalLink,
                                "Approval");
                        }
                        catch
                        {
                        }
                    }
                }
                if (isAjax)
                    return Json(new { success = true, requiresApproval, redirect = Url.Action("Login") });

                if (requiresApproval)
                    TempData["RegistrationMessage"] = "Registration successful! Your account is pending approval.";

                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                if (isAjax)
                    return Json(new { success = false, message = "Registration failed: " + ex.Message });
                return ValidationError("Registration failed: " + ex.Message);
            }
        }

        [HttpGet]

        public async Task<IActionResult> Login()
        {
            // Preserve TempData before sign-out (sign-out clears session which wipes TempData)
            var pendingApproval = TempData["PendingApproval"];
            var registrationMsg = TempData["RegistrationMessage"];
            var passwordReset = TempData["PasswordResetSuccess"];

            // NOTE: No sign-out here. Landing on the login page (e.g. via a
            // transient redirect) must NOT destroy the user's session - doing
            // so cascaded every [Authorize] page into login redirects.

            if (!_db.Users.Any())
            {
                return RedirectToAction(nameof(Register));
            }

            // Restore TempData after sign-out
            if (pendingApproval != null) TempData["PendingApproval"] = pendingApproval;
            if (registrationMsg != null) TempData["RegistrationMessage"] = registrationMsg;
            if (passwordReset != null) TempData["PasswordResetSuccess"] = passwordReset;

            var subdomainPartnerId = HttpContext.Items["SubdomainPartnerId"] as int?;
            ViewBag.CompanyLogo = BrandingResolver.ResolveCompanyLogo(_db, subdomainPartnerId);
            ViewBag.CompanyName = BrandingResolver.ResolveCompanyName(_db, subdomainPartnerId, HttpContext.Items["SubdomainPartnerName"] as string);
            ViewBag.SubdomainPartnerId = subdomainPartnerId;
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> Login(LoginModel model)
        {
            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

            if (!ModelState.IsValid)
            {
                if (isAjax) return Json(new { success = false, message = "Please fill in all required fields" });
                ViewBag.Message = "Please fill in all required fields";
                return View();
            }

            // ========================================================
            // STEP 0: Check if this is a SuperAdmin login
            // ========================================================
            var superAdmin = await _masterDb.SuperAdmins
                .FirstOrDefaultAsync(s => s.Email == model.Username && s.IsActive);

            if (superAdmin != null && PasswordHelper.VerifyPassword(model.Password, superAdmin.PasswordHash))
            {
                superAdmin.LastLoginOn = DateTime.UtcNow;
                await _masterDb.SaveChangesAsync();

                var saToken = GenerateSuperAdminToken(superAdmin);

                Response.Cookies.Append("jwtToken", saToken, new CookieOptions
                {
                    HttpOnly = false,
                    Secure = true,
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    Expires = DateTimeOffset.UtcNow.AddHours(8)
                });

                var saClaims = new List<Claim>
                {
                    new Claim("UserId", superAdmin.Id.ToString()),
                    new Claim("role", superAdmin.Role.ToString()),
                    new Claim(ClaimTypes.Name, superAdmin.FullName),
                    new Claim(ClaimTypes.Email, superAdmin.Email),
                    new Claim(ClaimTypes.Role, "SuperAdmin"),
                    new Claim("IsSuperAdmin", "true")
                };

                var saIdentity = new ClaimsIdentity(saClaims, CookieAuthenticationDefaults.AuthenticationScheme);

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(saIdentity),
                    new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
                    });

                if (isAjax)
                    return Json(new { success = true, redirect = "/SuperAdmin/Dashboard" });

                return Redirect("/SuperAdmin/Dashboard");
            }

            // ========================================================
            // STEP 1: Tenant resolved via subdomain -> check only that tenant DB
            // ========================================================
            var tenantService = HttpContext.RequestServices.GetRequiredService<ITenantService>();

            if (tenantService.IsResolved())
            {
                // Check if tenant is suspended
                var currentTenant = await _masterDb.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantService.GetTenantId());
                if (currentTenant != null && (currentTenant.IsSuspended || !currentTenant.IsActive))
                {
                    var msg = !string.IsNullOrEmpty(currentTenant.SuspendedReason)
                        ? $"This account has been suspended. Reason: {currentTenant.SuspendedReason}"
                        : "This account has been suspended. Please contact support.";
                    if (isAjax) return Json(new { success = false, message = msg });
                    ViewBag.Message = msg;
                    return View();
                }
                // Subdomain login - check only this tenant's DB
                var user = _db.Users.ToList().FirstOrDefault(u =>
                    (u.Username.Equals(model.Username, StringComparison.Ordinal) ||
                     u.Email.Equals(model.Username, StringComparison.OrdinalIgnoreCase))
                    && PasswordHelper.VerifyPassword(model.Password, u.Password));

                if (user == null)
                {
                    // Audit log for failed subdomain login
                    _db.AuditLogs.Add(new AuditLogModel
                    {
                        Action = "LoginFailed",
                        EntityType = "User",
                        IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                        UserAgent = Request.Headers["User-Agent"].ToString(),
                        Timestamp = IndianTime.Now
                    });

                    if (isAjax)
                        return Json(new { success = false, message = "Invalid credentials!" });

                    ViewBag.Message = "Invalid credentials!";
                    return View();
                }

                if (!user.IsActive)
                {
                    if (isAjax)
                        return Json(new
                        {
                            success = false,
                            pendingApproval = true,
                            message = "Your account is pending approval."
                        });

                    TempData["PendingApproval"] = "true";
                    return RedirectToAction("Login");
                }

                return await SignInUser(user, tenantService.GetTenantId(), isAjax);
            }

            // STEP 3: No subdomain -> lookup EmailDirectory in Master DB
            // ========================================================
            var isEmailInput = model.Username.Contains("@");
            var emailEntries = new List<MasterDb.Models.EmailDirectoryModel>();
            if (isEmailInput)
            {
                emailEntries = await _masterDb.EmailDirectory
               .Include(e => e.Tenant)
               .Where(e => e.Email == model.Username &&
                           e.Tenant != null &&
                           e.Tenant.IsActive &&
                           !e.Tenant.IsSuspended)
               .ToListAsync();
            }

            // Also try matching by username across all tenants if email not found
            if (!emailEntries.Any())
            {
                // Fallback: check all active tenants for this username
                //var allTenants = await _masterDb.Tenants
                //    .Where(t => t.IsActive && !t.IsSuspended)
                //    .ToListAsync();

                var allTenants = await _masterDb.Tenants
    .Where(t => t.IsActive)
    .ToListAsync();

                var validTenants = new List<(MasterDb.Models.TenantModel tenant, UserModel user)>();
                var SupendedTenants = new List<(MasterDb.Models.TenantModel tenant, UserModel user)>();

                foreach (var tenant in allTenants)
                {
                    try
                    {
                        // MongoDB mode: all tenants share the same database
                        // Filter users by TenantId so each tenant only finds its own users
                        var foundUser = _db.Users.ToList().FirstOrDefault(u =>
                            u.TenantId == tenant.TenantId &&
                            (u.Username.Equals(model.Username, StringComparison.Ordinal) ||
                             u.Email.Equals(model.Username, StringComparison.OrdinalIgnoreCase))
                            && PasswordHelper.VerifyPassword(model.Password, u.Password)
                            && u.IsActive);

                        if (foundUser != null)
                        {
                            if (tenant.IsSuspended)
                            {
                                SupendedTenants.Add((tenant, foundUser));
                            }
                            else
                            {
                                validTenants.Add((tenant, foundUser));
                            }
                        }
                    }
                    catch
                    {
                    }
                }
                if (!validTenants.Any() && SupendedTenants.Any())
                {
                    if (isAjax)
                        return Json(new
                        {
                            success = false,
                            message = "Your workspace has been suspended. Please contact support."
                        });

                    ViewBag.Message = "Your workspace has been suspended. Please contact support.";
                    return View();
                }
                if (!validTenants.Any())
                {

                    if (isAjax) return Json(new { success = false, message = "Invalid credentials!" });
                    ViewBag.Message = "Invalid credentials!";
                    return View();
                }

                if (validTenants.Count == 1)
                {
                    return await SignInUser(validTenants.First().user, validTenants.First().tenant.TenantId, isAjax);
                }

                // Multiple matches -> workspace picker
                if (isAjax)
                {
                    return Json(new
                    {
                        success = true,
                        pickWorkspace = true,
                        workspaces = validTenants.Select(v => new { tenantId = v.tenant.TenantId, companyName = v.tenant.CompanyName, role = v.user.Role })
                    });
                }

                ViewBag.PickWorkspace = true;
                ViewBag.Workspaces = validTenants.Select(v => new { tenantId = v.tenant.TenantId, companyName = v.tenant.CompanyName, role = v.user.Role });
                return View();
            }
            // ========================================================
            // Email found in EmailDirectory - validate password against each tenant
            // ========================================================
            var matchedTenants = new List<(MasterDb.Models.TenantModel tenant, UserModel user)>();

            foreach (var entry in emailEntries)
            {
                try
                {
                    // MongoDB mode: all tenants share the same database
                    // Filter users by email
                    var foundUser = _db.Users
                        .ToList()
                        .FirstOrDefault(u =>
                            u.Email.Equals(model.Username, StringComparison.OrdinalIgnoreCase) &&
                            PasswordHelper.VerifyPassword(model.Password, u.Password) &&
                            u.IsActive
                        );

                    if (foundUser != null)
                    {
                        matchedTenants.Add((entry.Tenant, foundUser));
                    }
                }
                catch
                {
                    // ignored
                }
            }


            // ========================================================
            // No match
            // ========================================================

            if (!matchedTenants.Any())
            {
                if (isAjax)
                {
                    return Json(new { success = false, message = "Invalid credentials!" });
                }

                ViewBag.Message = "Invalid credentials!";
                return View();
            }

            // ========================================================
            // Single match -> login directly
            // ========================================================
            // Audit log for failed login attempt
            _db.AuditLogs.Add(new AuditLogModel
            {
                Action = "LoginFailed",
                EntityType = "User",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers["User-Agent"].ToString(),
                Timestamp = IndianTime.Now
            });

            if (matchedTenants.Count == 1)
            {
                var match = matchedTenants.First();

                return await SignInUser(
                    match.user,
                    match.tenant.TenantId,
                    isAjax
                );
            }


            // ========================================================
            // Multiple matches -> workspace picker
            // ========================================================
            if (isAjax)
            {
                return Json(new
                {
                    success = true,
                    pickWorkspace = true,
                    workspaces = matchedTenants.Select(v => new
                    {
                        tenantId = v.tenant.TenantId,
                        companyName = v.tenant.CompanyName,
                        role = v.user.Role
                    })
                });
            }

            ViewBag.PickWorkspace = true;
            ViewBag.Workspaces = matchedTenants.Select(v => new
            {
                tenantId = v.tenant.TenantId,
                companyName = v.tenant.CompanyName,
                role = v.user.Role
            });

            return View();
        }


        // ========================================================
        // Workspace Picker: User selects which org to login to
        // ========================================================
        [HttpPost]
        public async Task<IActionResult> SelectWorkspace(int tenantId, string username, string password)
        {
            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

            var tenant = await _masterDb.Tenants
                .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.IsActive);

            if (tenant == null)
            {
                if (isAjax)
                {
                    return Json(new { success = false, message = "Organization not found" });
                }

                return RedirectToAction("Login");
            }

            try
            {
                // MongoDB mode: use shared AppDbContext, filter by username AND tenant
                var user = _db.Users
                    .ToList()
                    .FirstOrDefault(u =>
                        u.TenantId == tenantId &&
                        (u.Username.Equals(username, StringComparison.Ordinal) ||
                         u.Email.Equals(username, StringComparison.OrdinalIgnoreCase)) &&
                        PasswordHelper.VerifyPassword(password, u.Password) &&
                        u.IsActive
                    );

                if (user == null)
                {
                    if (isAjax)
                    {
                        return Json(new { success = false, message = "Invalid credentials" });
                    }

                    return RedirectToAction("Login");
                }

                return await SignInUser(user, tenantId, isAjax);
            }
            catch (Exception ex)
            {
                if (isAjax)
                {
                    return Json(new { success = false, message = $"Error: {ex.Message}" });
                }

                return RedirectToAction("Login");
            }
        }

        // Common sign-in helper (issues JWT with TenantId)
        // ========================================================
        private async Task<IActionResult> SignInUser(UserModel user, int tenantId, bool isAjax)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();

            // Check if this is the user's first login (LastActivity is null)
            bool isFirstLogin = !user.LastActivity.HasValue;

            // Audit log for successful login
            _db.AuditLogs.Add(new AuditLogModel
            {
                UserId = user.UserId,
                Action = "Login",
                EntityType = "User",
                EntityId = user.UserId,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers["User-Agent"].ToString(),
                Timestamp = IndianTime.Now
            });

            // Send welcome email on first login (fire-and-forget, don't block)
            if (isFirstLogin && !string.IsNullOrEmpty(user.Email))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Use EmailService.SendEmailAsync which handles credentials + logging
                        await _emailService.SendEmailAsync(
                            user.UserId,
                            user.Email,
                            "Welcome to PropTech CRM - Your Account is Ready",
                            $"<div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:20px;'>" +
                            $"<h2 style='color:#1a6fa8;'>Welcome to PropTech CRM!</h2>" +
                            $"<p>Hi {user.Username},</p>" +
                            $"<p>Your account has been created successfully. You can now log in and start managing your real estate business.</p>" +
                            $"<p><strong>Your Login Details:</strong><br/>" +
                            $"Username: {user.Email}<br/>" +
                            $"Role: {user.Role}</p>" +
                            $"<p>Get started by exploring the Dashboard, adding leads, and managing properties.</p>" +
                            $"<p style='margin-top:20px;'>Best regards,<br/>PropTech CRM Team</p>" +
                            $"</div>",
                            templateName: "WelcomeEmail",
                            category: "Welcome"
                        );
                    }
                    catch { }
                });
            }

            // Clear only authentication cookies, NOT anti-forgery token
            var cookiesToDelete = Request.Cookies.Keys
                .Where(key => key != ".AspNetCore.Antiforgery" &&
                             !key.StartsWith("__RequestVerificationToken"))
                .ToList();

            foreach (var cookie in cookiesToDelete)
            {
                Response.Cookies.Delete(cookie);
            }

            var token = GenerateJwtToken(user, tenantId);

            // Store JWT token in cookie with unique path
            Response.Cookies.Append("jwtToken", token, new CookieOptions
            {
                HttpOnly = false,
                Secure = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddHours(8)
            });

            // Sign in user with Cookie Authentication (required for [Authorize] attribute)
            var claims = new List<Claim>
            {
                new Claim("UserId", user.UserId.ToString()),
                new Claim("TenantId", tenantId.ToString()),

                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("ChannelPartnerId", user.ChannelPartnerId?.ToString() ?? ""),
                new Claim("token", token)
            };

            var claimsIdentity = new ClaimsIdentity(claims, Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(
               Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme,
                               new ClaimsPrincipal(claimsIdentity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8) });


            return user.Role switch
            {
                "Admin" => RedirectOrJson(isAjax, "Index", "Dashboard", user),
                "Sales" => RedirectOrJson(isAjax, "Index", "Dashboard", user),
                "Agent" => RedirectOrJson(isAjax, "Index", "Dashboard", user),
                "Partner" => RedirectOrJson(isAjax, "Index", "Home", user),
                "SuperAdmin" => RedirectOrJson(isAjax, "Dashboard", "SuperAdmin", user),
                _ => RedirectOrJson(isAjax, "Index", "Dashboard", user)

            };
        }

        private IActionResult RedirectOrJson(bool isAjax, string action, string controller, UserModel user)
        {
            if (isAjax)
                return Json(new { success = true, redirect = Url.Action(action, controller), username = user.Username, role = user.Role });
            return RedirectToAction(action, controller);
        }

        private string GenerateJwtToken(UserModel user, int tenantId = 0)
        {
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim("UserId", user.UserId.ToString()),   // ? UserId stored
                new Claim("TenantId", tenantId.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role)
            };

            var token = new JwtSecurityToken(
                _config["Jwt:Issuer"],
                _config["Jwt:Audience"],
                claims,
                expires: IndianTime.Now.AddHours(1),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private string GenerateSuperAdminToken(MasterDb.Models.SuperAdminModel superAdmin)
        {
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim("UserId", superAdmin.Id.ToString()),
                new Claim("IsSuperAdmin", "true"),
                new Claim(ClaimTypes.Name, superAdmin.FullName),
                new Claim(ClaimTypes.Email, superAdmin.Email),
                new Claim(ClaimTypes.Role, "SuperAdmin")
            };

            var token = new JwtSecurityToken(
                _config["Jwt:Issuer"],
                _config["Jwt:Audience"],
                claims,
                expires: IndianTime.Now.AddHours(8),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
        [HttpGet]
        public IActionResult ForgotPassword()
        {
            var subdomainPId = HttpContext.Items["SubdomainPartnerId"] as int?;
            ViewBag.CompanyLogo = BrandingResolver.ResolveCompanyLogo(_db, subdomainPId);
            ViewBag.CompanyName = BrandingResolver.ResolveCompanyName(_db, subdomainPId, HttpContext.Items["SubdomainPartnerName"] as string);
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> ForgotPassword([FromForm] ForgotPasswordModel model)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == model.Email);
            if (user == null)
            {
                // don't reveal presence
                ViewBag.Message = "If the email exists, a reset link was sent.";
                return View();
            }

            var token = Guid.NewGuid().ToString("N");
            user.ResetToken = token;
            user.ResetTokenExpiry = DateTime.UtcNow.AddHours(1); // Token expires in 1 hour
            _db.Users.Update(user);
            await _db.SaveChangesAsync();

            // send reset email
            try
            {
                var (from, pass) = await _emailService.GetEmailCredentials(user.UserId);

                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(pass))
                {
                    ViewBag.Message = "Email service not configured. Please contact administrator.";
                    return View();
                }

                var resetLink = Url.Action("ResetPasswordWithToken", "Account", new { token }, Request.Scheme);

                using var mail = new MailMessage();
                mail.From = new MailAddress(from);
                mail.To.Add(user.Email);
                mail.Subject = "Reset Your Password";
                mail.Body = $@"
                    <h2>Password Reset Request</h2>
                    <p>You requested to reset your password. Click the link below to reset it:</p>
                    <p><a href='{resetLink}' style='padding: 10px 20px; background-color: #007bff; color: white; text-decoration: none; border-radius: 5px;'>Reset Password</a></p>
                    <p>This link will expire in 1 hour.</p>
                    <p>If you didn't request this, please ignore this email.</p>
                ";
                mail.IsBodyHtml = true;

                using var smtp = new SmtpClient("smtp.gmail.com", 587)
                {
                    Credentials = new NetworkCredential(from, pass),
                    EnableSsl = true,
                    Timeout = 10000
                };

                var sendTask = smtp.SendMailAsync(mail);
                if (await Task.WhenAny(sendTask, Task.Delay(15000)) == sendTask)
                {
                    await sendTask;
                }
                else
                {
                    throw new TimeoutException("Email sending timed out after 15 seconds");
                }

                ViewBag.Message = "Password reset link sent to your email. Please check your inbox.";
            }
            catch (SmtpException smtpEx)
            {
                ViewBag.Message = $"SMTP Error: {smtpEx.Message}. Check: 1) Gmail App Password is correct (16 chars), 2) 2-Step Verification enabled, 3) Port 587 not blocked";
            }
            catch (TimeoutException)
            {
                ViewBag.Message = "Connection timeout. Check: 1) Internet connection, 2) Firewall blocking port 587, 3) Gmail App Password configured";
            }
            catch (Exception ex)
            {
                ViewBag.Message = $"Failed to send email: {ex.Message}";
            }

            return View();

        }

        [HttpGet]
        public async Task<IActionResult> ResetPasswordWithToken(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                ViewBag.Error = "Invalid reset link.";
                return View("ResetPasswordTokenExpired");
            }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.ResetToken == token);
            if (user == null)
            {
                ViewBag.Error = "Invalid or expired reset link.";
                return View("ResetPasswordTokenExpired");
            }

            // Check token expiry
            if (user.ResetTokenExpiry.HasValue && user.ResetTokenExpiry.Value < DateTime.UtcNow)
            {
                ViewBag.Error = "This reset link has expired. Please request a new one.";
                return View("ResetPasswordTokenExpired");
            }

            var model = new ResetPasswordModel { Username = user.Username };
            ViewBag.Token = token;
            return View("ResetPasswordWithToken", model);
        }

        [HttpPost]
        public async Task<IActionResult> ResetPasswordWithToken([FromForm] string token, [FromForm] string newPassword, [FromForm] string confirmPassword)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(newPassword))
            {
                ViewBag.Error = "Invalid request.";
                return View("ResetPasswordTokenExpired");
            }

            if (newPassword != confirmPassword)
            {
                ViewBag.Error = "Passwords do not match.";
                ViewBag.Token = token;
                return View("ResetPasswordWithToken", new ResetPasswordModel());
            }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.ResetToken == token);
            if (user == null || (user.ResetTokenExpiry.HasValue && user.ResetTokenExpiry.Value < DateTime.UtcNow))
            {
                ViewBag.Error = "Invalid or expired reset link.";
                return View("ResetPasswordTokenExpired");
            }

            // Reset password
            // user.Password = newPassword; // Plain text - uncomment this and comment below to disable hashing
            user.Password = PasswordHelper.HashPassword(newPassword); // Hashed password
            user.ResetToken = null;
            user.ResetTokenExpiry = null;

            var userSettings = await _db.UserSettings.FirstOrDefaultAsync(us => us.Username == user.Username);
            if (userSettings != null)
            {
                userSettings.PasswordLastChanged = IndianTime.Now;
                _db.UserSettings.Update(userSettings);
            }

            _db.Users.Update(user);
            await _db.SaveChangesAsync();

            TempData["PasswordResetSuccess"] = "Password reset successfully. Please login with your new password.";
            return RedirectToAction(nameof(Login));
        }

        private string GetUsernameFromToken()
        {
            string token = _httpContextAccessor.HttpContext?.Request.Cookies["jwtToken"];
            if (string.IsNullOrEmpty(token)) return null;

            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(token);
                return jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name || c.Type == "name")?.Value;
            }
            catch
            {
                return null;
            }
        }
        [HttpGet]
        public IActionResult ResetPassword()
        {
            string username = GetUsernameFromToken(); // however you get username
            var vm = new ResetPasswordModel { Username = username };
            return View(vm);
        }

        [HttpPost]
        public async Task<IActionResult> ValidateCurrentPassword([FromForm] string currentPassword)
        {
            if (string.IsNullOrWhiteSpace(currentPassword))
            {
                return Json(new { isValid = false, message = "Current password is required." });
            }

            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrWhiteSpace(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                return Unauthorized(new { isValid = false, message = "User session expired. Please login again." });
            }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null)
            {
                return NotFound(new { isValid = false, message = "User not found." });
            }

            bool isValid = PasswordHelper.VerifyPassword(currentPassword, user.Password);
            // bool isValid = string.Equals(user.Password, currentPassword, StringComparison.Ordinal); // Plain text - uncomment this and comment above to disable hashing
            return Json(new
            {
                isValid,
                message = isValid ? "Current password is correct." : "Current password is incorrect."
            });
        }

        // --- Reset password POST
        [HttpPost]
        public async Task<IActionResult> ResetPassword([FromForm] ResetPasswordModel model)
        {
            // Get current user's username from claims if not provided
            if (string.IsNullOrEmpty(model.Username))
            {
                var userIdClaim = User.FindFirst("UserId")?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                {
                    TempData["Error"] = "User session expired. Please login again.";
                    return RedirectToAction("Login");
                }

                var currentUser = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId);
                if (currentUser == null)
                {
                    TempData["Error"] = "User not found.";
                    return RedirectToAction("Login");
                }
                model.Username = currentUser.Username;
            }

            if (!ModelState.IsValid)
            {
                var firstError = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .FirstOrDefault();

                TempData["Error"] = string.IsNullOrWhiteSpace(firstError)
                    ? "Please check password fields and try again."
                    : firstError;
                return RedirectToAction("Index", "Profile");
            }

            // Validate password confirmation
            if (model.NewPassword != model.ConfirmPassword)
            {
                TempData["Error"] = "New password and confirm password do not match.";
                return RedirectToAction("Index", "Profile");
            }

            // Validate current password
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == model.Username);
            if (user == null || !PasswordHelper.VerifyPassword(model.oldPassword, user.Password))
            // if (user == null || !string.Equals(user.Password, model.oldPassword, StringComparison.Ordinal)) // Plain text - uncomment this and comment above to disable hashing
            {
                TempData["Error"] = "Current password is incorrect.";
                return RedirectToAction("Index", "Profile");
            }

            // Update password
            // user.Password = model.NewPassword; // Plain text - uncomment this and comment below to disable hashing
            user.Password = PasswordHelper.HashPassword(model.NewPassword); // Hashed password
            user.ResetToken = null;
            user.ResetTokenExpiry = null;

            var userSettings = await _db.UserSettings.FirstOrDefaultAsync(us => us.Username == user.Username);
            if (userSettings != null)
            {
                userSettings.PasswordLastChanged = IndianTime.Now;
                _db.UserSettings.Update(userSettings);
            }

            _db.Users.Update(user);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Password updated successfully!";
            return RedirectToAction("Index", "Profile");
        }

        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            // Audit log for logout
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (!string.IsNullOrEmpty(userIdClaim) && int.TryParse(userIdClaim, out int logoutUserId))
            {
                _db.AuditLogs.Add(new AuditLogModel
                {
                    UserId = logoutUserId,
                    Action = "Logout",
                    EntityType = "User",
                    EntityId = logoutUserId,
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = Request.Headers["User-Agent"].ToString(),
                    Timestamp = IndianTime.Now
                });
            }

            // Check if currently impersonating and warn
            var isImpersonating = HttpContext.Session.GetString("IsImpersonating");
            if (isImpersonating == "true")
            {
                // Clear impersonation data before logout
                HttpContext.Session.Remove("OriginalAdminId");
                HttpContext.Session.Remove("OriginalAdminUsername");
                HttpContext.Session.Remove("IsImpersonating");
            }

            // Sign out from Cookie Authentication
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            HttpContext.Session.Clear();

            // ?? Clear cookies
            foreach (var cookie in Request.Cookies.Keys)
            {
                Response.Cookies.Delete(cookie);
            }

            // ?? Disable browser cache
            Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";

            // Redirect to Login
            return RedirectToAction("Landing", "Home");
        }

        public async Task<IActionResult> DeleteAccount()
        {
            string username = GetUsernameFromToken(); // get logged-in user
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);

            if (user == null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction("Index", "Settings");
            }
            var userSettings = await _db.UserSettings.FirstOrDefaultAsync(us => us.Username == user.Username);
            if (userSettings != null)
            {
                userSettings.AccountDeletedAt = IndianTime.Now; // or DateTime.UtcNow
                _db.UserSettings.Update(userSettings);
            }


            _db.Users.Remove(user);
            await _db.SaveChangesAsync();

            return RedirectToAction("Login", "Account");
        }



        [HttpPost]
        public void ClearSessionOnClose()
        {
            HttpContext.Session.Clear();
            Response.Cookies.Delete("jwtToken");
            Response.Cookies.Delete("Username");
            Response.Cookies.Delete("UserRole");
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

        [HttpGet]
        public async Task<IActionResult> MigratePasswords()
        {
            // One-time use: converts all plain-text passwords to hashed passwords
            // Remove or disable this endpoint after running once
            var users = await _db.Users.ToListAsync();
            int migrated = 0;
            foreach (var user in users)
            {
                // Skip already hashed passwords (they contain a dot separator)
                if (!string.IsNullOrEmpty(user.Password) && !user.Password.Contains('.'))
                {
                    user.Password = PasswordHelper.HashPassword(user.Password);
                    migrated++;
                }
            }
            await _db.SaveChangesAsync();
            return Json(new { success = true, message = $"Migrated {migrated} of {users.Count} passwords to hashed format." });
        }

        [HttpPost]
        public IActionResult KeepAlive()
        {
            // Simple endpoint to keep session alive
            HttpContext.Session.SetString("LastActivity", IndianTime.Now.ToString("O"));
            return Json(new { success = true, timestamp = IndianTime.Now });
        }

        public IActionResult Dashboard()
        {
            var userInfo = GetUserDetailsFromToken();

            ViewBag.UserId = userInfo.userId;
            ViewBag.Username = userInfo.username;
            ViewBag.Role = userInfo.role;

            return View();
        }

        // Impersonation methods
        [HttpGet]
        public async Task<IActionResult> GetUsersForImpersonation()
        {
            var currentUser = GetUserDetailsFromToken();
            if (currentUser.role != "Admin")
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            var users = await _db.Users
                .Where(u => u.Username != currentUser.username && u.IsActive)
                .Select(u => new { u.UserId, u.Username, u.Role })
                .OrderBy(u => u.Role).ThenBy(u => u.Username)
                .ToListAsync();

            var groupedUsers = users.GroupBy(u => u.Role)
                .ToDictionary(g => g.Key, g => g.ToList());

            return Json(new { success = true, users = groupedUsers });
        }

        [HttpPost]
        public async Task<IActionResult> StartImpersonation(int userId)
        {
            var currentUser = GetUserDetailsFromToken();
            if (currentUser.role != "Admin")
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            var targetUser = await _db.Users.FindAsync(userId);
            if (targetUser == null || !targetUser.IsActive)
            {
                return Json(new { success = false, message = "User not found or inactive" });
            }

            // Store original admin info in session
            HttpContext.Session.SetString("OriginalAdminId", currentUser.userId.ToString());
            HttpContext.Session.SetString("OriginalAdminUsername", currentUser.username);
            HttpContext.Session.SetString("IsImpersonating", "true");

            int currentTenantId = 0;
            var currentToken = Request.Cookies["jwtToken"];
            if (!string.IsNullOrEmpty(currentToken))
            {
                var jwtHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var jwtRead = jwtHandler.ReadJwtToken(currentToken);
                int.TryParse(jwtRead.Claims.FirstOrDefault(c => c.Type == "TenantId")?.Value, out currentTenantId);

            }
            if (currentTenantId == 0)
            {
                var tenantService = HttpContext.RequestServices.GetService<Services.ITenantService>();
                if (tenantService != null && tenantService.IsResolved())
                {
                    currentTenantId = tenantService.GetTenantId();
                }
            }
            HttpContext.Session.SetString("OriginalTenantId", currentTenantId.ToString());

            // Generate new token for target user with same TenantId
            var token = GenerateJwtToken(targetUser, currentTenantId);

            // Update JWT cookie
            Response.Cookies.Append("jwtToken", token, new CookieOptions
            {
                HttpOnly = false,
                Secure = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
            });

            // Update authentication claims
            var claims = new List<Claim>
            {
                new Claim("UserId", targetUser.UserId.ToString()),
                                new Claim("TenantId", currentTenantId.ToString()),

                new Claim(ClaimTypes.Name, targetUser.Username),
                new Claim(ClaimTypes.Role, targetUser.Role),
                new Claim("ChannelPartnerId", targetUser.ChannelPartnerId?.ToString() ?? ""),
                new Claim("token", token)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            return Json(new { success = true, message = $"Now impersonating {targetUser.Username}" });
        }

        [HttpPost]
        public async Task<IActionResult> StopImpersonation()
        {
            var isImpersonating = HttpContext.Session.GetString("IsImpersonating");
            if (isImpersonating != "true")
            {
                return Json(new { success = false, message = "Not currently impersonating" });
            }

            var originalAdminId = HttpContext.Session.GetString("OriginalAdminId");
            var originalAdminUsername = HttpContext.Session.GetString("OriginalAdminUsername");

            if (string.IsNullOrEmpty(originalAdminId))
            {
                return Json(new { success = false, message = "Original admin info not found" });
            }

            var adminUser = await _db.Users.FindAsync(int.Parse(originalAdminId));
            if (adminUser == null)
            {
                return Json(new { success = false, message = "Original admin user not found" });
            }

            // Retrieve stored TenantId before clearing session
            var storedTenantId = HttpContext.Session.GetString("OriginalTenantId");
            int tenantId = 0;
            if (!string.IsNullOrEmpty(storedTenantId))
            {
                int.TryParse(storedTenantId, out tenantId);
            }
            else
            {
                // Fallback: get TenantId from current JWT
                var curToken = Request.Cookies["jwtToken"];
                if (!string.IsNullOrEmpty(curToken))
                {
                    var jwtHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                    var jwtRead = jwtHandler.ReadJwtToken(curToken);
                    int.TryParse(jwtRead.Claims.FirstOrDefault(c => c.Type == "TenantId")?.Value, out tenantId);
                }
            }

            // Clear impersonation session data
            HttpContext.Session.Remove("OriginalAdminId");
            HttpContext.Session.Remove("OriginalAdminUsername");
            HttpContext.Session.Remove("IsImpersonating");
            HttpContext.Session.Remove("OriginalTenantId");
            // Generate new token for admin user
            var token = GenerateJwtToken(adminUser, tenantId);

            // Update JWT cookie
            Response.Cookies.Append("jwtToken", token, new CookieOptions
            {
                HttpOnly = false,
                Secure = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
            });

            // Update authentication claims
            var claims = new List<Claim>
            {
                new Claim("UserId", adminUser.UserId.ToString()),
                new Claim("TenantId", tenantId.ToString()),
                new Claim(ClaimTypes.Name, adminUser.Username),
                new Claim(ClaimTypes.Role, adminUser.Role),
                new Claim("ChannelPartnerId", adminUser.ChannelPartnerId?.ToString() ?? ""),
                new Claim("token", token)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);
            // Prevent browser cache from showing stale data
            Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";

            return Json(new { success = true, message = "Stopped impersonation, back to admin", redirectUrl = "/Impersonation" });
        }
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> TestMasterDb()
        {
            try
            {
                // Fetch tenants from master DB (This was missing and causing a compilation error)
                var tenants = await _masterDb.Tenants.ToListAsync();

                var superAdmins = await _masterDb.SuperAdmins.ToListAsync();
                var emailDirectory = await _masterDb.EmailDirectory.Take(10).ToListAsync();
                var tenantService = HttpContext.RequestServices.GetService<Services.ITenantService>();

                return Json(new
                {
                    success = true,
                    masterDbConnected = true,
                    tenantResolved = tenantService?.IsResolved() ?? false,
                    currentTenantId = tenantService?.GetTenantId(),
                    currentTenantName = tenantService?.GetTenantName(),
                    tenants = tenants.Select(t => new
                    {
                        t.TenantId,
                        t.CompanyName,
                        t.Subdomain,
                        t.Plan,
                        t.IsActive,
                        t.IsSuspended
                    }),
                    superAdmins = superAdmins.Select(s => new
                    {
                        s.Id,
                        s.Email,
                        s.FullName,
                        s.IsActive,
                        passwordHashLength = s.PasswordHash?.Length ?? 0
                    }),
                    emailDirectorySample = emailDirectory.Select(e => new
                    {
                        e.Email,
                        e.TenantId
                    })
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    error = ex.Message,
                    innerError = ex.InnerException?.Message
                });
            }
        }
    }
}

