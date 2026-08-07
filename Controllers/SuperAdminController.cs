using CRM.MasterDb;
using CRM.MasterDb.Models;
using CRM.Models;
using CRM.Services;
using CRM.Helpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;

namespace CRM.Controllers
{
    [Authorize(Roles = "SuperAdmin")]
    public class SuperAdminController : Controller
    {
        private readonly MasterDbContext _masterDb;
        private readonly IConfiguration _config;
        private readonly ILogger<SuperAdminController> _logger;
        private readonly IWebHostEnvironment _env;

        public SuperAdminController(
            MasterDbContext masterDb,
            IConfiguration config,
            ILogger<SuperAdminController> logger,
            IWebHostEnvironment env)
        {
            _masterDb = masterDb;
            _config = config;
            _logger = logger;
            _env = env;
        }

        // ==========================================
        // SuperAdmin Login (separate from tenant login)
        // ==========================================
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Dashboard");
            }
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Login(LoginModel model)
        {
            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

            if (!ModelState.IsValid)
            {
                if (isAjax) return Json(new { success = false, message = "Please fill in all required fields" });
                ViewBag.Message = "Please fill in all required fields";
                return View();
            }

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

            if (isAjax)
                return Json(new { success = false, message = "Invalid credentials!" });

            ViewBag.Message = "Invalid credentials!";
            return View();
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

        // ==========================================
        // Dashboard
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            var totalTenants = await _masterDb.Tenants.CountAsync();
            var activeTenants = await _masterDb.Tenants.CountAsync(t => t.IsActive && !t.IsSuspended);
            var suspendedTenants = await _masterDb.Tenants.CountAsync(t => t.IsSuspended);

            var totalInquiries = await _masterDb.Inquiries.CountAsync();
            var newInquiries = await _masterDb.Inquiries.CountAsync(i => i.Status == "New");

            var totalEmails = await _masterDb.EmailDirectory.CountAsync();

            ViewBag.TotalTenants = totalTenants;
            ViewBag.ActiveTenants = activeTenants;
            ViewBag.SuspendedTenants = suspendedTenants;
            ViewBag.TotalInquiries = totalInquiries;
            ViewBag.NewInquiries = newInquiries;
            ViewBag.TotalEmails = totalEmails;

            var recentInquiries = await _masterDb.Inquiries
                .OrderByDescending(i => i.CreatedOn)
                .Take(5)
                .ToListAsync();

            var recentTenants = await _masterDb.Tenants
                .OrderByDescending(t => t.CreatedOn)
                .Take(5)
                .ToListAsync();

            ViewBag.RecentInquiries = recentInquiries;
            ViewBag.RecentTenants = recentTenants;

            var token = Request.Cookies["jwtToken"];
            string saUsername = "";
            if (!string.IsNullOrEmpty(token))
            {
                try
                {
                    var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                    var jwt = handler.ReadJwtToken(token);
                    saUsername = jwt.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Name || c.Type == "name")?.Value ?? "";
                }
                catch { }
            }
            ViewBag.Username = saUsername;

            var saCompanyNameSetting = _masterDb.SaasSetting.FirstOrDefault(s => s.SettingKey == "CompanyName");
            ViewBag.CompanyName = saCompanyNameSetting?.SettingValue ?? "PropTech CRM";
            var saCompanyLogoSetting = _masterDb.SaasSetting.FirstOrDefault(s => s.SettingKey == "CompanyLogo");
            ViewBag.CompanyLogo = saCompanyLogoSetting?.SettingValue;
            if (string.IsNullOrEmpty(ViewBag.CompanyLogo as string))
            {
                var saBranding = _masterDb.SaasBranding.FirstOrDefault();
                ViewBag.CompanyLogo = saBranding?.CompanyLogo;
            }
            if (string.IsNullOrEmpty(ViewBag.CompanyLogo as string))
            {
                ViewBag.CompanyLogo = "/icons/PropTech_Logo_Color.png";
            }
            else
            {
                ViewBag.CompanyLogo = "/icons/PropTech_Logo_Color.png";
            }

