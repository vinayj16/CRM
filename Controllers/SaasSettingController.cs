using CRM.Attributes;
using CRM.Helpers;
using CRM.MasterDb;
using CRM.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CRM.Controllers
{
    [Authorize]
    [PermissionAuthorize("View")]
    public class SaasSettingController : Controller
    {
        private readonly MasterDbContext _db;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<SaasSettingController> _logger;

        public SaasSettingController(MasterDbContext db, IHttpContextAccessor httpContextAccessor, IWebHostEnvironment env, ILogger<SaasSettingController> logger)
        {
            _db = db;
            _httpContextAccessor = httpContextAccessor;
            _env = env;
            _logger = logger;
        }

        // GET: SaasSetting
        //[Route("saassetting")]
        public IActionResult Index()
        {
            var role = User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("Id")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid, out int id);
            var currentUser = _db.SuperAdmins.FirstOrDefault(u => u.SuperAdminId == id);
            //var channelPartnerId = currentUser?.ChannelPartnerId;

            var saassettingQuery = _db.SaasSetting.AsQueryable();
            //if (role?.ToLower() == "partner")
            //    saassettingQuery = saassettingQuery.Where(s => s.ChannelPartnerId == channelPartnerId);
            //else if (role?.ToLower() == "admin")
            //saassettingQuery = saassettingQuery.Where(s => s.ChannelPartnerId == null);

            var saassetting = saassettingQuery.ToList();

            // Convert list to dictionary for easier access in view
            var saassettingDict = saassetting.ToDictionary(s => s.SettingKey, s => s.SettingValue ?? "");

            return View(saassettingDict);
        }
        // GET: SaasSetting/SystemSettings
        // Redirect to Index (the actual System Settings view)
        public IActionResult SystemSettings()
        {
            return RedirectToAction(nameof(Index));
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
        // POST: Update SaasSetting
        [HttpPost]
        [PermissionAuthorize("Edit")]
        public async Task<IActionResult> UpdateSaasSetting(IFormCollection saassetting, IFormFile? CompanyLogo, IFormFile? CollapsedLogo)
        {
            System.Diagnostics.Debug.WriteLine("UpdateSaasSetting action HIT");
            try
            {
                var userInfo = GetUserDetailsFromToken();

                ViewBag.UserId = userInfo.userId;
                ViewBag.Username = userInfo.username;
                ViewBag.Role = userInfo.userId;

                var currentId = userInfo.userId;
                var userRole = User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
                var idStr = userInfo.userId;
                //int.TryParse(idStr, out int id);
                var currentUser = _db.SuperAdmins.FirstOrDefault(u => u.SuperAdminId == idStr);
                //var channelPartnerId = currentUser?.ChannelPartnerId;

                // Handle logo upload - saved to wwwroot/uploads/logos (not base64)
                if (CompanyLogo != null && CompanyLogo.Length > 0)
                {
                    try
                    {
                        var logoPath = await LogoUploadHelper.SaveLogoAsync(CompanyLogo, _env, "saas-company", _logger);

                        SaasSettingsModel logoSetting;
                        //if (userRole?.ToLower() == "partner")
                        //    logoSetting = _db.SaasSetting.FirstOrDefault(s => s.SettingKey == "CompanyLogo" && s.ChannelPartnerId == channelPartnerId);
                        //else
                        logoSetting = _db.SaasSetting.FirstOrDefault(s => s.SettingKey == "CompanyLogo");

                        if (logoSetting != null)
                        {
                            var oldLogoValue = logoSetting.SettingValue;
                            logoSetting.SettingValue = logoPath;
                            logoSetting.ModifiedOn = IndianTime.Now;
                            logoSetting.ModifiedBy = currentId;
                            _db.SaasSetting.Update(logoSetting);
                            if (!string.Equals(oldLogoValue, logoPath, System.StringComparison.OrdinalIgnoreCase))
                                DeleteLogoFile(oldLogoValue);
                        }
                        else
                        {
                            _db.SaasSetting.Add(new SaasSettingsModel
                            {
                                SettingKey = "CompanyLogo",
                                SettingValue = logoPath,
                                SettingType = "Image",
                                ModifiedOn = IndianTime.Now,
                                ModifiedBy = currentId,
                                ChannelPartnerId = 0
                            });
                        }
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Json(new { success = false, message = ex.Message });
                    }
                }

                // Handle collapsed logo upload - saved to wwwroot/uploads/logos (not base64)
                if (CollapsedLogo != null && CollapsedLogo.Length > 0)
                {
                    try
                    {
                        var logoPath = await LogoUploadHelper.SaveLogoAsync(CollapsedLogo, _env, "saas-collapsed", _logger);

                        SaasSettingsModel collapsedLogoSetting;
                        //if (userRole?.ToLower() == "partner")
                        //    collapsedLogoSetting = _db.SaasSetting.FirstOrDefault(s => s.SettingKey == "CollapsedLogo" && s.ChannelPartnerId == channelPartnerId);
                        //else
                        collapsedLogoSetting = _db.SaasSetting.FirstOrDefault(s => s.SettingKey == "CollapsedLogo");

                        if (collapsedLogoSetting != null)
                        {
                            var oldCollapsedValue = collapsedLogoSetting.SettingValue;
                            collapsedLogoSetting.SettingValue = logoPath;
                            collapsedLogoSetting.ModifiedOn = IndianTime.Now;
                            collapsedLogoSetting.ModifiedBy = currentId;
                            _db.SaasSetting.Update(collapsedLogoSetting);
                            if (!string.Equals(oldCollapsedValue, logoPath, System.StringComparison.OrdinalIgnoreCase))
                                DeleteLogoFile(oldCollapsedValue);
                        }
                        else
                        {
                            _db.SaasSetting.Add(new SaasSettingsModel
                            {
                                SettingKey = "CollapsedLogo",
                                SettingValue = logoPath,
                                SettingType = "Image",
                                ModifiedOn = IndianTime.Now,
                                ModifiedBy = currentId,
                                ChannelPartnerId = 0
                            });
                        }
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Json(new { success = false, message = ex.Message });
                    }
                }

                foreach (var key in saassetting.Keys)
                {
                    if (key == "CompanyLogo" || key == "CollapsedLogo" || key == "__RequestVerificationToken")
                        continue;

                    var value = saassetting[key].ToString();

                    SaasSettingsModel existingSetting;
                    //if (userRole?.ToLower() == "partner")
                    //    existingSetting = _db.SaasSetting.FirstOrDefault(s => s.SettingKey == key && s.ChannelPartnerId == channelPartnerId);
                    //else
                    existingSetting = _db.SaasSetting.FirstOrDefault(s => s.SettingKey == key);

                    if (existingSetting != null)
                    {
                        existingSetting.SettingValue = value;
                        existingSetting.ModifiedOn = IndianTime.Now;
                        existingSetting.ModifiedBy = currentId;
                        _db.SaasSetting.Update(existingSetting);
                    }
                    else
                    {
                        _db.SaasSetting.Add(new SaasSettingsModel
                        {
                            SettingKey = key,
                            SettingValue = value,
                            SettingType = "Text",
                            ModifiedOn = IndianTime.Now,
                            ModifiedBy = currentId,
                            ChannelPartnerId = 0
                        });
                    }
                }

                await _db.SaveChangesAsync();

                // Audit log
                try { var appDb = HttpContext.RequestServices.GetRequiredService<AppDbContext>(); appDb.AuditLogs.Add(new AuditLogModel { UserId = currentId, Action = "Update", EntityType = "Setting", Timestamp = DateTime.UtcNow }); await appDb.SaveChangesAsync(); } catch { }

                return Json(new { success = true, message = "SaasSetting updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}. Inner: {ex.InnerException?.Message}" });
            }
        }

        // POST: Remove Logo
        [HttpPost]
        public IActionResult RemoveLogo()
        {
            try
            {
                var userRole = User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
                var idStr = User?.FindFirst("Id")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(idStr, out int uId);
                var currentUser = _db.SuperAdmins.FirstOrDefault(u => u.SuperAdminId == uId);
                //var cpId = currentUser?.ChannelPartnerId;

                SaasSettingsModel logoSetting;
                // SaasSettings use ChannelPartnerId = 0 (not null) for global settings
                logoSetting = _db.SaasSetting.FirstOrDefault(s => s.SettingKey == "CompanyLogo" && s.ChannelPartnerId == 0);

                if (logoSetting == null)
                    logoSetting = _db.SaasSetting.FirstOrDefault(s => s.SettingKey == "CompanyLogo" && s.ChannelPartnerId == null);

                if (logoSetting != null)
                {
                    DeleteLogoFile(logoSetting.SettingValue);
                    _db.SaasSetting.Remove(logoSetting);
                    _db.SaveChanges();
                }

                return Json(new { success = true, message = "Logo removed successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // POST: Remove Collapsed Logo
        [HttpPost]
        public IActionResult RemoveCollapsedLogo()
        {
            try
            {
                var userRole = User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
                var idStr = User?.FindFirst("Id")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(idStr, out int uId);
                var currentUser = _db.SuperAdmins.FirstOrDefault(u => u.SuperAdminId == uId);
                //var cpId = currentUser?.ChannelPartnerId;

                SaasSettingsModel logoSetting;
                // SaasSettings use ChannelPartnerId = 0 (not null) for global settings
                logoSetting = _db.SaasSetting.FirstOrDefault(s => s.SettingKey == "CollapsedLogo" && s.ChannelPartnerId == 0);

                if (logoSetting == null)
                    logoSetting = _db.SaasSetting.FirstOrDefault(s => s.SettingKey == "CollapsedLogo" && s.ChannelPartnerId == null);

                if (logoSetting != null)
                {
                    DeleteLogoFile(logoSetting.SettingValue);
                    _db.SaasSetting.Remove(logoSetting);
                    _db.SaveChanges();
                }

                return Json(new { success = true, message = "Collapsed logo removed successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        private void DeleteLogoFile(string? settingValue)
        {
            if (string.IsNullOrWhiteSpace(settingValue)) return;
            if (!settingValue.StartsWith("/uploads/", System.StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                var fullPath = Path.Combine(_env.WebRootPath, settingValue.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(fullPath))
                    System.IO.File.Delete(fullPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete SaaS logo file {Path}", settingValue);
            }
        }

        // GET: Get specific setting value
        [HttpGet]
        public IActionResult GetSetting(string key)
        {
            var setting = _db.SaasSetting.FirstOrDefault(s => s.SettingKey == key);

            if (setting != null)
            {
                return Json(new { success = true, value = setting.SettingValue });
            }

            return Json(new { success = false, message = "Setting not found" });
        }

        // Helper method to get current user ID from JWT
        private int? _getCurrentId()
        {
            try
            {
                string? token = _httpContextAccessor.HttpContext?.Request.Cookies["jwtToken"];
                if (string.IsNullOrEmpty(token)) return null;

                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(token);

                var idClaim = jwt.Claims.FirstOrDefault(c => c.Type == "Id" || c.Type == "sub");
                if (idClaim != null && int.TryParse(idClaim.Value, out int id))
                {
                    return id;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        // Backward compatible - defaults to Admin saassetting (channelPartnerId = null)
        public static string GetSettingValue(MasterDbContext db, string key, string defaultValue = "")
        {
            var setting = db.SaasSetting.FirstOrDefault(s => s.SettingKey == key && s.ChannelPartnerId == null);
            return setting?.SettingValue ?? defaultValue;
        }

        // New overload with channelPartnerId
        public static string GetSettingValue(MasterDbContext db, string key, int? channelPartnerId, string defaultValue = "")
        {
            var setting = db.SaasSetting.FirstOrDefault(s => s.SettingKey == key && s.ChannelPartnerId == channelPartnerId);
            return setting?.SettingValue ?? defaultValue;
        }

        // Backward compatible - defaults to Admin saassetting
        public static decimal GetSettingValueDecimal(MasterDbContext db, string key, decimal defaultValue = 0)
        {
            var setting = db.SaasSetting.FirstOrDefault(s => s.SettingKey == key && s.ChannelPartnerId == null);
            if (setting != null && decimal.TryParse(setting.SettingValue, out decimal value))
            {
                return value;
            }
            return defaultValue;
        }

        // New overload with channelPartnerId
        public static decimal GetSettingValueDecimal(MasterDbContext db, string key, int? channelPartnerId, decimal defaultValue = 0)
        {
            var setting = db.SaasSetting.FirstOrDefault(s => s.SettingKey == key && s.ChannelPartnerId == channelPartnerId);
            if (setting != null && decimal.TryParse(setting.SettingValue, out decimal value))
            {
                return value;
            }
            return defaultValue;
        }

        // GET: Branding SaasSetting
        [RoleAuthorize("Admin")]
        [PermissionAuthorize("View")]
        //[Route("branding")]
        public IActionResult Branding()
        {
            var branding = _db.SaasBranding.FirstOrDefault() ?? new SaasBrandingModel();
            return View(branding);
        }

        // POST: Update Branding
        [HttpPost]
        [RoleAuthorize("Admin")]
        [PermissionAuthorize("Edit")]
        public async Task<IActionResult> UpdateBranding(IFormCollection form, IFormFile? CompanyLogo, IFormFile? AboutUsImage, IFormFile? FooterLogo)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("UpdateBranding called");
                foreach (var key in form.Keys)
                {
                    System.Diagnostics.Debug.WriteLine($"Form Key: {key}, Value: {form[key]}");
                }

                var currentId = _getCurrentId();
                var existingBranding = _db.SaasBranding.FirstOrDefault();

                if (existingBranding == null)
                {
                    existingBranding = new SaasBrandingModel();
                    _db.SaasBranding.Add(existingBranding);
                }

                // Handle Company Logo upload - saved to wwwroot/uploads/logos (not base64)
                if (CompanyLogo != null && CompanyLogo.Length > 0)
                {
                    try
                    {
                        existingBranding.CompanyLogo = await LogoUploadHelper.SaveLogoAsync(CompanyLogo, _env, "saas-branding-company", _logger);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Json(new { success = false, message = ex.Message });
                    }
                }

                // Handle About Us Image upload - saved to wwwroot/uploads/logos (not base64)
                if (AboutUsImage != null && AboutUsImage.Length > 0)
                {
                    try
                    {
                        existingBranding.AboutUsImage = await LogoUploadHelper.SaveLogoAsync(AboutUsImage, _env, "saas-branding-aboutus", _logger);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Json(new { success = false, message = ex.Message });
                    }
                }

                // Handle Footer Logo upload - saved to wwwroot/uploads/logos (not base64)
                if (FooterLogo != null && FooterLogo.Length > 0)
                {
                    try
                    {
                        existingBranding.FooterLogo = await LogoUploadHelper.SaveLogoAsync(FooterLogo, _env, "saas-branding-footer", _logger);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Json(new { success = false, message = ex.Message });
                    }
                }

                // Update text fields from form data - only if provided
                if (form.ContainsKey("LogoDisplayStyle")) existingBranding.LogoDisplayStyle = string.IsNullOrWhiteSpace(form["LogoDisplayStyle"].ToString()) ? "LogoOnly" : form["LogoDisplayStyle"].ToString();
                if (form.ContainsKey("TwitterUrl")) existingBranding.TwitterUrl = form["TwitterUrl"].ToString();
                if (form.ContainsKey("WhatsAppNumber")) existingBranding.WhatsAppNumber = form["WhatsAppNumber"].ToString();
                if (form.ContainsKey("FacebookUrl")) existingBranding.FacebookUrl = form["FacebookUrl"].ToString();
                if (form.ContainsKey("InstagramUrl")) existingBranding.InstagramUrl = form["InstagramUrl"].ToString();
                if (form.ContainsKey("LinkedInUrl")) existingBranding.LinkedInUrl = form["LinkedInUrl"].ToString();
                if (form.ContainsKey("AboutUsText")) existingBranding.AboutUsText = form["AboutUsText"].ToString();
                if (form.ContainsKey("CompanyInfo")) existingBranding.CompanyInfo = form["CompanyInfo"].ToString();
                if (form.ContainsKey("TermsAndConditions")) existingBranding.TermsAndConditions = form["TermsAndConditions"].ToString();
                if (form.ContainsKey("PrivacyPolicy")) existingBranding.PrivacyPolicy = form["PrivacyPolicy"].ToString();
                if (form.ContainsKey("RefundPolicy")) existingBranding.RefundPolicy = form["RefundPolicy"].ToString();
                existingBranding.ModifiedOn = IndianTime.Now;
                existingBranding.ModifiedBy = currentId;

                await _db.SaveChangesAsync();

                // Audit log
                try { var appDb = HttpContext.RequestServices.GetRequiredService<AppDbContext>(); appDb.AuditLogs.Add(new AuditLogModel { UserId = currentId, Action = "Update", EntityType = "Branding", Timestamp = DateTime.UtcNow }); await appDb.SaveChangesAsync(); } catch { }

                return Json(new { success = true, message = "Branding updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // =============================================
        // Maintenance Mode Management
        // =============================================
        [HttpGet]
        public async Task<IActionResult> Maintenance()
        {
            var settings = _db.SaasSetting.ToList();
            var dict = new Dictionary<string, object>();
            foreach (var s in settings)
            {
                dict[s.SettingKey] = s.SettingValue ?? "";
            }
            
            // Add maintenance logs from AppDbContext
            try
            {
                var appDb = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var maintenanceLogs = await appDb.MaintenanceLogs.OrderByDescending(m => m.StartedOn).ToListAsync();
                dict["MaintenanceLogs"] = maintenanceLogs;
            }
            catch { dict["MaintenanceLogs"] = new List<MaintenanceLogModel>(); }
            
            return View(dict);
        }

        [HttpPost]
        public async Task<IActionResult> SaveMaintenanceSettings(bool isEnabled, string? message, DateTime? startDate, DateTime? endDate)
        {
            try
            {
                var appDb = HttpContext.RequestServices.GetRequiredService<AppDbContext>();

                // Save maintenance mode toggle
                var enabledSetting = _db.SaasSetting.FirstOrDefault(s => s.SettingKey == "MaintenanceMode");
                if (enabledSetting != null)
                {
                    enabledSetting.SettingValue = isEnabled ? "true" : "false";
                    enabledSetting.ModifiedOn = IndianTime.Now;
                    _db.SaasSetting.Update(enabledSetting);
                }
                else
                {
                    _db.SaasSetting.Add(new SaasSettingsModel
                    {
                        SettingKey = "MaintenanceMode",
                        SettingValue = isEnabled ? "true" : "false",
                        SettingType = "Boolean",
                        ModifiedOn = IndianTime.Now
                    });
                }

                // Save maintenance message
                var msgSetting = _db.SaasSetting.FirstOrDefault(s => s.SettingKey == "MaintenanceMessage");
                if (msgSetting != null)
                {
                    msgSetting.SettingValue = message ?? "We are currently performing scheduled maintenance. Please check back shortly.";
                    msgSetting.ModifiedOn = IndianTime.Now;
                    _db.SaasSetting.Update(msgSetting);
                }
                else
                {
                    _db.SaasSetting.Add(new SaasSettingsModel
                    {
                        SettingKey = "MaintenanceMessage",
                        SettingValue = message ?? "We are currently performing scheduled maintenance. Please check back shortly.",
                        SettingType = "Text",
                        ModifiedOn = IndianTime.Now
                    });
                }

                if (startDate.HasValue)
                {
                    var startSetting = _db.SaasSetting.FirstOrDefault(s => s.SettingKey == "MaintenanceStartDate");
                    if (startSetting != null)
                    {
                        startSetting.SettingValue = startDate.Value.ToString("yyyy-MM-dd HH:mm");
                        _db.SaasSetting.Update(startSetting);
                    }
                    else
                        _db.SaasSetting.Add(new SaasSettingsModel { SettingKey = "MaintenanceStartDate", SettingValue = startDate.Value.ToString("yyyy-MM-dd HH:mm"), SettingType = "DateTime", ModifiedOn = IndianTime.Now });
                }

                if (endDate.HasValue)
                {
                    var endSetting = _db.SaasSetting.FirstOrDefault(s => s.SettingKey == "MaintenanceEndDate");
                    if (endSetting != null)
                    {
                        endSetting.SettingValue = endDate.Value.ToString("yyyy-MM-dd HH:mm");
                        _db.SaasSetting.Update(endSetting);
                    }
                    else
                        _db.SaasSetting.Add(new SaasSettingsModel { SettingKey = "MaintenanceEndDate", SettingValue = endDate.Value.ToString("yyyy-MM-dd HH:mm"), SettingType = "DateTime", ModifiedOn = IndianTime.Now });
                }

                // Log this maintenance event
                appDb.MaintenanceLogs.Add(new MaintenanceLogModel
                {
                    IsEnabled = isEnabled,
                    Message = message ?? "",
                    StartedOn = startDate ?? IndianTime.Now,
                    EndedOn = endDate,
                    SetBy = User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "System",
                    CreatedOn = IndianTime.Now
                });

                await _db.SaveChangesAsync();
                await appDb.SaveChangesAsync();

                // Audit log
                try { appDb.AuditLogs.Add(new AuditLogModel { UserId = _getCurrentId(), Action = "Update", EntityType = "Maintenance", Timestamp = DateTime.UtcNow, NewValues = System.Text.Json.JsonSerializer.Serialize(new { enabled = isEnabled }) }); await appDb.SaveChangesAsync(); } catch { }

                return Json(new { success = true, message = "Maintenance settings saved" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Helper method to get branding data
        public static BrandingModel GetBrandingData(AppDbContext db)
        {
            return db.Branding.FirstOrDefault() ?? new BrandingModel();
        }

        // POST: Remove Branding Image
        [HttpPost]
        [RoleAuthorize("Admin")]
        [PermissionAuthorize("Edit")]
        public async Task<IActionResult> RemoveBrandingImage([FromBody] string fieldName)
        {
            try
            {
                var branding = _db.SaasBranding.FirstOrDefault();
                if (branding == null)
                    return Json(new { success = false, message = "Branding record not found" });

                switch (fieldName)
                {
                    case "CompanyLogo":
                        DeleteLogoFile(branding.CompanyLogo);
                        branding.CompanyLogo = null;
                        break;
                    case "AboutUsImage":
                        DeleteLogoFile(branding.AboutUsImage);
                        branding.AboutUsImage = null;
                        break;
                    case "FooterLogo":
                        DeleteLogoFile(branding.FooterLogo);
                        branding.FooterLogo = null;
                        break;
                    default: return Json(new { success = false, message = "Invalid field" });
                }

                branding.ModifiedOn = IndianTime.Now;
                branding.ModifiedBy = _getCurrentId();
                _db.SaasBranding.Update(branding);
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Image removed successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }
    }
}