            var superAdmin = await _masterDb.SuperAdmins.FirstOrDefaultAsync();
            if (superAdmin != null)
            {
                ViewBag.SuperAdminProfileImage = superAdmin.ProfileImagePath;
            }

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var token = Request.Cookies["jwtToken"];
            string saUsername = "";
            if (!string.IsNullOrEmpty(token))
            {
                try
                {
                    var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                    var jwt = handler.ReadJwtToken(token);
                    saUsername = jwt.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Name || c.Type == "name")?.Value ?? "";
                }
                catch { }
            }

            var superAdmin = await _masterDb.SuperAdmins.FirstOrDefaultAsync();
            if (superAdmin == null)
            {
                return NotFound();
            }

            ViewBag.Username = saUsername;
            return View(superAdmin);
        }

        [HttpPost]
        public async Task<IActionResult> UploadProfilePicture(IFormFile? profileImage)
        {
            var token = Request.Cookies["jwtToken"];
            string saUsername = "";
            if (!string.IsNullOrEmpty(token))
            {
                try
                {
                    var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                    var jwt = handler.ReadJwtToken(token);
                    saUsername = jwt.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Name || c.Type == "name")?.Value ?? "";
                }
                catch { }
            }

            var superAdmin = await _masterDb.SuperAdmins.FirstOrDefaultAsync();
            if (superAdmin == null)
            {
                return Json(new { success = false, error = "SuperAdmin not found" });
            }

            if (profileImage != null && profileImage.Length > 0)
            {
                var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "profiles");
                Directory.CreateDirectory(uploadsDir);

                var ext = Path.GetExtension(profileImage.FileName).ToLower();
                var fileName = $"superadmin_{DateTime.Now:yyyyMMddHHmmss}{ext}";
                var filePath = Path.Combine(uploadsDir, fileName);

                using (var ms = new MemoryStream())
                {
                    await profileImage.CopyToAsync(ms);
                    var imageBytes = ms.ToArray();
                    await System.IO.File.WriteAllBytesAsync(filePath, imageBytes);
                    superAdmin.ProfileImage = imageBytes;
                }

                if (!string.IsNullOrEmpty(superAdmin.ProfileImagePath))
                {
                    var oldPath = Path.Combine(_env.WebRootPath, superAdmin.ProfileImagePath.TrimStart('/'));
                    if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
                }

                superAdmin.ProfileImagePath = $"/uploads/profiles/{fileName}";
                _masterDb.SuperAdmins.Update(superAdmin);
            }

            return Json(new { success = true, imagePath = superAdmin.ProfileImagePath });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveProfilePicture()
        {
            var superAdmin = await _masterDb.SuperAdmins.FirstOrDefaultAsync();
            if (superAdmin == null)
            {
                return Json(new { success = false, error = "SuperAdmin not found" });
            }

            if (!string.IsNullOrEmpty(superAdmin.ProfileImagePath))
            {
                var oldPath = Path.Combine(_env.WebRootPath, superAdmin.ProfileImagePath.TrimStart('/'));
                if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
            }

            superAdmin.ProfileImage = null;
            superAdmin.ProfileImagePath = null;
            _masterDb.SuperAdmins.Update(superAdmin);

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateSuperAdminProfileRequest request)
        {
            var superAdmin = await _masterDb.SuperAdmins.FirstOrDefaultAsync();
            if (superAdmin == null)
            {
                return Json(new { success = false, error = "SuperAdmin not found" });
            }

            if (!string.IsNullOrWhiteSpace(request.FullName))
            {
                superAdmin.FullName = request.FullName.Trim();
                await _masterDb.SaveChangesAsync();
            }

            return Json(new { success = true });
        }

        public class UpdateSuperAdminProfileRequest
        {
            public string FullName { get; set; } = "";
        }


        // ==========================================
        // Tenants List
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Tenants(string? search, string? status)
        {
            var query = _masterDb.Tenants.AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(t =>
                    t.CompanyName.Contains(search) ||
                    (t.Subdomain != null && t.Subdomain.Contains(search)));
            }

            if (status == "active")
            {
                query = query.Where(t => t.IsActive && !t.IsSuspended);
            }
            else if (status == "suspended")
            {
                query = query.Where(t => t.IsSuspended);
            }
            else if (status == "inactive")
            {
                query = query.Where(t => !t.IsActive);
            }

            var tenants = await query
                .OrderByDescending(t => t.CreatedOn)
                .ToListAsync();

            ViewBag.Search = search;
            ViewBag.Status = status;

            return View(tenants);
        }

        // ==========================================
        // Inquiries List
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Inquiries(string? status)
        {
            var query = _masterDb.Inquiries.AsQueryable();

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(i => i.Status == status);
            }

            var inquiries = await query
                .OrderByDescending(i => i.CreatedOn)
                .ToListAsync();

            // Load related data separately for MongoDB
            var allTenants = await _masterDb.Tenants.ToListAsync();

            var viewModels = inquiries.Select(inq => new InquiryViewModel
            {
                Inquiry = inq,
                ReferralCompany = inq.ReferralCode != null
                    ? allTenants.FirstOrDefault(t => t.Referral == inq.ReferralCode)?.CompanyName
                    : null
            }).ToList();

            ViewBag.Status = status;

            return View(viewModels);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateInquiryStatus(int inquiryId, string status, string? notes)
        {
            try
            {
                var inquiry = await _masterDb.Inquiries.FindAsync(inquiryId);
                if (inquiry == null)
                {
                    return Json(new { success = false, message = "Inquiry not found" });
                }

                inquiry.Status = status;
                if (!string.IsNullOrEmpty(notes))
                {
                    inquiry.Notes = notes;
                }
                inquiry.UpdatedOn = DateTime.UtcNow;

                await _masterDb.SaveChangesAsync();

                return Json(new { success = true, message = "Status updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating inquiry status");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ==========================================
        // Create Tenant (just creates empty DB)
        // ==========================================
        [HttpGet]
        public IActionResult CreateTenant()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> CreateTenant(string companyName, string subdomain, string plan, int? planId, string? contactperson, string? email, string? phone, string? referralCode)
        {
            plan = "basic";
            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

            // Validate subdomain
            subdomain = subdomain?.ToLower().Trim() ?? "";

            var reserved = new[]
            {
                "www", "api", "admin", "superadmin",
                "app", "mail", "support", "help"
            };

            if (reserved.Contains(subdomain))
            {
                if (isAjax)
                    return Json(new { success = false, message = "This subdomain is reserved." });

                ViewBag.Error = "This subdomain is reserved.";
                return View();
            }

            var exists = await _masterDb.Tenants
                .AnyAsync(t => t.Subdomain == subdomain);

            if (exists)
            {
                if (isAjax)
                    return Json(new { success = false, message = "Subdomain already taken." });

                ViewBag.Error = "Subdomain already taken.";
                return View();
            }

            try
            {
                // MongoDB mode: no separate tenant databases needed
                // All tenants share the same MongoDB database

                // Generate unique referral code: first 2 letters of company name + random number
                var prefix = new string(companyName.Where(char.IsLetter).Take(2).ToArray()).ToUpper();
                if (prefix.Length < 2) prefix = prefix.PadRight(2, 'X');
                var rng = new Random();
                string referral;
                do
                {
                    referral = prefix + rng.Next(1000, 9999).ToString();
                } while (await _masterDb.Tenants.AnyAsync(t => t.Referral == referral));

                // Register in Master DB

                // Assign the next TenantId explicitly: the Mongo shim treats TenantId as a
                // foreign key and deliberately skips it in AutoAssignIntId, so without this
                // the tenant would be persisted with TenantId 0 and could never be found
                // (FindAsync / filters by TenantId), nor would its users/subscriptions link.
                int nextTenantId = 1;
                if (await _masterDb.Tenants.AnyAsync())
                {
                    nextTenantId = (await _masterDb.Tenants.MaxAsync(t => (int?)t.TenantId) ?? 0) + 1;
                }

                var tenant = new TenantModel
                {
                    TenantId = nextTenantId,
                    CompanyName = companyName,
                    Subdomain = subdomain,
                    ConnectionString = "mongodb://localhost:27017/crm",
                    Plan = plan ?? "Basic",
                    Email = email,
                    ContactPerson = contactperson,
                    Phone = phone,
                    IsActive = true,
                    CreatedOn = DateTime.UtcNow,
                    Referral = referral
                };

                _masterDb.Tenants.Add(tenant);
                await _masterDb.SaveChangesAsync();

                // Create 7-day free trial subscription. Plan names in the DB are stored as
                // "Basic Plan"/"Standard Plan"/"Premium Plan", while the form posts a short
                // "basic"/"standard"/"premium" - match by PlanType (case-insensitive) and fall
                // back to the Free plan so a brand-new company is never locked out.
                var selectedPlan = (SaasSubscriptionPlanModel?)null;
                if (planId.HasValue)
                {
                    selectedPlan = await _masterDb.SaasPlans.FirstOrDefaultAsync(p => p.PlanId == planId.Value && p.IsActive);
                }
                if (selectedPlan == null && !string.IsNullOrWhiteSpace(plan))
                {
                    var planType = plan.Trim();
                    selectedPlan = await _masterDb.SaasPlans.FirstOrDefaultAsync(p =>
                        p.IsActive && p.PlanType != null && p.PlanType.ToLower() == planType.ToLower());
                }
                if (selectedPlan == null)
                {
                    selectedPlan = await _masterDb.SaasPlans.FirstOrDefaultAsync(p => p.IsActive && p.PlanType == "Free")
                        ?? await _masterDb.SaasPlans.FirstOrDefaultAsync(p => p.IsActive);
                }

                if (selectedPlan != null)
                {
                    var trial = new MasterDb.Models.TenantSubscriptionModel
                    {
                        TenantId = tenant.TenantId,
                        PlanId = selectedPlan.PlanId,
                        BillingCycle = "Trial",
                        Amount = 0,
                        StartDate = DateTime.UtcNow,
                        EndDate = DateTime.UtcNow.AddDays(7),
                        Status = "Active",
                        AutoRenew = false,
                        PaymentMethod = "Trial",
                        CreatedOn = DateTime.UtcNow
                    };

                    _masterDb.TenantSubscriptions.Add(trial);
                    await _masterDb.SaveChangesAsync();

                    // Keep the tenant's Plan field in sync with the actual plan
                    tenant.Plan = selectedPlan.PlanName;
                    _masterDb.Tenants.Update(tenant);
                    await _masterDb.SaveChangesAsync();
                }

                // Seed the tenant's CompanyName setting so the sidebar footer, welcome banner and
                // PDF headers immediately show this company's name instead of the platform fallback.
                try
                {
                    var appDb = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                    var existingNameSetting = appDb.Settings.FirstOrDefault(s =>
                        s.SettingKey == "CompanyName" && s.TenantId == tenant.TenantId && s.ChannelPartnerId == null);
                    if (existingNameSetting == null)
                    {
                        int nextSettingId = 1;
                        if (appDb.Settings.Any())
                        {
                            nextSettingId = (await appDb.Settings.MaxAsync(s => (int?)s.SettingId) ?? 0) + 1;
                        }
                        appDb.Settings.Add(new SettingsModel
                        {
                            SettingId = nextSettingId,
                            SettingKey = "CompanyName",
                            SettingValue = companyName,
                            TenantId = tenant.TenantId,
                            ChannelPartnerId = null
                        });
                        await appDb.SaveChangesAsync();
                    }
                }
                catch (Exception settingsEx)
                {
                    _logger.LogWarning(settingsEx, "Failed to seed CompanyName setting for tenant {TenantId}", tenant.TenantId);
                }

                _logger.LogInformation($"Tenant created: {companyName} ({subdomain})");
                await CreditReferralEarnings(tenant, referralCode);
                await LogAuditAsync(GetCurrentUserId(), "Create", "Tenant", tenant.TenantId, $"Company: {companyName}, Subdomain: {subdomain}");
                if (isAjax)
                {
                    return Json(new
                    {
                        success = true,
                        message = $"Tenant '{companyName}' created successfully."
                    });
                }

                TempData["Success"] = $"Tenant '{companyName}' created successfully!";
                return RedirectToAction("Tenants");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating tenant");

                if (isAjax)
                    return Json(new { success = false, message = $"Error: {ex.Message}" });

                ViewBag.Error = $"Error: {ex.Message}";
                return View();
            }
        }


        // ==========================================
        // Suspend / Activate Tenant
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> SuspendTenant(int tenantId, string reason)
        {
            var tenant = await _masterDb.Tenants.FindAsync(tenantId);

            if (tenant == null)
                return Json(new { success = false, message = "Tenant not found" });

            tenant.IsSuspended = true;
            tenant.SuspendedReason = reason;
            tenant.UpdatedOn = DateTime.UtcNow;

            _masterDb.Tenants.Update(tenant);
            await _masterDb.SaveChangesAsync();

            _logger.LogInformation($"Tenant suspended: {tenant.CompanyName}. Reason: {reason}");
            await LogAuditAsync(GetCurrentUserId(), "Update", "Tenant", tenantId, $"Suspended: {tenant.CompanyName}. Reason: {reason}");

            return Json(new
            {
                success = true,
                message = $"'{tenant.CompanyName}' suspended."
            });
        }

        [HttpPost]
        public async Task<IActionResult> ActivateTenant(int tenantId)
        {
            var tenant = await _masterDb.Tenants.FindAsync(tenantId);

            if (tenant == null)
                return Json(new { success = false, message = "Tenant not found" });

            tenant.IsSuspended = false;
            tenant.IsActive = true;
            tenant.SuspendedReason = null;
            tenant.UpdatedOn = DateTime.UtcNow;
            _masterDb.Tenants.Update(tenant);
            await _masterDb.SaveChangesAsync();
            try
            {
                _logger.LogInformation($"Tenenat activated: {tenant.CompanyName}");
                await LogAuditAsync(GetCurrentUserId(), "Update", "Tenant", tenantId, $"Activated: {tenant.CompanyName}");

                return Json(new
                {
                    success = true,
                    message = $"'{tenant.CompanyName}' activated.Tables creation pending"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error activating tenant");

                return Json(new
                {
                    success = false,
                    message = $"Error: {ex.Message}"
                });
            }
        }// ==========================================
         // Inquiries
         // ==========================================
        [HttpGet]
        public async Task<IActionResult> GetReferralWallet(int tenantId)
        {
            var earnings = await _masterDb.ReferralEarnings
                .Where(r => r.TenantId == tenantId && !r.IsUsed)
                .ToListAsync();



            var balance = earnings.Sum(e => e.Amount);

            var referralEarnings = await _masterDb.ReferralEarnings
                .Where(r => r.TenantId == tenantId && r.Type == "Referrer")
                .OrderByDescending(r => r.CreatedOn)
                .ToListAsync();

            // Load referred tenants separately (Include is a no-op on MongoDbSet)
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
                })
                .ToList();

            var tenant = await _masterDb.Tenants.FindAsync(tenantId);

            return Json(new
            {
                success = true,
                balance,
                referralCode = tenant?.Referral ?? "",
                referrals
            });
        }

        // ==========================================
        // Referrals Management - SuperAdmin view all
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Referrals()
        {
            // Always ensure every tenant has a referral code (idempotent, cheap)
            await EnsureTenantReferralCodes();

            // Auto-seed referral earnings exactly once (first visit only). The flag is written on
            // every first visit (whether or not seeding ran) so it guards against re-seeding after
            // the SuperAdmin intentionally deletes all rows - including on existing deployments that
            // already had referral data before this flag existed.
            var seedFlag = _masterDb.SaasSetting.FirstOrDefault(s => s.SettingKey == "ReferralSeedDone");
            if (seedFlag == null)
            {
                var existingCount = await _masterDb.ReferralEarnings.CountAsync();
                if (existingCount == 0)
                {
                    await AutoSeedReferralData();
                }
                _masterDb.SaasSetting.Add(new SaasSettingsModel
                {
                    SettingKey = "ReferralSeedDone",
                    SettingValue = "true"
                });
                await _masterDb.SaveChangesAsync();
            }

            // Tenant dropdown for the "Add Referral" modal
            ViewBag.Tenants = await _masterDb.Tenants
                .OrderBy(t => t.CompanyName)
                .ToListAsync();

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetAllReferrals()
        {
            try
            {
                var allReferrals = await _masterDb.ReferralEarnings
                    .OrderByDescending(r => r.CreatedOn)
                    .ToListAsync();

                // Repair legacy records that were persisted without an Id so CRUD works on every row.
                // NOTE: MongoDbSet.Update/Remove silently no-op for string-Id models, so we must use the
                // raw collection and key on the ACTUAL stored _id (which may be null/missing) to persist.
                var refColl = _masterDb.ReferralEarnings.Collection;
                var refBsonColl = refColl.Database.GetCollection<MongoDB.Bson.BsonDocument>(refColl.CollectionNamespace.CollectionName);
                // Catch any row whose _id is not a proper ObjectId (null, missing, or a non-ObjectId
                // legacy value that fails to deserialize into the string Id property).
                var brokenFilter = Builders<MongoDB.Bson.BsonDocument>.Filter.Not(
                    Builders<MongoDB.Bson.BsonDocument>.Filter.Type("_id", MongoDB.Bson.BsonType.ObjectId));
                var brokenDocs = await refBsonColl.Find(brokenFilter).ToListAsync();
                foreach (var doc in brokenDocs)
                {
                    // MongoDB treats _id as immutable (WriteError Code 66) - ReplaceOne cannot alter it.
                    // Insert the repaired copy FIRST (fresh ObjectId, unique) then delete the old doc,
                    // so a failed insert never loses the legacy row.
                    var oldId = doc.Contains("_id") ? doc["_id"] : MongoDB.Bson.BsonNull.Value;
                    doc["_id"] = MongoDB.Bson.ObjectId.GenerateNewId();
                    await refBsonColl.InsertOneAsync(doc);
                    await refBsonColl.DeleteOneAsync(Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("_id", oldId));
                }
                if (brokenDocs.Count > 0)
                {
                    // Re-fetch so this response already reflects the repaired ids
                    allReferrals = await _masterDb.ReferralEarnings
                        .OrderByDescending(r => r.CreatedOn)
                        .ToListAsync();
                }

                // Load all tenants for mapping
                var allTenants = await _masterDb.Tenants.ToListAsync();

                var result = allReferrals.Select(r => new
                {
                    r.Id,
                    TenantName = allTenants.FirstOrDefault(t => t.TenantId == r.TenantId)?.CompanyName ?? "Unknown",
                    r.Type,
                    r.Amount,
                    r.Description,
                    r.ReferralCode,
                    r.IsUsed,
                    r.ReferredTenantId,
                    CreatedOn = r.CreatedOn.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }).ToList();

                return Json(new { success = true, referrals = result });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ========================================================================
        // Referral CRUD (SuperAdmin)
        // ========================================================================
        [HttpPost]
        public async Task<IActionResult> CreateReferral(int tenantId, string referralCode, string type, decimal amount, string description)
        {
            try
            {
                if (tenantId <= 0)
                    return Json(new { success = false, message = "Please select a tenant" });

                _masterDb.ReferralEarnings.Add(new ReferralEarningModel
                {
                    TenantId = tenantId,
                    ReferralCode = string.IsNullOrWhiteSpace(referralCode) ? "REF" + new Random().Next(1000, 9999) : referralCode.Trim(),
                    Type = string.IsNullOrWhiteSpace(type) ? "Referrer" : type.Trim(),
                    Amount = amount,
                    Description = description ?? "",
                    IsUsed = false,
                    CreatedOn = DateTime.UtcNow
                });
                await _masterDb.SaveChangesAsync();
                return Json(new { success = true, message = "Referral created" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateReferralStatus(string id, bool isUsed)
        {
            try
            {
                // FlexibleStringSerializer writes plain BSON strings for ALL string filters (even typed
                // lambdas), which never match ObjectId-typed _id fields. Parse to ObjectId explicitly so
                // the filter value uses the ObjectId serializer.
                if (!MongoDB.Bson.ObjectId.TryParse(id, out var oid) || oid == MongoDB.Bson.ObjectId.Empty)
                    return Json(new { success = false, message = "Referral not found" });

                // $set only - never rewrites _id (a full ReplaceOne would reserialize Id as a plain string
                // via FlexibleStringSerializer and trigger MongoDB WriteError 66 on the immutable _id).
                var res = await _masterDb.ReferralEarnings.Collection.UpdateOneAsync(
                    Builders<ReferralEarningModel>.Filter.Eq("_id", oid),
                    Builders<ReferralEarningModel>.Update.Set(x => x.IsUsed, isUsed));
                if (res.MatchedCount == 0)
                    return Json(new { success = false, message = "Referral not found" });

                return Json(new { success = true, message = "Referral status updated" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteReferral(string id)
        {
            try
            {
                // FlexibleStringSerializer writes plain BSON strings for ALL string filters (even typed
                // lambdas), which never match ObjectId-typed _id fields. Parse to ObjectId explicitly so
                // the filter value uses the ObjectId serializer.
                if (!MongoDB.Bson.ObjectId.TryParse(id, out var oid) || oid == MongoDB.Bson.ObjectId.Empty)
                    return Json(new { success = false, message = "Referral not found" });

                // MongoDbSet.Remove silently no-ops for string-Id models - use the raw collection to persist
                var res = await _masterDb.ReferralEarnings.Collection.DeleteOneAsync(
                    Builders<ReferralEarningModel>.Filter.Eq("_id", oid));
                if (res.DeletedCount == 0)
                    return Json(new { success = false, message = "Referral not found" });

                return Json(new { success = true, message = "Referral deleted" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ========================================================================
        // SuperAdmin Notifications
        // ========================================================================
        [HttpGet]
        public async Task<IActionResult> GetNotifications()
        {
            try
            {
                var newInquiries = await _masterDb.Inquiries
                    .Where(i => i.Status == "New")
                    .OrderByDescending(i => i.CreatedOn)
                    .Take(10)
                    .ToListAsync();

                var recentTenants = await _masterDb.Tenants
                    .Where(t => t.CreatedOn >= DateTime.UtcNow.AddDays(-7))
                    .OrderByDescending(t => t.CreatedOn)
                    .Take(5)
                    .ToListAsync();

                var notifications = new List<object>();

                foreach (var inq in newInquiries)
                {
                    notifications.Add(new
                    {
                        id = "inq_" + inq.InquiryId,
                        title = "New Inquiry",
                        message = $"{inq.CompanyName} - {inq.ContactPerson}",
                        type = "Inquiry",
                        link = "/SuperAdmin/Inquiries",
                        priority = "High",
                        createdOn = inq.CreatedOn.ToString("MMM dd, HH:mm")
                    });
                }

                foreach (var t in recentTenants)
                {
                    notifications.Add(new
                    {
                        id = "tenant_" + t.TenantId,
                        title = "New Tenant Registered",
                        message = $"{t.CompanyName} ({t.Subdomain})",
                        type = "TenantCreated",
                        link = "/SuperAdmin/Tenants",
                        priority = "Normal",
                        createdOn = t.CreatedOn.ToString("MMM dd, HH:mm")
                    });
                }

                return Json(new
                {
                    success = true,
                    count = newInquiries.Count,
                    notifications = notifications
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ==========================================
        // Impersonate Tenant Admin
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> ImpersonateTenant(int tenantId)
        {
            var tenant = await _masterDb.Tenants.FindAsync(tenantId);

            if (tenant == null)
                return Json(new { success = false, message = "Tenant not found" });

            try
            {
                // MongoDB mode: all tenants share the same database
                // Just use the existing AppDbContext to find admin users
                var adminUser = await HttpContext.RequestServices.GetRequiredService<AppDbContext>().Users
                    .FirstOrDefaultAsync(u => u.Role == "Admin" && u.IsActive);

                if (adminUser == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "No active admin found in this tenant."
                    });
                }

                // Sign out current Super Admin
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                // Store session
                HttpContext.Session.SetString("IsSuperAdminImpersonating", "true");
                HttpContext.Session.SetString("SuperAdminEmail",
                    User.FindFirst(ClaimTypes.Email)?.Value ?? "");

                // Claims
                var claims = new List<Claim>
        {
            new Claim("UserId", adminUser.UserId.ToString()),
            new Claim("TenantId", tenant.TenantId.ToString()),
            new Claim(ClaimTypes.Name, adminUser.Username),
            new Claim(ClaimTypes.Role, adminUser.Role),
            new Claim("ChannelPartnerId", adminUser.ChannelPartnerId?.ToString() ?? ""),
            new Claim("IsSuperAdminImpersonating", "true")
        };

                var identity = new ClaimsIdentity(
                    claims,
                    CookieAuthenticationDefaults.AuthenticationScheme
                );

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(identity),
                    new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2)
                    });

                // JWT
                var securityKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                    System.Text.Encoding.UTF8.GetBytes(_config["Jwt:Key"]!)
                );

                var credentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                    securityKey,
                    Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256
                );

                var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
                    _config["Jwt:Issuer"],
                    _config["Jwt:Audience"],
                    claims,
                    expires: DateTime.Now.AddHours(8),
                    signingCredentials: credentials
                );

                var tokenString = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
                    .WriteToken(token);

                Response.Cookies.Append("jwtToken", tokenString, new CookieOptions
                {
                    HttpOnly = false,
                    Secure = true,
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    Expires = DateTimeOffset.UtcNow.AddHours(2)
                });

                _logger.LogInformation(
                    $"Super Admin impersonating tenant: {tenant.CompanyName} as {adminUser.Username}"
                );

                return Json(new
                {
                    success = true,
                    message = $"Now impersonating {adminUser.Username} in {tenant.CompanyName}",
                    redirect = "/"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error impersonating tenant {tenantId}");

                return Json(new
                {
                    success = false,
                    message = $"Error: {ex.Message}"
                });
            }
        }


        // ==========================================
        // Back to Super Admin
        // ==========================================
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> BackToSuperAdmin()
        {
            var isImpersonating = HttpContext.Session.GetString("IsSuperAdminImpersonating");

            if (isImpersonating != "true")
            {
                return Json(new
                {
                    success = false,
                    message = "Not impersonating"
                });
            }

            var superAdminEmail = HttpContext.Session.GetString("SuperAdminEmail");

            var superAdmin = await _masterDb.SuperAdmins
                .FirstOrDefaultAsync(s => s.Email == superAdminEmail && s.IsActive);

            if (superAdmin == null)
            {
                return Json(new
                {
                    success = false,
                    message = "Super Admin not found"
                });
            }

            // Clear session
            HttpContext.Session.Remove("IsSuperAdminImpersonating");
            HttpContext.Session.Remove("SuperAdminEmail");
            HttpContext.Session.Remove("IsImpersonating");

            HttpContext.Session.Remove("OriginalAdminId");

            HttpContext.Session.Remove("OriginalAdminUsername");


            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            var claims = new List<Claim>
    {
        new Claim("UserId", superAdmin.Id.ToString()),
        new Claim("IsSuperAdmin", "true"),
        new Claim(ClaimTypes.Name, superAdmin.FullName),
        new Claim(ClaimTypes.Email, superAdmin.Email),
        new Claim(ClaimTypes.Role, "SuperAdmin")
    };

            var identity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme
            );

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
                });

            // JWT
            var securityKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(_config["Jwt:Key"]!)
            );

            var credentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                securityKey,
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256
            );

            var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
                _config["Jwt:Issuer"],
                _config["Jwt:Audience"],
                claims,
                expires: DateTime.Now.AddHours(8),
                signingCredentials: credentials
            );

            var tokenString = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
                .WriteToken(token);

            Response.Cookies.Append("jwtToken", tokenString, new CookieOptions
            {
                HttpOnly = false,
                Secure = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddHours(8)
            });

            return Json(new
            {
                success = true,
                redirect = "/superadmin/tenants"
            });
        }

        // SaaS Subscription Plans CRUD
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Plans()
        {
            var plans = await _masterDb.SaasPlans.OrderBy(p => p.SortOrder).ToListAsync();
            return View(plans);
        }

        [HttpPost]
        public async Task<IActionResult> CreatePlan(SaasSubscriptionPlanModel plan)
        {
            try
            {
                plan.CreatedOn = DateTime.UtcNow;
                plan.MaxStorageGB = -1; // Unlimited for now
                _masterDb.SaasPlans.Add(plan);
                await _masterDb.SaveChangesAsync();
                await LogAuditAsync(GetCurrentUserId(), "Create", "Plan", plan.PlanId, $"Plan: {plan.PlanName}, Type: {plan.PlanType}");
                return Json(new { success = true, message = "Plan created successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdatePlan(SaasSubscriptionPlanModel plan)
        {
            try
            {
                var existing = await _masterDb.SaasPlans.FindAsync(plan.PlanId);
                if (existing == null) return Json(new { success = false, message = "Plan not found" });

                existing.PlanName = plan.PlanName;
                existing.Description = plan.Description;
                existing.MonthlyPrice = plan.MonthlyPrice;
                existing.YearlyPrice = plan.YearlyPrice;
                existing.MaxUsers = plan.MaxUsers;
                existing.MaxAgents = plan.MaxAgents;
                existing.MaxLeadsPerMonth = plan.MaxLeadsPerMonth;
                existing.MaxPartners = plan.MaxPartners;
                existing.MaxStorageGB = -1; // Unlimited for now
                existing.HasWhatsAppIntegration = plan.HasWhatsAppIntegration;
                existing.HasFacebookIntegration = plan.HasFacebookIntegration;
                existing.HasEmailIntegration = plan.HasEmailIntegration;
                existing.HasCustomAPIAccess = plan.HasCustomAPIAccess;
                existing.HasAdvancedReports = plan.HasAdvancedReports;
                existing.HasCustomBranding = plan.HasCustomBranding;
                existing.HasPrioritySupport = plan.HasPrioritySupport;
                existing.HasImpersonation = plan.HasImpersonation;
                // New feature flags
                existing.HasLeadScoring = plan.HasLeadScoring;
                existing.HasSiteVisitManagement = plan.HasSiteVisitManagement;
                existing.HasDocumentManagement = plan.HasDocumentManagement;
                existing.HasInventoryManagement = plan.HasInventoryManagement;
                existing.HasCampaignManagement = plan.HasCampaignManagement;
                existing.HasLegalManagement = plan.HasLegalManagement;
                existing.HasInvoiceAutomation = plan.HasInvoiceAutomation;
                existing.HasQuotationManagement = plan.HasQuotationManagement;
                existing.HasWorkflowAutomation = plan.HasWorkflowAutomation;
                existing.HasCustomerPortal = plan.HasCustomerPortal;
                existing.HasAIScoring = plan.HasAIScoring;
                existing.HasAIChatbot = plan.HasAIChatbot;
                existing.HasMobileApp = plan.HasMobileApp;
                existing.HasTwoFactorAuth = plan.HasTwoFactorAuth;
                existing.HasCallIntegration = plan.HasCallIntegration;
                existing.HasSmsIntegration = plan.HasSmsIntegration;
                existing.HasMultiLanguage = plan.HasMultiLanguage;
                existing.HasGpsTracking = plan.HasGpsTracking;
                // New limit fields
                existing.MaxSiteVisitsPerMonth = plan.MaxSiteVisitsPerMonth;
                existing.MaxEmailCampaigns = plan.MaxEmailCampaigns;
                existing.MaxDocuments = plan.MaxDocuments;
                existing.MaxProperties = plan.MaxProperties;
                existing.MaxQuotationsPerMonth = plan.MaxQuotationsPerMonth;

                existing.ShowOnLandingPage = plan.ShowOnLandingPage;
                existing.SupportLevel = plan.SupportLevel;
                existing.PlanType = plan.PlanType;
                existing.IsActive = plan.IsActive;
                existing.SortOrder = plan.SortOrder;
                existing.UpdatedOn = DateTime.UtcNow;

                _masterDb.SaasPlans.Update(existing);
                await _masterDb.SaveChangesAsync();
                await LogAuditAsync(GetCurrentUserId(), "Update", "Plan", plan.PlanId, $"Plan: {existing.PlanName}");
                return Json(new { success = true, message = "Plan updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // DeletePlan endpoint removed — replaced by TogglePlanStatus for activate/deactivate

        [HttpGet]
        public async Task<IActionResult> GetPlan(int id)
        {
            var plan = await _masterDb.SaasPlans.FindAsync(id);
            if (plan == null) return Json(new { success = false });
            return Json(new { success = true, plan });
        }

        // ==========================================
        // Tenant Subscriptions Management
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> TenantSubscriptions(string? search, string? plan, string? billing, string? fromDate, string? toDate, int page = 1)
        {
            var allSubscriptions = await _masterDb.TenantSubscriptions
                .Where(s => s.Status == "Active" || s.Status == "Trial" || s.Status == "Scheduled")
                .OrderByDescending(s => s.CreatedOn)
                .ToListAsync();

            // Load related data
            var allTenants = await _masterDb.Tenants.ToListAsync();
            var allPlans = await _masterDb.SaasPlans.Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync();

            // Populate navigation properties and fix amounts
            foreach (var sub in allSubscriptions)
            {
                var planObj = allPlans.FirstOrDefault(p => p.PlanId == sub.PlanId);
                if (planObj != null)
                {
                    if (sub.Amount == 0 && sub.BillingCycle != "Trial")
                    {
                        sub.Amount = sub.BillingCycle?.ToLower() == "annual"
                            ? planObj.YearlyPrice
                            : planObj.MonthlyPrice;
                    }
                    sub.Plan = planObj;
                }
                
                var tenant = allTenants.FirstOrDefault(t => t.TenantId == sub.TenantId);
                if (tenant != null)
                    sub.Tenant = tenant;
            }

            // Apply filters
            var displayList = allTenants.Where(t => t.IsActive).Select(t => new
            {
                Tenant = t,
                Subscription = allSubscriptions
                    .Where(s => s.TenantId == t.TenantId)
                    .OrderByDescending(s => s.CreatedOn)
                    .FirstOrDefault()
            }).ToList();

            // Search filter
            if (!string.IsNullOrEmpty(search))
            {
                displayList = displayList.Where(r => 
                    (r.Tenant.CompanyName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (r.Tenant.Subdomain?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
                    .ToList();
            }

            // Plan filter
            if (!string.IsNullOrEmpty(plan) && int.TryParse(plan, out int planId))
            {
                displayList = displayList.Where(r => r.Subscription?.PlanId == planId).ToList();
            }

            // Billing filter
            if (!string.IsNullOrEmpty(billing))
            {
                displayList = displayList.Where(r => (r.Subscription?.BillingCycle ?? "") == billing).ToList();
            }

            // Date filters
            if (DateTime.TryParse(fromDate, out var fromDt))
            {
                displayList = displayList.Where(r => r.Subscription != null && r.Subscription.StartDate >= fromDt).ToList();
            }
            if (DateTime.TryParse(toDate, out var toDt))
            {
                displayList = displayList.Where(r => r.Subscription != null && r.Subscription.EndDate <= toDt.AddDays(1)).ToList();
            }

            // Pagination
            int pageSize = 15;
            int totalCount = displayList.Count;
            int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            if (totalPages < 1) totalPages = 1;
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var pagedData = displayList.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            // Build filtered model — only pass subscriptions that match filters & pagination
            var filteredTenantIds = pagedData.Select(r => r.Tenant.TenantId).ToHashSet();
            var filteredSubscriptions = allSubscriptions
                .Where(s => filteredTenantIds.Contains(s.TenantId))
                .ToList();

            ViewBag.Plans = allPlans;
            ViewBag.Tenants = allTenants.Where(t => filteredTenantIds.Contains(t.TenantId)).ToList();
            ViewBag.Search = search ?? "";
            ViewBag.SelectedPlan = plan ?? "";
            ViewBag.SelectedBilling = billing ?? "";
            ViewBag.FromDate = fromDate ?? "";
            ViewBag.ToDate = toDate ?? "";
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;

            return View(filteredSubscriptions);
        }

        [HttpPost]
        public async Task<IActionResult> AssignTenantPlan(int tenantId, int planId, string billingCycle)
        {
            try
            {
                var plan = await _masterDb.SaasPlans.FindAsync(planId);
                if (plan == null) return Json(new { success = false, message = "Plan not found" });

                var amount = billingCycle == "Annual" ? plan.YearlyPrice : plan.MonthlyPrice;
                var duration = billingCycle == "Annual" ? 365 : 30;

                // Expire existing active subscriptions
                var existing = await _masterDb.TenantSubscriptions
                    .Where(s => s.TenantId == tenantId && (s.Status == "Active" || s.Status == "Trial"))
                    .ToListAsync();

                foreach (var sub in existing)
                {
                    sub.Status = "Cancelled";
                    sub.CancelledOn = DateTime.UtcNow;
                    sub.UpdatedOn = DateTime.UtcNow;
                }

                _masterDb.TenantSubscriptions.Add(new TenantSubscriptionModel
                {
                    TenantId = tenantId,
                    PlanId = planId,
                    BillingCycle = billingCycle,
                    Amount = amount,
                    StartDate = DateTime.UtcNow,
                    EndDate = DateTime.UtcNow.AddDays(duration),
                    Status = "Active",
                    PaymentMethod = "Manual",
                    CreatedOn = DateTime.UtcNow
                });

                // Update tenant plan field
                var tenant = await _masterDb.Tenants.FindAsync(tenantId);
                if (tenant != null)
                {
                    tenant.Plan = plan.PlanName;
                    tenant.MaxUsers = plan.MaxUsers;
                    tenant.UpdatedOn = DateTime.UtcNow;
                }

                await _masterDb.SaveChangesAsync();
                var tenantInfo = tenant != null ? $"Tenant: {tenant.CompanyName}" : "";
                await LogAuditAsync(GetCurrentUserId(), "Create", "Subscription", null, $"Assigned plan '{plan.PlanName}' ({billingCycle}) to tenant #{tenantId}. {tenantInfo}");
                return Json(new { success = true, message = "Plan assigned successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> TogglePlanStatus(int planId, bool isActive)
        {
            try
            {
                var plan = await _masterDb.SaasPlans.FindAsync(planId);
                if (plan == null)
                    return Json(new { success = false, message = "Plan not found" });

                plan.IsActive = isActive;
                plan.UpdatedOn = DateTime.UtcNow;
                _masterDb.SaasPlans.Update(plan);
                await _masterDb.SaveChangesAsync();

                var msg = isActive ? "Plan activated successfully" : "Plan deactivated successfully";
                await LogAuditAsync(GetCurrentUserId(), "Update", "Plan", planId, $"Plan: {plan.PlanName}. {msg}");
                return Json(new { success = true, message = msg });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ==========================================
        // Email Templates CRUD (SuperAdmin)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> EmailTemplates()
        {
            // Get templates from the shared AppDbContext (tenant DB has email_templates collection)
            var appDb = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var emailTemplates = await appDb.EmailTemplates.OrderByDescending(t => t.UpdatedOn).ToListAsync();
            return View(emailTemplates);
        }

        [HttpPost]
        public async Task<IActionResult> CreateTemplate([FromBody] CRM.Models.EmailTemplateModel model)
        {
            try
            {
                var appDb = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                model.CreatedOn = DateTime.UtcNow;
                model.UpdatedOn = DateTime.UtcNow;
                appDb.EmailTemplates.Add(model);
                await appDb.SaveChangesAsync();
                return Json(new { success = true, message = "Template created" });
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateTemplate([FromBody] CRM.Models.EmailTemplateModel model)
        {
            try
            {
                var appDb = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var existing = await appDb.EmailTemplates.FindAsync(model.TemplateId);
                if (existing == null) return Json(new { success = false, message = "Template not found" });
                existing.TemplateName = model.TemplateName;
                existing.Subject = model.Subject;
                existing.BodyHtml = model.BodyHtml;
                existing.Variables = model.Variables;
                existing.IsActive = model.IsActive;
                existing.UpdatedOn = DateTime.UtcNow;
                appDb.EmailTemplates.Update(existing);
                await appDb.SaveChangesAsync();
                return Json(new { success = true, message = "Template updated" });
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteTemplate(int templateId)
        {
            try
            {
                var appDb = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var existing = await appDb.EmailTemplates.FindAsync(templateId);
                if (existing == null) return Json(new { success = false, message = "Template not found" });
                appDb.EmailTemplates.Remove(existing);
                await appDb.SaveChangesAsync();
                return Json(new { success = true, message = "Template deleted" });
            }
            catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
        }

        // ==========================================
        // Email Log - View sent emails
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> EmailLog()
        {
            var appDb = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var logs = await appDb.EmailLogs.OrderByDescending(l => l.SentOn).Take(200).ToListAsync();
            return View(logs);
        }

        // ==========================================
        // Compose & Send Email (SuperAdmin)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> ComposeEmail()
        {
            var tenants = await _masterDb.Tenants.Where(t => t.IsActive).ToListAsync();
            
            // Load all tenant users once
            var allUsers = new List<object>();
            try
            {
                var appDb = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var users = await appDb.Users.Where(u => u.IsActive).ToListAsync();
                foreach (var u in users)
                {
                    // Try to map user to tenant via ChannelPartnerId
                    var tenant = !u.ChannelPartnerId.HasValue
                        ? null
                        : tenants.FirstOrDefault(t => t.TenantId == u.ChannelPartnerId.Value);
                    
                    allUsers.Add(new
                    {
                        tenantName = tenant?.CompanyName ?? "Unassigned",
                        tenantId = tenant?.TenantId ?? 0,
                        userId = u.UserId,
                        username = u.Username,
                        email = u.Email ?? "",
                        role = u.Role
                    });
                }
            }
            catch { }

            // Get email templates
            var appDbCtx = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var templates = await appDbCtx.EmailTemplates.Where(t => t.IsActive).ToListAsync();

            ViewBag.TenantUsers = allUsers;
            ViewBag.Templates = templates;
            ViewBag.Tenants = tenants;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> SendEmail(string toEmail, string toName, string subject, string body, int? tenantId, string templateName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(toEmail))
                    return Json(new { success = false, message = "Recipient email is required" });
                if (string.IsNullOrWhiteSpace(subject))
                    return Json(new { success = false, message = "Subject is required" });
                if (string.IsNullOrWhiteSpace(body))
                    return Json(new { success = false, message = "Email body is required" });

                var scopeFactory = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();
                using var scope = scopeFactory.CreateScope();
                var emailService = scope.ServiceProvider.GetRequiredService<EmailService>();

                bool sent = false;
                if (!string.IsNullOrEmpty(templateName))
                {
                    var variables = new Dictionary<string, string>
                    {
                        ["Name"] = toName ?? toEmail,
                        ["Email"] = toEmail,
                        ["CompanyName"] = "PropTech CRM",
                        ["Year"] = DateTime.UtcNow.Year.ToString()
                    };
                    sent = await emailService.SendTemplateEmailAsync(templateName, toEmail, 0, variables, "SuperAdmin");
                }

                if (!sent)
                {
                    await emailService.SendEmailAsync(0, toEmail, subject, body, "Manual", "SuperAdmin");
                }

                return Json(new { success = true, message = "Email sent successfully!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email from SuperAdmin");
                return Json(new { success = false, message = "Failed to send email: " + ex.Message });
            }
        }

        // ==========================================
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetUpgradeOptions(int tenantId)
        {
            try
            {
                var tenant = await _masterDb.Tenants.FindAsync(tenantId);
                if (tenant == null)
                {
                    return Json(new { success = false, message = "Tenant not found" });
                }

                var currentSubscription = await _masterDb.TenantSubscriptions
                    .Where(s => s.TenantId == tenantId && s.Status == "Active")
                    .FirstOrDefaultAsync();
                var availablePlans = await _masterDb.SaasPlans
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder)
            .ToListAsync();
                    
                // Look up plan name separately (MongoDbSet .Include() is a no-op)
                var currentPlanName = "";
                if (currentSubscription != null)
                {
                    var currentPlan = availablePlans.FirstOrDefault(p => p.PlanId == currentSubscription.PlanId);
                    currentPlanName = currentPlan?.PlanName ?? "";
                }

                return Json(new
                {
                    success = true,
                    tenant = new { tenant.TenantId, tenant.CompanyName, tenant.Email, tenant.ContactPerson },
                    currentSubscription = currentSubscription != null ? new
                    {
                        subscriptionId = currentSubscription.SubscriptionId,
                        planName = currentPlanName,
                        billingCycle = currentSubscription.BillingCycle ?? "",
                        amount = currentSubscription.Amount,
                        startDate = currentSubscription.StartDate.ToString("yyyy-MM-dd"),
                        endDate = currentSubscription.EndDate.ToString("yyyy-MM-dd"),
                        daysRemaining = (currentSubscription.EndDate - DateTime.UtcNow).Days,
                        status = currentSubscription.Status ?? ""
                    } : (object?)null,
                    availablePlans = availablePlans.Select(p => new
                    {
                        p.PlanId,
                        p.PlanName,
                        p.Description,
                        p.MonthlyPrice,
                        p.YearlyPrice,
                        p.MaxUsers,
                        p.MaxAgents,
                        p.MaxLeadsPerMonth,
                        p.MaxPartners,
                        p.HasWhatsAppIntegration,
                        p.HasFacebookIntegration,
                        p.HasAdvancedReports,
                        p.HasPrioritySupport,
                        p.HasImpersonation,
                        p.HasLeadScoring,
                        p.HasQuotationManagement,
                        p.HasSiteVisitManagement,
                        p.HasDocumentManagement,
                        p.HasInventoryManagement,
                        p.HasInvoiceAutomation
                    })
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Razorpay Payment Configuration

        [HttpGet]
        public async Task<IActionResult> PaymentConfig()
        {
            var config = await _masterDb.SaasPaymentConfig.FirstOrDefaultAsync(c => c.IsActive);
            
            // If no config in DB, fall back to appsettings.json values so they are visible in the UI
            if (config == null)
            {
                config = new SaasPaymentConfigModel
                {
                    RazorpayKeyId = _config["Razorpay:KeyId"] ?? "",
                    RazorpayKeySecret = _config["Razorpay:KeySecret"] ?? "",
                    RazorpayWebhookSecret = _config["Razorpay:WebhookSecret"] ?? "",
                    IsActive = true
                };
            }
            
            return View(config);
        }

        [HttpPost]
        public async Task<IActionResult> SavePaymentConfig(SaasPaymentConfigModel model)
        {
            try
            {
                var existing = await _masterDb.SaasPaymentConfig.FirstOrDefaultAsync(c => c.IsActive);
                if (existing != null)
                {
                    existing.RazorpayKeyId = model.RazorpayKeyId;
                    existing.RazorpayKeySecret = model.RazorpayKeySecret;
                    existing.RazorpayWebhookSecret = model.RazorpayWebhookSecret;
                    existing.UpdatedOn = DateTime.UtcNow;
                    _masterDb.SaasPaymentConfig.Update(existing);
                }
                else
                {
                    model.CreatedOn = DateTime.UtcNow;
                    model.IsActive = true;
                    _masterDb.SaasPaymentConfig.Add(model);
                }

                await _masterDb.SaveChangesAsync();
                var updatedKeyId = existing?.RazorpayKeyId ?? model.RazorpayKeyId;
                var maskedKey = updatedKeyId?.Length > 6 ? updatedKeyId.Substring(0, 6) + "..." : updatedKeyId;
                await LogAuditAsync(GetCurrentUserId(), "Update", "PaymentConfig", null, $"Key ID: {maskedKey}");
                return Json(new { success = true, message = "Payment configuration saved" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
        // ==========================================
        // API: Get plans for SaaS landing page
        // ==========================================
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetSaasPlans()
        {
            var plans = await _masterDb.SaasPlans
                .Where(p => p.IsActive && p.ShowOnLandingPage == true)
                .OrderBy(p => p.SortOrder)
                .Select(p => new
                {
                    p.PlanId,
                    p.PlanName,
                    p.Description,
                    p.MonthlyPrice,
                    p.YearlyPrice,
                    p.MaxUsers,
                    p.MaxAgents,
                    p.MaxLeadsPerMonth,
                    p.MaxPartners,
                    p.HasWhatsAppIntegration,
                    p.HasFacebookIntegration,
                    p.HasEmailIntegration,
                    p.HasCustomAPIAccess,
                    p.HasAdvancedReports,
                    p.HasCustomBranding,
                    p.HasPrioritySupport,
                    p.HasImpersonation,
                    p.HasLeadScoring,
                    p.HasSiteVisitManagement,
                    p.HasDocumentManagement,
                    p.HasInventoryManagement,
                    p.HasCampaignManagement,
                    p.HasLegalManagement,
                    p.HasInvoiceAutomation,
                    p.HasQuotationManagement,
                    p.HasWorkflowAutomation,
                    p.HasCustomerPortal,
                    p.HasAIScoring,
                    p.HasAIChatbot,
                    p.HasMobileApp,
                    p.HasTwoFactorAuth,
                    p.HasCallIntegration,
                    p.HasSmsIntegration,
                    p.HasMultiLanguage,
                    p.HasGpsTracking,
                    p.MaxSiteVisitsPerMonth,
                    p.MaxDocuments,
                    p.MaxProperties,
                    p.MaxQuotationsPerMonth,
                    p.MaxEmailCampaigns,
                    p.SupportLevel,
                    p.PlanType
                })
                .ToListAsync();

            return Json(new { success = true, plans });
        }

        [HttpGet]
        public async Task<IActionResult> SaasTransactions()
        {
            var transactions = await _masterDb.SaasPaymentTransactions
                .OrderByDescending(t => t.TransactionDate)
                .ToListAsync();

            // Load related data separately (Include is a no-op on MongoDbSet)
            var allTenants = await _masterDb.Tenants.ToListAsync();
            var allSubscriptions = await _masterDb.TenantSubscriptions.ToListAsync();
            var allPlans = await _masterDb.SaasPlans.ToListAsync();

            foreach (var t in transactions)
            {
                t.Tenant = allTenants.FirstOrDefault(te => te.TenantId == t.TenantId);
                if (t.SubscriptionId.HasValue)
                {
                    var sub = allSubscriptions.FirstOrDefault(s => s.SubscriptionId == t.SubscriptionId.Value);
                    if (sub != null)
                    {
                        t.Subscription = sub;
                        sub.Plan = allPlans.FirstOrDefault(p => p.PlanId == sub.PlanId);
                    }
                }
            }

            return View(transactions);
        }
        [AllowAnonymous]

        public IActionResult Terms()
        {
            return View();
        }
        [AllowAnonymous]

        public IActionResult RefundPolicy()
        {
            return View();
        }
        [AllowAnonymous]

        public IActionResult Privacy()
        {
            return View();
        }

        // ==========================================
        // Audit Log
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> AuditLog(string? actionFilter, string? entityType, int page = 1)
        {
            try
            {
                var mongoDbContext = HttpContext.RequestServices.GetRequiredService<MongoDbContext>();
                var appDb = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                
                var auditCollection = mongoDbContext.GetCollection<AuditLogModel>("audit_logs");
                
                // Auto-seed audit logs if empty (first visit)
                var totalExisting = (int)await auditCollection.CountDocumentsAsync(FilterDefinition<AuditLogModel>.Empty);
                if (totalExisting == 0)
                {
                    await AutoSeedAuditLogs(appDb);
                }

                var filterBuilder = new FilterDefinitionBuilder<AuditLogModel>();
                var filter = filterBuilder.Empty;

                if (!string.IsNullOrEmpty(actionFilter))
                    filter = filterBuilder.And(filter, filterBuilder.Eq(a => a.Action, actionFilter));

                if (!string.IsNullOrEmpty(entityType))
                    filter = filterBuilder.And(filter, filterBuilder.Eq(a => a.EntityType, entityType));

                int pageSize = 30;
                int totalCount = (int)await auditCollection.CountDocumentsAsync(filter);
                int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                if (totalPages < 1) totalPages = 1;
                if (page < 1) page = 1;
                if (page > totalPages) page = totalPages;

                var logs = await auditCollection
                    .Find(filter)
                    .SortByDescending(a => a.Timestamp)
                    .Skip((page - 1) * pageSize)
                    .Limit(pageSize)
                    .ToListAsync();

                // Load user info for each log - check both AppDb users and SuperAdmins
                var userIds = logs.Where(l => l.UserId.HasValue).Select(l => l.UserId.Value).Distinct().ToList();
                var users = await appDb.Users.Where(u => userIds.Contains(u.UserId)).ToListAsync();
                var superAdminUsers = await _masterDb.SuperAdmins.Where(s => userIds.Contains(s.Id)).ToListAsync();
                
                // Attach users to matching logs
                foreach (var log in logs)
                {
                    if (log.UserId.HasValue)
                    {
                        log.User = users.FirstOrDefault(u => u.UserId == log.UserId.Value);
                        if (log.User == null)
                        {
                            var sa = superAdminUsers.FirstOrDefault(s => s.Id == log.UserId.Value);
                            if (sa != null)
                            {
                                log.User = new UserModel { Username = $"Super Admin ({sa.FullName})" };
                            }
                        }
                    }
                }

                ViewBag.ActionFilter = actionFilter ?? "";
                ViewBag.EntityType = entityType ?? "";
                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.TotalCount = totalCount;

                // Dynamic filter options from actual data
                ViewBag.AllActions = await (await auditCollection.DistinctAsync<string>("Action", filter)).ToListAsync();
                ViewBag.AllEntityTypes = await (await auditCollection.DistinctAsync<string>("EntityType", filter)).ToListAsync();

                // Compute full counts for stats cards (from filtered query before pagination)
                ViewBag.TotalLogins = await auditCollection.CountDocumentsAsync(filterBuilder.And(filter, filterBuilder.Eq(a => a.Action, "Login")));
                ViewBag.TotalCreations = await auditCollection.CountDocumentsAsync(filterBuilder.And(filter, filterBuilder.Eq(a => a.Action, "Create")));
                ViewBag.TotalUpdates = await auditCollection.CountDocumentsAsync(filterBuilder.And(filter, filterBuilder.Eq(a => a.Action, "Update")));
                ViewBag.TotalDeletions = await auditCollection.CountDocumentsAsync(filterBuilder.And(filter, filterBuilder.Eq(a => a.Action, "Delete")));

                return View(logs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading audit log");
                ViewBag.Error = "Error loading audit log: " + ex.Message;
                return View(new List<AuditLogModel>());
            }
        }

        private async Task AutoSeedAuditLogs(AppDbContext appDb)
        {
            try
            {
                var users = await appDb.Users.ToListAsync();
                var superAdmins = await _masterDb.SuperAdmins.ToListAsync();
                var rng = new Random();
                var auditLogs = new List<AuditLogModel>();
                var actions = new[] { "Login", "Login", "Login", "Create", "Update", "Delete", "View", "View" };
                var entityTypes = new[] { "User", "Lead", "Tenant", "Plan", "Payment", "Ticket", "Lead", "Booking" };

                for (int i = 0; i < 50; i++)
                {
                    var userId = rng.Next(0, 2) == 0
                        ? (users.Count > 0 ? users[rng.Next(users.Count)].UserId : 1)
                        : (superAdmins.Count > 0 ? superAdmins[rng.Next(superAdmins.Count)].Id : 1);
                    var action = actions[rng.Next(actions.Length)];
                    var entityType = entityTypes[rng.Next(entityTypes.Length)];
                    var timestamp = DateTime.UtcNow.AddDays(-rng.Next(0, 60)).AddHours(-rng.Next(0, 24));

                    auditLogs.Add(new AuditLogModel
                    {
                        UserId = userId,
                        Action = action,
                        EntityType = entityType,
                        EntityId = rng.Next(1, 100),
                        IpAddress = $"192.168.{rng.Next(0, 255)}.{rng.Next(1, 255)}",
                        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0",
                        Timestamp = timestamp,
                        NewValues = System.Text.Json.JsonSerializer.Serialize(new { description = $"Auto-seeded {action} on {entityType} #{rng.Next(1, 100)}" })
                    });
                }

                appDb.AuditLogs.AddRange(auditLogs);
                await appDb.SaveChangesAsync();
                _logger.LogInformation("Auto-seeded 50 audit log entries on first visit");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to auto-seed audit logs");
            }
        }

        private async Task EnsureTenantReferralCodes()
        {
            try
            {
                var tenants = await _masterDb.Tenants.OrderBy(t => t.CreatedOn).ToListAsync();
                var seedRng = new Random();
                foreach (var t in tenants)
                {
                    if (string.IsNullOrWhiteSpace(t.Referral))
                    {
                        var prefix = new string(t.CompanyName.Where(char.IsLetter).Take(2).ToArray()).ToUpper();
                        if (prefix.Length < 2) prefix = prefix.PadRight(2, 'X');
                        string code;
                        do
                        {
                            code = prefix + seedRng.Next(1000, 9999).ToString();
                        } while (tenants.Any(t2 => t2.TenantId != t.TenantId && string.Equals(t2.Referral, code, StringComparison.OrdinalIgnoreCase)));
                        t.Referral = code;
                        _masterDb.Tenants.Update(t);
                    }
                }
                await _masterDb.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to ensure tenant referral codes");
            }
        }

        private async Task AutoSeedReferralData()
        {
            try
            {
                // Ensure SaasSetting values exist for referral amounts
                var referrerSetting = await _masterDb.SaasSetting
                    .FirstOrDefaultAsync(s => s.SettingKey == "ReferralReferrerAmount");
                if (referrerSetting == null)
                {
                    _masterDb.SaasSetting.Add(new SaasSettingsModel
                    {
                        SettingKey = "ReferralReferrerAmount",
                        SettingValue = "500",
                        Description = "Amount credited to referrer when their referral joins"
                    });
                }

                var joinerSetting = await _masterDb.SaasSetting
                    .FirstOrDefaultAsync(s => s.SettingKey == "ReferralJoinerAmount");
                if (joinerSetting == null)
                {
                    _masterDb.SaasSetting.Add(new SaasSettingsModel
                    {
                        SettingKey = "ReferralJoinerAmount",
                        SettingValue = "200",
                        Description = "Amount credited to new joiner when they use a referral code"
                    });
                }

                await _masterDb.SaveChangesAsync();

                // Re-fetch now that defaults are saved
                var referrerAmountStr = (await _masterDb.SaasSetting
                    .FirstOrDefaultAsync(s => s.SettingKey == "ReferralReferrerAmount"))?.SettingValue ?? "500";
                var joinerAmountStr = (await _masterDb.SaasSetting
                    .FirstOrDefaultAsync(s => s.SettingKey == "ReferralJoinerAmount"))?.SettingValue ?? "200";

                decimal.TryParse(referrerAmountStr, out var referrerAmount);
                decimal.TryParse(joinerAmountStr, out var joinerAmount);

                // Ensure every tenant has a referral code so the referral chain can be built
                await EnsureTenantReferralCodes();

                var allTenants = await _masterDb.Tenants.OrderBy(t => t.CreatedOn).ToListAsync();
                var now = DateTime.UtcNow;
                var earnings = new List<ReferralEarningModel>();

                // Pair up tenants: each tenant (except the first) is referred by the previous one
                // This creates a realistic referral chain
                for (int i = 0; i < allTenants.Count; i++)
                {
                    var tenant = allTenants[i];

                    // Referrer earning: crediting each tenant for being a referrer
                    if (referrerAmount > 0 && !string.IsNullOrEmpty(tenant.Referral))
                    {
                        // Check if the tenant was referred by someone (previous tenant in list)
                        if (i > 0)
                        {
                            var referrerTenant = allTenants[i - 1];
                            if (referrerTenant.TenantId != tenant.TenantId)
                            {
                                earnings.Add(new ReferralEarningModel
                                {
                                    TenantId = referrerTenant.TenantId,
                                    ReferralCode = referrerTenant.Referral,
                                    Type = "Referrer",
                                    Amount = referrerAmount,
                                    Description = $"Referral bonus for referring {tenant.CompanyName}",
                                    ReferredTenantId = tenant.TenantId,
                                    IsUsed = false,
                                    CreatedOn = tenant.CreatedOn.AddMinutes(-5)
                                });
                            }
                        }

                        // Joiner earning: credit each tenant (except the first) as a joiner
                        if (i > 0 && joinerAmount > 0)
                        {
                            earnings.Add(new ReferralEarningModel
                            {
                                TenantId = tenant.TenantId,
                                ReferralCode = allTenants[i - 1].Referral,
                                Type = "Joiner",
                                Amount = joinerAmount,
                                Description = $"Welcome bonus for joining via {allTenants[i - 1].CompanyName} referral",
                                ReferredTenantId = tenant.TenantId,
                                IsUsed = false,
                                CreatedOn = tenant.CreatedOn
                            });
                        }
                    }
                }

                if (earnings.Count > 0)
                {
                    _masterDb.ReferralEarnings.AddRange(earnings);
                    await _masterDb.SaveChangesAsync();
                    _logger.LogInformation($"Auto-seeded {earnings.Count} referral earnings entries");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to auto-seed referral data");
            }
        }

        // ==========================================
        // User Management (SuperAdmin can create/manage users for any tenant)
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> Users(int? tenantId)
        {
            var tenants = await _masterDb.Tenants.Where(t => t.IsActive).OrderBy(t => t.CompanyName).ToListAsync();
            ViewBag.Tenants = tenants;

            if (tenantId.HasValue)
            {
                var appDb = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var users = await appDb.Users.Where(u => u.TenantId == tenantId.Value && u.IsActive).ToListAsync();
                var userProfiles = await appDb.UserProfiles.Where(p => users.Select(u => u.UserId).Contains(p.UserId)).ToListAsync();
                ViewBag.CurrentTenant = tenants.FirstOrDefault(t => t.TenantId == tenantId.Value);
                return View(users);
            }

            return View(new List<UserModel>());
        }

        [HttpGet]
        public async Task<IActionResult> GetTenantUsers(int tenantId)
        {
            try
            {
                var appDb = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var users = await appDb.Users.Where(u => u.TenantId == tenantId && u.IsActive).ToListAsync();
                var tenant = await _masterDb.Tenants.FindAsync(tenantId);
                return Json(new
                {
                    success = true,
                    tenantName = tenant?.CompanyName ?? "",
                    users = users.Select(u => new
                    {
                        u.UserId, u.Username, u.Email, u.Phone, u.Role, u.IsActive, u.CreatedDate
                    })
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateTenantUser(int tenantId, string username, string email, string phone, string role, string password)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email))
                    return Json(new { success = false, message = "Username and Email are required" });

                var appDb = HttpContext.RequestServices.GetRequiredService<AppDbContext>();

                // Check duplicates
                if (await appDb.Users.AnyAsync(u => u.Email == email))
                    return Json(new { success = false, message = "Email already exists" });
                if (await appDb.Users.AnyAsync(u => u.Username == username && u.TenantId == tenantId))
                    return Json(new { success = false, message = "Username already exists in this tenant" });

                var tenant = await _masterDb.Tenants.FindAsync(tenantId);
                if (tenant == null)
                    return Json(new { success = false, message = "Tenant not found" });

                var nextUserId = 1;
                if (await appDb.Users.AnyAsync())
                    nextUserId = (await appDb.Users.MaxAsync(u => (int?)u.UserId) ?? 0) + 1;

                var pwHash = PasswordHelper.HashPassword(string.IsNullOrWhiteSpace(password) ? "Test@123" : password);

                var user = new UserModel
                {
                    UserId = nextUserId,
                    Username = username,
                    Email = email,
                    Phone = phone ?? "",
                    Password = pwHash,
                    Role = role ?? "Agent",
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow,
                    TenantId = tenantId
                };
                appDb.Users.Add(user);

                appDb.UserProfiles.Add(new UserProfile
                {
                    UserId = nextUserId,
                    Username = username,
                    Email = email,
                    FirstName = username,
                    LastName = role ?? "",
                    PhoneNumber = phone ?? "",
                    Country = "India",
                    TenantId = tenantId
                });

                await appDb.SaveChangesAsync();

                // Update email directory via MasterDbContext
                _masterDb.EmailDirectory.Add(new MasterDb.Models.EmailDirectoryModel
                {
                    Email = email,
                    TenantId = tenantId
                });
                await _masterDb.SaveChangesAsync();

                await LogAuditAsync(GetCurrentUserId(), "Create", "User", nextUserId,
                    $"User '{username}' ({role}) created under '{tenant.CompanyName}'");

                return Json(new
                {
                    success = true,
                    message = $"User '{username}' created under '{tenant.CompanyName}' with password: {(string.IsNullOrWhiteSpace(password) ? "Test@123" : password)}"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateTenantUser(int userId, string username, string email, string phone, string role, bool isActive, string? password)
        {
            try
            {
                var appDb = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var user = await appDb.Users.FindAsync(userId);
                if (user == null)
                    return Json(new { success = false, message = "User not found" });

                user.Username = username ?? user.Username;
                user.Email = email ?? user.Email;
                user.Phone = phone ?? user.Phone;
                user.Role = role ?? user.Role;
                user.IsActive = isActive;

                if (!string.IsNullOrWhiteSpace(password))
                    user.Password = PasswordHelper.HashPassword(password);

                appDb.Users.Update(user);
                await appDb.SaveChangesAsync();
                await LogAuditAsync(GetCurrentUserId(), "Update", "User", userId,
                    $"User '{username}' updated (role: {role})");

                return Json(new { success = true, message = "User updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteTenantUser(int userId)
        {
            try
            {
                var appDb = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var user = await appDb.Users.FindAsync(userId);
                if (user == null)
                    return Json(new { success = false, message = "User not found" });

                user.IsActive = false;
                appDb.Users.Update(user);
                await appDb.SaveChangesAsync();
                await LogAuditAsync(GetCurrentUserId(), "Delete", "User", userId,
                    $"User '{user.Username}' deactivated");

                return Json(new { success = true, message = "User deactivated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // =============================================
        // Referral System
        // =============================================
        // ==========================================
        // Audit Log Helper
        // ==========================================
        private async Task LogAuditAsync(int? userId, string action, string entityType, int? entityId, string? details = null)
        {
            try
            {
                var appDb = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                appDb.AuditLogs.Add(new AuditLogModel
                {
                    UserId = userId ?? 0,
                    Action = action,
                    EntityType = entityType,
                    EntityId = entityId,
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = Request.Headers["User-Agent"].ToString(),
                    Timestamp = DateTime.UtcNow,
                    NewValues = details != null ? System.Text.Json.JsonSerializer.Serialize(new { description = details }) : null
                });
                await appDb.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write audit log: {Action} {EntityType}", action, entityType);
            }
        }

        private int? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst("UserId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out int id))
                return id;
            return null;
        }

        private async Task CreditReferralEarnings(TenantModel newTenant, string? referralCode)
        {
            try
            {
                // If no code passed directly, check if there's an inquiry with a referral code for this email
                if (string.IsNullOrWhiteSpace(referralCode) && !string.IsNullOrEmpty(newTenant.Email))
                {
                    var inquiry = await _masterDb.Inquiries
                        .FirstOrDefaultAsync(i => i.Email == newTenant.Email && !string.IsNullOrEmpty(i.ReferralCode));
                    referralCode = inquiry?.ReferralCode;
                }

                if (string.IsNullOrWhiteSpace(referralCode)) return;

                // Find the referrer tenant
                var referrerTenant = await _masterDb.Tenants
                    .FirstOrDefaultAsync(t => t.Referral == referralCode);

                if (referrerTenant == null || referrerTenant.TenantId == newTenant.TenantId) return;

                // Get amounts from SaasSettings
                var referrerAmountSetting = await _masterDb.SaasSetting
                    .FirstOrDefaultAsync(s => s.SettingKey == "ReferralReferrerAmount");
                var joinerAmountSetting = await _masterDb.SaasSetting
                    .FirstOrDefaultAsync(s => s.SettingKey == "ReferralJoinerAmount");

                decimal referrerAmount = 0, joinerAmount = 0;
                if (referrerAmountSetting != null) decimal.TryParse(referrerAmountSetting.SettingValue, out referrerAmount);
                if (joinerAmountSetting != null) decimal.TryParse(joinerAmountSetting.SettingValue, out joinerAmount);

                if (referrerAmount > 0)
                {
                    _masterDb.ReferralEarnings.Add(new ReferralEarningModel
                    {
                        TenantId = referrerTenant.TenantId,
                        ReferralCode = referralCode,
                        Type = "Referrer",
                        Amount = referrerAmount,
                        Description = $"Referral bonus: {newTenant.CompanyName} joined using your code",
                        ReferredTenantId = newTenant.TenantId,
                        CreatedOn = DateTime.UtcNow
                    });
                }

                if (joinerAmount > 0)
                {
                    _masterDb.ReferralEarnings.Add(new ReferralEarningModel
                    {
                        TenantId = newTenant.TenantId,
                        ReferralCode = referralCode,
                        Type = "Joiner",
                        Amount = joinerAmount,
                        Description = $"Welcome bonus: Joined using referral code {referralCode}",
                        ReferredTenantId = referrerTenant.TenantId,
                        CreatedOn = DateTime.UtcNow
                    });
                }

                await _masterDb.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error crediting referral earnings");
            }
        }

        // ==========================================
        // Seed Audit Log Data
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> SeedAuditLogs()
        {
            try
            {
                var appDb = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var existingCount = await appDb.AuditLogs.CountAsync();
                if (existingCount > 0)
                    return Json(new { success = true, message = $"Audit logs already seeded ({existingCount} entries exist)" });

                await AutoSeedAuditLogs(appDb);
                return Json(new { success = true, message = "50 audit log entries seeded successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error seeding audit logs");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ==========================================
        // Seed Page Permissions (Modules, Pages, Permissions)
        // ==========================================
        [HttpPost]
        public async Task<IActionResult> SeedPagePermissions()
        {
            try
            {
                var appDb = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                
                // Check if modules already exist
                var existingModules = await appDb.Modules.CountAsync();
                if (existingModules > 0)
                {
                    return Json(new { success = true, message = $"Page permissions already seeded ({existingModules} modules exist)" });
                }

                // Seed modules
                var modules = new List<ModuleModel>
                {
                    new() { ModuleName = "Dashboard", Icon = "activity", SortOrder = 1, IsActive = true },
                    new() { ModuleName = "Leads & Properties", Icon = "folder", SortOrder = 2, IsActive = true },
                    new() { ModuleName = "Sales", Icon = "shopping-cart", SortOrder = 3, IsActive = true },
                    new() { ModuleName = "Finance", Icon = "credit-card", SortOrder = 4, IsActive = true },
                    new() { ModuleName = "Team Management", Icon = "users", SortOrder = 5, IsActive = true },
                    new() { ModuleName = "Attendance", Icon = "calendar", SortOrder = 6, IsActive = true },
                    new() { ModuleName = "Payouts", Icon = "credit-card", SortOrder = 7, IsActive = true },
                    new() { ModuleName = "User Management", Icon = "settings", SortOrder = 8, IsActive = true },
                    new() { ModuleName = "Settings", Icon = "sliders", SortOrder = 9, IsActive = true },
                    new() { ModuleName = "Subscriptions", Icon = "layers", SortOrder = 10, IsActive = true },
                    new() { ModuleName = "Support", Icon = "help-circle", SortOrder = 11, IsActive = true }
                };
                appDb.Modules.AddRange(modules);
                await appDb.SaveChangesAsync();

                // Seed pages for each module
                var pages = new List<PageModel>
                {
                    // Dashboard pages
                    new() { ModuleId = modules[0].ModuleId, PageName = "Dashboard Home", Controller = "Home", Action = "Index", SortOrder = 1, IsActive = true },
                    new() { ModuleId = modules[0].ModuleId, PageName = "Sales Overview", Controller = "Home", Action = "SalesOverview", SortOrder = 2, IsActive = true },
                    new() { ModuleId = modules[0].ModuleId, PageName = "Team Dashboard", Controller = "Home", Action = "TeamDashboard", SortOrder = 3, IsActive = true },
                    
                    // Leads pages
                    new() { ModuleId = modules[1].ModuleId, PageName = "Leads", Controller = "Leads", Action = "Index", SortOrder = 1, IsActive = true },
                    new() { ModuleId = modules[1].ModuleId, PageName = "Sales Pipeline", Controller = "SalesPipelines", Action = "Index", SortOrder = 2, IsActive = true },
                    new() { ModuleId = modules[1].ModuleId, PageName = "Tasks", Controller = "Tasks", Action = "Index", SortOrder = 3, IsActive = true },
                    new() { ModuleId = modules[1].ModuleId, PageName = "Unassigned Leads", Controller = "WebhookLeads", Action = "Index", SortOrder = 4, IsActive = true },
                    new() { ModuleId = modules[1].ModuleId, PageName = "Properties", Controller = "Properties", Action = "Index", SortOrder = 5, IsActive = true },
                    
                    // Sales pages
                    new() { ModuleId = modules[2].ModuleId, PageName = "Quotations", Controller = "Quotations", Action = "Index", SortOrder = 1, IsActive = true },
                    new() { ModuleId = modules[2].ModuleId, PageName = "Bookings", Controller = "Bookings", Action = "Index", SortOrder = 2, IsActive = true },
                    new() { ModuleId = modules[2].ModuleId, PageName = "Invoices", Controller = "Invoices", Action = "Index", SortOrder = 3, IsActive = true },
                    new() { ModuleId = modules[2].ModuleId, PageName = "Payments", Controller = "Payments", Action = "Index", SortOrder = 4, IsActive = true },
                    
                    // Finance pages
                    new() { ModuleId = modules[3].ModuleId, PageName = "Expenses", Controller = "Expenses", Action = "Index", SortOrder = 1, IsActive = true },
                    new() { ModuleId = modules[3].ModuleId, PageName = "Revenue", Controller = "Revenue", Action = "Index", SortOrder = 2, IsActive = true },
                    new() { ModuleId = modules[3].ModuleId, PageName = "Profit", Controller = "Profit", Action = "Index", SortOrder = 3, IsActive = true },
                    
                    // Team pages
                    new() { ModuleId = modules[4].ModuleId, PageName = "Agent List", Controller = "Agent", Action = "List", SortOrder = 1, IsActive = true },
                    new() { ModuleId = modules[4].ModuleId, PageName = "Channel Partner", Controller = "ManageUsers", Action = "PartnerApproval", SortOrder = 2, IsActive = true },
                    
                    // Attendance pages
                    new() { ModuleId = modules[5].ModuleId, PageName = "My Attendance", Controller = "Attendance", Action = "Calendar", SortOrder = 1, IsActive = true },
                    new() { ModuleId = modules[5].ModuleId, PageName = "Agent Attendance", Controller = "Attendance", Action = "AgentList", SortOrder = 2, IsActive = true },
                    
                    // Payout pages
                    new() { ModuleId = modules[6].ModuleId, PageName = "Agent Payouts", Controller = "AgentPayout", Action = "Index", SortOrder = 1, IsActive = true },
                    new() { ModuleId = modules[6].ModuleId, PageName = "Partner Payouts", Controller = "PartnerCommission", Action = "Index", SortOrder = 2, IsActive = true },
                    
                    // User Management pages
                    new() { ModuleId = modules[7].ModuleId, PageName = "Manage Users", Controller = "ManageUsers", Action = "Index", SortOrder = 1, IsActive = true },
                    new() { ModuleId = modules[7].ModuleId, PageName = "Roles Management", Controller = "ManageUsers", Action = "Roles", SortOrder = 2, IsActive = true },
                    
                    // Settings pages
                    new() { ModuleId = modules[8].ModuleId, PageName = "System Settings", Controller = "Settings", Action = "Index", SortOrder = 1, IsActive = true },
                    new() { ModuleId = modules[8].ModuleId, PageName = "Branding", Controller = "Settings", Action = "Branding", SortOrder = 2, IsActive = true },
                    new() { ModuleId = modules[8].ModuleId, PageName = "My Profile", Controller = "Profile", Action = "Index", SortOrder = 3, IsActive = true },
                    
                    // Subscription pages
                    new() { ModuleId = modules[9].ModuleId, PageName = "My Plan", Controller = "SaasSubscription", Action = "MyPlan", SortOrder = 1, IsActive = true },
                    new() { ModuleId = modules[9].ModuleId, PageName = "Transactions", Controller = "SaasSubscription", Action = "Transactions", SortOrder = 2, IsActive = true },
                    
                    // Support pages
                    new() { ModuleId = modules[10].ModuleId, PageName = "Support Tickets", Controller = "Ticket", Action = "Index", SortOrder = 1, IsActive = true },
                    new() { ModuleId = modules[10].ModuleId, PageName = "Help Center", Controller = "Home", Action = "HelpCenter", SortOrder = 2, IsActive = true }
                };
                appDb.Pages.AddRange(pages);
                await appDb.SaveChangesAsync();

                // Seed permissions
                var permNames = new[] { "View", "Create", "Edit", "Delete", "Export", "BulkUpload" };
                var existingPerms = await appDb.Permissions.CountAsync();
                if (existingPerms == 0)
                {
                    var permissions = permNames.Select((name, idx) => new PermissionModel
                    {
                        PermissionName = name,
                        SortOrder = idx + 1,
                        IsActive = true
                    }).ToList();
                    appDb.Permissions.AddRange(permissions);
                    await appDb.SaveChangesAsync();
                }

                _logger.LogInformation($"Seeded {modules.Count} modules, {pages.Count} pages, and permissions");
                return Json(new { success = true, message = $"Seeded {modules.Count} modules, {pages.Count} pages successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error seeding page permissions");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ==========================================
        // Dashboard badge counts for sidebar
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> GetTenantCount()
        {
            try
            {
                var count = await _masterDb.Tenants.CountAsync();
                return Json(new { count });
            }
            catch (Exception ex)
            {
                return Json(new { count = 0, error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetInquiryCount()
        {
            try
            {
                var count = await _masterDb.Inquiries.CountAsync(i => i.Status == "New");
                return Json(new { count });
            }
            catch (Exception ex)
            {
                return Json(new { count = 0, error = ex.Message });
            }
        }

        // API: Get referral wallet balance for a tenant
        [HttpPost]
        public async Task<IActionResult> ProcessRefund(int transactionId)
        {
            using var dbTransaction = await _masterDb.Database.BeginTransactionAsync();

            try
            {
                var cancellationTransaction = await _masterDb.SaasPaymentTransactions
                    .FirstOrDefaultAsync(x => x.TransactionId == transactionId);

                if (cancellationTransaction == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Transaction not found."
                    });
                }

                if (cancellationTransaction.RefundStatus == "Refunded")
                {
                    return Json(new
                    {
                        success = false,
                        message = "Refund already processed."
                    });
                }

                var tenant = await _masterDb.Tenants
                    .FirstOrDefaultAsync(t => t.TenantId == cancellationTransaction.TenantId);

                if (tenant == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Tenant not found."
                    });
                }

                // Original successful payment
                //var paymentTransaction = await _masterDb.SaasPaymentTransactions
                //    .Where(x =>
                //x.TenantId == cancellationTransaction.TenantId &&
                //        x.Status == "Success" &&
                //        x.RazorpayPaymentId != null)
                //    .OrderByDescending(x => x.TransactionDate)
                //    .FirstOrDefaultAsync();

                var paymentTransaction = await _masterDb.SaasPaymentTransactions
                .Where(x =>
                    x.SubscriptionId == cancellationTransaction.SubscriptionId && // ? FIX
                    x.TenantId == cancellationTransaction.TenantId &&
                    x.Status == "Success" &&
                    x.TransactionType != "Refund" &&
                    x.TransactionType != "Cancellation" &&
                    x.RazorpayPaymentId != null &&
                    x.RazorpayPaymentId.StartsWith("pay_"))
                .OrderByDescending(x => x.TransactionDate)
                .FirstOrDefaultAsync();

                decimal razorpayPaidAmount = paymentTransaction?.NetAmount ?? 0m;

                // Total plan amount
                decimal totalAmount = cancellationTransaction.Amount;

                // Reward points used
                decimal rewardPointsUsed = Math.Max(0, totalAmount - razorpayPaidAmount);

                string? refundId = null;

                // Refund Razorpay only if actual payment exists
                if (razorpayPaidAmount > 0)
                {
                    var saasConfig = await _masterDb.SaasPaymentConfig
                        .FirstOrDefaultAsync(x => x.IsActive);

                    if (saasConfig == null)
                    {
                        return Json(new
                        {
                            success = false,
                            message = "Payment configuration not found."
                        });
                    }

                    using var httpClient = new HttpClient();

                    httpClient.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue(
                            "Basic",
                            Convert.ToBase64String(
                                System.Text.Encoding.UTF8.GetBytes(
                                    $"{saasConfig.RazorpayKeyId}:{saasConfig.RazorpayKeySecret}")));

                    var refundPayload = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        amount = (int)(razorpayPaidAmount * 100)
                    });

                    var response = await httpClient.PostAsync(
                        $"https://api.razorpay.com/v1/payments/{paymentTransaction!.RazorpayPaymentId}/refund",
                        new StringContent(
                            refundPayload,
                            System.Text.Encoding.UTF8,
                            "application/json"));

                    var responseContent = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {


                        return Json(new
                        {
                            success = false,
                            message = responseContent
                        });
                    }

                    var jsonDoc = System.Text.Json.JsonDocument.Parse(responseContent);

                    refundId = jsonDoc.RootElement
                        .GetProperty("id")
                        .GetString();
                }

                if (rewardPointsUsed > 0)
                {
                    _masterDb.ReferralEarnings.Add(new ReferralEarningModel
                    {
                        TenantId = cancellationTransaction.TenantId,
                        ReferralCode = tenant.Referral,
                        Type = "Refund",
                        Amount = rewardPointsUsed,
                        Description = $"Referral credits restored due to subscription cancellation refund",
                        IsUsed = false,
                        CreatedOn = DateTime.UtcNow
                    });
                }

                cancellationTransaction.RefundStatus = "Refunded";
                cancellationTransaction.RefundDate = DateTime.UtcNow;
                cancellationTransaction.RefundId = refundId;
                cancellationTransaction.Status = "Success";
                cancellationTransaction.Description =
    cancellationTransaction.Description?.Replace("Refund Pending", "Refund Completed");
                // ? Update refund transaction mapping
                //cancellationTransaction.RazorpayPaymentId = paymentTransaction?.RazorpayPaymentId;

                // ? Update subscription for admin
                var subscription = await _masterDb.TenantSubscriptions
                    .FirstOrDefaultAsync(s => s.SubscriptionId == cancellationTransaction.SubscriptionId);

                if (subscription != null)
                {
                    subscription.CancellationReason = subscription.CancellationReason
                        ?.Replace("refund pending", "Refund Completed");

                    subscription.UpdatedOn = DateTime.UtcNow;
                }

                await _masterDb.SaveChangesAsync();
                await dbTransaction.CommitAsync();
                await LogAuditAsync(GetCurrentUserId(), "Update", "Payment", cancellationTransaction.TransactionId, $"Refund processed for {tenant.CompanyName}. Amount: ?{razorpayPaidAmount:N2}, Rewards: ?{rewardPointsUsed:N2}");

                return Json(new
                {
                    success = true,
                    message = $"Refund completed. Razorpay: ?{razorpayPaidAmount:N2}, Reward Points Restored: ?{rewardPointsUsed:N2}"
                });
            }
            catch (Exception ex)
            {
                await dbTransaction.RollbackAsync();

                return Json(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }


        [HttpGet]
        public async Task<IActionResult> ExportDbJson()
        {
            try
            {
                var appDb = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var masterDb = HttpContext.RequestServices.GetRequiredService<MasterDbContext>();
                var result = new Dictionary<string, object>();

                var collections = new Dictionary<string, Func<Task<object>>>
                {
                    ["users"] = async () => await GetDocumentsAsync(appDb.Users),
                    ["leads"] = async () => await GetDocumentsAsync(appDb.Leads),
                    ["properties"] = async () => await GetDocumentsAsync(appDb.Properties),
                    ["bookings"] = async () => await GetDocumentsAsync(appDb.Bookings),
                    ["payments"] = async () => await GetDocumentsAsync(appDb.Payments),
                    ["quotations"] = async () => await GetDocumentsAsync(appDb.Quotations),
                    ["invoices"] = async () => await GetDocumentsAsync(appDb.Invoices),
                    ["notifications"] = async () => await GetDocumentsAsync(appDb.Notifications),
                    ["followups"] = async () => await GetDocumentsAsync(appDb.FollowUps),
                    ["agents"] = async () => await GetDocumentsAsync(appDb.Agents),
                    ["agent_documents"] = async () => await GetDocumentsAsync(appDb.AgentDocuments),
                    ["channel_partners"] = async () => await GetDocumentsAsync(appDb.ChannelPartners),
                    ["channel_partner_documents"] = async () => await GetDocumentsAsync(appDb.ChannelPartnerDocuments),
                    ["settings"] = async () => await GetDocumentsAsync(appDb.Settings),
                    ["branding"] = async () => await GetDocumentsAsync(appDb.Branding),
                    ["expenses"] = async () => await GetDocumentsAsync(appDb.Expenses),
                    ["revenues"] = async () => await GetDocumentsAsync(appDb.Revenues),
                    ["role_permissions"] = async () => await GetDocumentsAsync(appDb.RolePermissions),
                    ["permissions"] = async () => await GetDocumentsAsync(appDb.Permissions),
                    ["lead_logs"] = async () => await GetDocumentsAsync(appDb.LeadLogs),
                    ["lead_notes"] = async () => await GetDocumentsAsync(appDb.LeadNotes),
                    ["lead_histories"] = async () => await GetDocumentsAsync(appDb.LeadHistories),
                    ["webhook_leads"] = async () => await GetDocumentsAsync(appDb.WebhookLeads),
                    ["email_templates"] = async () => await GetDocumentsAsync(appDb.EmailTemplates),
                    ["email_settings"] = async () => await GetDocumentsAsync(appDb.EmailSettings),
                    ["email_logs"] = async () => await GetDocumentsAsync(appDb.EmailLogs),
                    ["user_profiles"] = async () => await GetDocumentsAsync(appDb.UserProfiles),
                    ["payment_transactions"] = async () => await GetDocumentsAsync(appDb.PaymentTransactions),
                    ["payment_plans"] = async () => await GetDocumentsAsync(appDb.PaymentPlans),
                    ["property_flats"] = async () => await GetDocumentsAsync(appDb.PropertyFlats),
                    ["property_documents"] = async () => await GetDocumentsAsync(appDb.PropertyDocuments),
                    ["property_gallery"] = async () => await GetDocumentsAsync(appDb.PropertyGallery),
                    ["booking_amendments"] = async () => await GetDocumentsAsync(appDb.BookingAmendments),
                    ["builders"] = async () => await GetDocumentsAsync(appDb.Builders),
                    ["agent_attendances"] = async () => await GetDocumentsAsync(appDb.AgentAttendances),
                    ["agent_payouts"] = async () => await GetDocumentsAsync(appDb.AgentPayouts),
                    ["agent_commission_logs"] = async () => await GetDocumentsAsync(appDb.AgentCommissionLogs),
                    ["channel_partner_commission_logs"] = async () => await GetDocumentsAsync(appDb.ChannelPartnerCommissionLogs),
                    ["partner_commissions"] = async () => await GetDocumentsAsync(appDb.PartnerCommissions),
                    ["partner_leads"] = async () => await GetDocumentsAsync(appDb.PartnerLeads),
                    ["partner_payouts"] = async () => await GetDocumentsAsync(appDb.PartnerPayouts),
                    ["partner_subscriptions"] = async () => await GetDocumentsAsync(appDb.PartnerSubscriptions),
                    ["subscription_plans"] = async () => await GetDocumentsAsync(appDb.SubscriptionPlans),
                    ["subscription_addons"] = async () => await GetDocumentsAsync(appDb.SubscriptionAddons),
                    ["lead_integration_configs"] = async () => await GetDocumentsAsync(appDb.LeadIntegrationConfigs),
                    ["leave_requests"] = async () => await GetDocumentsAsync(appDb.LeaveRequests),
                    ["attendance_logs"] = async () => await GetDocumentsAsync(appDb.AttendanceLogs),
                    ["audit_logs"] = async () => await GetDocumentsAsync(appDb.AuditLogs),
                    ["user_favorites"] = async () => await GetDocumentsAsync(appDb.UserFavorites),
                    ["user_recent_searches"] = async () => await GetDocumentsAsync(appDb.UserRecentSearches),
                    ["user_settings"] = async () => await GetDocumentsAsync(appDb.UserSettings),
                    ["user_dashboard_settings"] = async () => await GetDocumentsAsync(appDb.UserDashboardSettings),
                    ["project_interests"] = async () => await GetDocumentsAsync(appDb.ProjectInterests),
                    ["duplicate_leads"] = async () => await GetDocumentsAsync(appDb.DuplicateLeads),
                    ["role_page_permissions"] = async () => await GetDocumentsAsync(appDb.RolePagePermissions),
                    ["modules"] = async () => await GetDocumentsAsync(appDb.Modules),
                    ["pages"] = async () => await GetDocumentsAsync(appDb.Pages),
                    ["property_agents"] = async () => await GetDocumentsAsync(appDb.PropertyAgents),
                    ["property_histories"] = async () => await GetDocumentsAsync(appDb.PropertyHistories),
                    ["property_uploads"] = async () => await GetDocumentsAsync(appDb.PropertyUploads),
                    ["lead_uploads"] = async () => await GetDocumentsAsync(appDb.LeadUploads),
                    ["lead_handover_audits"] = async () => await GetDocumentsAsync(appDb.LeadHandoverAudits),
                    ["notification_preferences"] = async () => await GetDocumentsAsync(appDb.NotificationPreferences),
                    ["referral_earnings"] = async () => await GetDocumentsAsync(appDb.ReferralEarnings),
                    ["testimonials"] = async () => await GetDocumentsAsync(appDb.Testimonials),
                    ["bank_accounts"] = async () => await GetDocumentsAsync(appDb.BankAccounts),
                    ["payment_gateways"] = async () => await GetDocumentsAsync(appDb.PaymentGateways),
                    ["quotation_items"] = async () => await GetDocumentsAsync(appDb.QuotationItems),
                    ["invoice_items"] = async () => await GetDocumentsAsync(appDb.InvoiceItems),
                    ["booking_documents"] = async () => await GetDocumentsAsync(appDb.BookingDocuments),
                    ["payment_installments"] = async () => await GetDocumentsAsync(appDb.PaymentInstallments),
                    ["webhook_retry_queue"] = async () => await GetDocumentsAsync(appDb.WebhookRetryQueue),
                    ["whatsapp_logs"] = async () => await GetDocumentsAsync(appDb.WhatsAppLogs),
                    ["support_tickets"] = async () => await GetDocumentsAsync(appDb.Tickets),
                    ["site_visits"] = async () => await GetDocumentsAsync(appDb.SiteVisits),
                    ["lead_scores"] = async () => await GetDocumentsAsync(appDb.LeadScores),
                    ["campaigns"] = async () => await GetDocumentsAsync(appDb.Campaigns),
                    ["legal_cases"] = async () => await GetDocumentsAsync(appDb.LegalCases),
                    ["inventory_units"] = async () => await GetDocumentsAsync(appDb.InventoryUnits),
                    ["chatbot_conversations"] = async () => await GetDocumentsAsync(appDb.ChatbotConversations),
                    ["chatbot_messages"] = async () => await GetDocumentsAsync(appDb.ChatbotMessages),
                    ["chatbot_knowledge"] = async () => await GetDocumentsAsync(appDb.ChatbotKnowledge),
                    ["chat_sessions"] = async () => await GetDocumentsAsync(appDb.ChatSessions),
                    ["chat_logs"] = async () => await GetDocumentsAsync(appDb.ChatLogs),
                    ["chat_intents"] = async () => await GetDocumentsAsync(appDb.ChatIntents),
                    ["chatbot_settings"] = async () => await GetDocumentsAsync(appDb.ChatbotSettings),
                    ["chat_agents"] = async () => await GetDocumentsAsync(appDb.ChatAgents),
                    ["tenants"] = async () => await GetDocumentsAsync(masterDb.Tenants),
                    ["super_admins"] = async () => await GetDocumentsAsync(masterDb.SuperAdmins),
                    ["saas_plans"] = async () => await GetDocumentsAsync(masterDb.SaasPlans),
                    ["saas_settings"] = async () => await GetDocumentsAsync(masterDb.SaasSettings),
                    ["tenant_subscriptions"] = async () => await GetDocumentsAsync(masterDb.TenantSubscriptions),
                    ["payment_transactions_saas"] = async () => await GetDocumentsAsync(masterDb.SaasPaymentTransactions),
                    ["inquiries"] = async () => await GetDocumentsAsync(masterDb.Inquiries),
                    ["referral_earnings_master"] = async () => await GetDocumentsAsync(masterDb.ReferralEarnings)
                };

                foreach (var (name, factory) in collections)
                {
                    try
                    {
                        result[name] = await factory();
                    }
                    catch (Exception ex)
                    {
                        result[name] = new { error = ex.Message };
                    }
                }

                var json = System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                var fileName = $"crm_export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
                return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", fileName);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private async Task<List<object>> GetDocumentsAsync<T>(MongoDbSet<T> set) where T : class
        {
            var list = await set.ToListAsync();
            return list.Cast<object>().ToList();
        }

        [HttpGet]
        public async Task<IActionResult> ResetAllPasswords()
        {
            try
            {
                var appDb = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var masterDb = HttpContext.RequestServices.GetRequiredService<MasterDbContext>();
                var results = new List<object>();

                var defaultPassword = "Reset@123";
                var hashedPassword = PasswordHelper.HashPassword(defaultPassword);

                var users = await appDb.Users.ToListAsync();
                foreach (var user in users)
                {
                    var oldHash = user.Password;
                    user.Password = hashedPassword;
                    appDb.Users.Update(user);
                    results.Add(new { userId = user.UserId, username = user.Username, email = user.Email, role = user.Role, password = defaultPassword, oldHashLength = oldHash?.Length ?? 0 });
                }
                await appDb.SaveChangesAsync();

                var superAdmins = await masterDb.SuperAdmins.ToListAsync();
                foreach (var sa in superAdmins)
                {
                    var oldHash = sa.PasswordHash;
                    sa.PasswordHash = hashedPassword;
                    masterDb.SuperAdmins.Update(sa);
                    results.Add(new { superAdminId = sa.Id, name = sa.FullName, email = sa.Email, role = "SuperAdmin", password = defaultPassword, oldHashLength = oldHash?.Length ?? 0 });
                }
                await masterDb.SaveChangesAsync();

                await LogAuditAsync(GetCurrentUserId(), "Update", "AllUsers", null, "Reset all user passwords to default");

                return Json(new { success = true, message = "All passwords reset successfully", defaultPassword = defaultPassword, count = results.Count, users = results });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}
