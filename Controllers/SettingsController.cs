using CRM.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using CRM.Models;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using CRM.Attributes;

namespace CRM.Controllers
{
    [Authorize]
    [PermissionAuthorize("View")]
    public class SettingsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<SettingsController> _logger;

        public SettingsController(AppDbContext db, IHttpContextAccessor httpContextAccessor, IWebHostEnvironment env, ILogger<SettingsController> logger)
        {
            _db = db;
            _httpContextAccessor = httpContextAccessor;
            _env = env;
            _logger = logger;
        }

        // GET: Settings
        public IActionResult Index()
        {
            var role = User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid, out int userId);
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;

            var settingsQuery = _db.Settings.AsQueryable();
            if (role?.ToLower() == "partner")
                settingsQuery = settingsQuery.Where(s => s.ChannelPartnerId == channelPartnerId);
            else if (role?.ToLower() == "admin")
                settingsQuery = settingsQuery.Where(s => s.ChannelPartnerId == null);

            var settings = settingsQuery.ToList();
            
            // Convert list to dictionary for easier access in view (handle duplicates by taking first)
            var settingsDict = settings.GroupBy(s => s.SettingKey)
                .ToDictionary(g => g.Key, g => g.First().SettingValue ?? "");
            
            return View(settingsDict);
        }

        // POST: Update Settings
        [HttpPost]
        [PermissionAuthorize("Edit")]
        public async Task<IActionResult> UpdateSettings(IFormCollection settings, IFormFile? CompanyLogo, IFormFile? CollapsedLogo)
        {
            System.Diagnostics.Debug.WriteLine("UpdateSettings action HIT");
            try
            {
                var currentUserId = _getCurrentUserId();
                var userRole = User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
                var userIdStr = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(userIdStr, out int userId);
                var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
                var channelPartnerId = currentUser?.ChannelPartnerId;
                var tenantIdForSave = currentUser?.TenantId > 0 ? currentUser.TenantId : (HttpContext.Items["TenantId"] as int? ?? 0);
                var cpIdForFile = userRole?.ToLower() == "partner" ? channelPartnerId : null;

                // Handle logo upload - saved to wwwroot/uploads/logos (not base64)
                if (CompanyLogo != null && CompanyLogo.Length > 0)
                {
                    try
                    {
                        var logoPath = await LogoUploadHelper.SaveLogoAsync(CompanyLogo, _env, "company", _logger);

                        SettingsModel logoSetting;
                        if (userRole?.ToLower() == "partner")
                            logoSetting = _db.Settings.FirstOrDefault(s => s.SettingKey == "CompanyLogo" && s.ChannelPartnerId == channelPartnerId && s.TenantId == tenantIdForSave);
                        else
                            logoSetting = _db.Settings.FirstOrDefault(s => s.SettingKey == "CompanyLogo" && s.ChannelPartnerId == null && s.TenantId == tenantIdForSave);
                        
                        if (logoSetting != null)
                        {
                            var oldLogoValue = logoSetting.SettingValue;
                            logoSetting.SettingValue = logoPath;
                            logoSetting.ModifiedOn = IndianTime.Now;
                            logoSetting.ModifiedBy = currentUserId;
                            _db.Settings.Update(logoSetting);
                            // Remove the previous logo file when replacing (otherwise old files accumulate)
                            if (!string.Equals(oldLogoValue, logoPath, System.StringComparison.OrdinalIgnoreCase))
                                DeleteLogoFile(oldLogoValue);
                        }
                        else
                        {
                            _db.Settings.Add(new SettingsModel
                            {
                                SettingKey = "CompanyLogo",
                                SettingValue = logoPath,
                                SettingType = "Image",
                                ModifiedOn = IndianTime.Now,
                                ModifiedBy = currentUserId,
                                ChannelPartnerId = cpIdForFile,
                                TenantId = tenantIdForSave
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
                        var logoPath = await LogoUploadHelper.SaveLogoAsync(CollapsedLogo, _env, "collapsed", _logger);

                        SettingsModel collapsedLogoSetting;
                        if (userRole?.ToLower() == "partner")
                            collapsedLogoSetting = _db.Settings.FirstOrDefault(s => s.SettingKey == "CollapsedLogo" && s.ChannelPartnerId == channelPartnerId && s.TenantId == tenantIdForSave);
                        else
                            collapsedLogoSetting = _db.Settings.FirstOrDefault(s => s.SettingKey == "CollapsedLogo" && s.ChannelPartnerId == null && s.TenantId == tenantIdForSave);
                        
                        if (collapsedLogoSetting != null)
                        {
                            var oldCollapsedValue = collapsedLogoSetting.SettingValue;
                            collapsedLogoSetting.SettingValue = logoPath;
                            collapsedLogoSetting.ModifiedOn = IndianTime.Now;
                            collapsedLogoSetting.ModifiedBy = currentUserId;
                            _db.Settings.Update(collapsedLogoSetting);
                            if (!string.Equals(oldCollapsedValue, logoPath, System.StringComparison.OrdinalIgnoreCase))
                                DeleteLogoFile(oldCollapsedValue);
                        }
                        else
                        {
                            _db.Settings.Add(new SettingsModel
                            {
                                SettingKey = "CollapsedLogo",
                                SettingValue = logoPath,
                                SettingType = "Image",
                                ModifiedOn = IndianTime.Now,
                                ModifiedBy = currentUserId,
                                ChannelPartnerId = cpIdForFile,
                                TenantId = tenantIdForSave
                            });
                        }
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Json(new { success = false, message = ex.Message });
                    }
                }

                foreach (var key in settings.Keys)
                {
                    if (key == "CompanyLogo" || key == "CollapsedLogo" || key == "__RequestVerificationToken")
                        continue;

                    // Handle checkbox values: when both hidden (false) and checkbox (true) are submitted,
                    // take the LAST value (checkbox's true) which represents the actual user choice
                    var strValues = settings[key];
                    var value = strValues.Count > 1 ? strValues.Last() : strValues.ToString();
                    
                    SettingsModel existingSetting;
                    if (userRole?.ToLower() == "partner")
                        existingSetting = _db.Settings.FirstOrDefault(s => s.SettingKey == key && s.ChannelPartnerId == channelPartnerId && s.TenantId == tenantIdForSave);
                    else
                        existingSetting = _db.Settings.FirstOrDefault(s => s.SettingKey == key && s.ChannelPartnerId == null && s.TenantId == tenantIdForSave);
                    
                    if (existingSetting != null)
                    {
                        existingSetting.SettingValue = value;
                        existingSetting.ModifiedOn = IndianTime.Now;
                        existingSetting.ModifiedBy = currentUserId;
                        _db.Settings.Update(existingSetting);
                    }
                    else
                    {
                        _db.Settings.Add(new SettingsModel
                        {
                            SettingKey = key,
                            SettingValue = value,
                            SettingType = "Text",
                            ModifiedOn = IndianTime.Now,
                            ModifiedBy = currentUserId,
                            ChannelPartnerId = cpIdForFile,
                            TenantId = tenantIdForSave
                        });
                    }
                }

                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Settings updated successfully" });
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
                var userIdStr = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(userIdStr, out int uId);
                var currentUser = _db.Users.FirstOrDefault(u => u.UserId == uId);
                var cpId = currentUser?.ChannelPartnerId;
                var tenantId = currentUser?.TenantId > 0 ? currentUser.TenantId : (HttpContext.Items["TenantId"] as int? ?? 0);

                SettingsModel logoSetting;
                if (userRole?.ToLower() == "partner")
                    logoSetting = _db.Settings.FirstOrDefault(s => s.SettingKey == "CompanyLogo" && s.ChannelPartnerId == cpId && s.TenantId == tenantId);
                else
                    logoSetting = _db.Settings.FirstOrDefault(s => s.SettingKey == "CompanyLogo" && s.ChannelPartnerId == null && s.TenantId == tenantId);

                if (logoSetting != null)
                {
                    // Delete the physical file if it was stored as a path (not a base64 blob)
                    DeleteLogoFile(logoSetting.SettingValue);
                    _db.Settings.Remove(logoSetting);
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
                var userIdStr = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(userIdStr, out int uId);
                var currentUser = _db.Users.FirstOrDefault(u => u.UserId == uId);
                var cpId = currentUser?.ChannelPartnerId;
                var tenantId = currentUser?.TenantId > 0 ? currentUser.TenantId : (HttpContext.Items["TenantId"] as int? ?? 0);

                SettingsModel logoSetting;
                if (userRole?.ToLower() == "partner")
                    logoSetting = _db.Settings.FirstOrDefault(s => s.SettingKey == "CollapsedLogo" && s.ChannelPartnerId == cpId && s.TenantId == tenantId);
                else
                    logoSetting = _db.Settings.FirstOrDefault(s => s.SettingKey == "CollapsedLogo" && s.ChannelPartnerId == null && s.TenantId == tenantId);

                if (logoSetting != null)
                {
                    DeleteLogoFile(logoSetting.SettingValue);
                    _db.Settings.Remove(logoSetting);
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
            // Only delete physical files (paths start with /uploads/); leave base64 blobs untouched
            if (!settingValue.StartsWith("/uploads/", System.StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                var fullPath = Path.Combine(_env.WebRootPath, settingValue.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(fullPath))
                    System.IO.File.Delete(fullPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete logo file {Path}", settingValue);
            }
        }

        // GET: Get specific setting value
        [HttpGet]
        public IActionResult GetSetting(string key)
        {
            var userIdStr = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(userIdStr ?? "0", out int gsUserId);
            var gsUser = _db.Users.FirstOrDefault(u => u.UserId == gsUserId);
            var gsTenantId = gsUser?.TenantId > 0 ? gsUser.TenantId : (HttpContext.Items["TenantId"] as int? ?? 0);
            var setting = _db.Settings.FirstOrDefault(s => s.SettingKey == key
                && (gsTenantId > 0 ? s.TenantId == gsTenantId : true));
            
            if (setting != null)
            {
                return Json(new { success = true, value = setting.SettingValue });
            }
            
            return Json(new { success = false, message = "Setting not found" });
        }

        // Helper method to get current user ID from JWT
        private int? _getCurrentUserId()
        {
            try
            {
                string? token = _httpContextAccessor.HttpContext?.Request.Cookies["jwtToken"];
                if (string.IsNullOrEmpty(token)) return null;

                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(token);

                var userIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == "UserId" || c.Type == "sub");
                if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
                {
                    return userId;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        // Backward compatible - defaults to Admin settings (channelPartnerId = null)
        public static string GetSettingValue(AppDbContext db, string key, string defaultValue = "")
        {
            var setting = db.Settings.FirstOrDefault(s => s.SettingKey == key && s.ChannelPartnerId == null);
            return setting?.SettingValue ?? defaultValue;
        }

        // New overload with channelPartnerId
        public static string GetSettingValue(AppDbContext db, string key, int? channelPartnerId, string defaultValue = "")
        {
            var setting = db.Settings.FirstOrDefault(s => s.SettingKey == key && s.ChannelPartnerId == channelPartnerId);
            return setting?.SettingValue ?? defaultValue;
        }

        // Tenant-scoped overload: never leaks another tenant's settings. Falls back to the
        // tenant-scoped row, then an unassigned (TenantId == 0) row of the same scope.
        public static string GetSettingValue(AppDbContext db, string key, int tenantId, int? channelPartnerId, string defaultValue = "")
        {
            if (tenantId > 0)
            {
                var scoped = db.Settings.FirstOrDefault(s => s.SettingKey == key && s.TenantId == tenantId
                    && (channelPartnerId.HasValue && channelPartnerId.Value > 0 ? s.ChannelPartnerId == channelPartnerId.Value : s.ChannelPartnerId == null));
                if (scoped != null) return scoped.SettingValue ?? defaultValue;

                var legacy = db.Settings.FirstOrDefault(s => s.SettingKey == key && s.TenantId == 0
                    && (channelPartnerId.HasValue && channelPartnerId.Value > 0 ? s.ChannelPartnerId == channelPartnerId.Value : s.ChannelPartnerId == null));
                if (legacy != null) return legacy.SettingValue ?? defaultValue;

                return defaultValue;
            }

            var setting = db.Settings.FirstOrDefault(s => s.SettingKey == key
                && (channelPartnerId.HasValue && channelPartnerId.Value > 0 ? s.ChannelPartnerId == channelPartnerId.Value : s.ChannelPartnerId == null));
            return setting?.SettingValue ?? defaultValue;
        }

        // Backward compatible - defaults to Admin settings
        public static decimal GetSettingValueDecimal(AppDbContext db, string key, decimal defaultValue = 0)
        {
            var setting = db.Settings.FirstOrDefault(s => s.SettingKey == key && s.ChannelPartnerId == null);
            if (setting != null && decimal.TryParse(setting.SettingValue, out decimal value))
            {
                return value;
            }
            return defaultValue;
        }

        // New overload with channelPartnerId
        public static decimal GetSettingValueDecimal(AppDbContext db, string key, int? channelPartnerId, decimal defaultValue = 0)
        {
            var setting = db.Settings.FirstOrDefault(s => s.SettingKey == key && s.ChannelPartnerId == channelPartnerId);
            if (setting != null && decimal.TryParse(setting.SettingValue, out decimal value))
            {
                return value;
            }
            return defaultValue;
        }

        // GET: Branding Settings
        [RoleAuthorize("Admin")]
        [PermissionAuthorize("View")]
        public IActionResult Branding()
        {
            var branding = _db.Branding.FirstOrDefault() ?? new BrandingModel();
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
                
                var currentUserId = _getCurrentUserId();
                var existingBranding = _db.Branding.FirstOrDefault();

                if (existingBranding == null)
                {
                    existingBranding = new BrandingModel();
                    _db.Branding.Add(existingBranding);
                }

                // Handle Company Logo upload - saved to wwwroot/uploads/logos (not base64)
                if (CompanyLogo != null && CompanyLogo.Length > 0)
                {
                    try
                    {
                        existingBranding.CompanyLogo = await LogoUploadHelper.SaveLogoAsync(CompanyLogo, _env, "branding-company", _logger);
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
                        existingBranding.AboutUsImage = await LogoUploadHelper.SaveLogoAsync(AboutUsImage, _env, "branding-aboutus", _logger);
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
                        existingBranding.FooterLogo = await LogoUploadHelper.SaveLogoAsync(FooterLogo, _env, "branding-footer", _logger);
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
                existingBranding.ModifiedBy = currentUserId;

                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Branding updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // Helper method to get branding data
        public static BrandingModel GetBrandingData(AppDbContext db)
        {
            return db.Branding.FirstOrDefault() ?? new BrandingModel();
        }

        // POST: Remove Branding Image - uses form-encoded fieldName (NOT [FromBody] JSON)
        [HttpPost]
        [RoleAuthorize("Admin")]
        [PermissionAuthorize("Edit")]
        public async Task<IActionResult> RemoveBrandingImage(string fieldName)
        {
            try
            {
                var branding = _db.Branding.FirstOrDefault();
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
                branding.ModifiedBy = _getCurrentUserId();
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Image removed successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // GET: Impersonation Settings
        [RoleAuthorize("Admin")]
        public IActionResult Impersonation()
        {
            var users = _db.Users.Where(u => u.IsActive).OrderBy(u => u.Role).ThenBy(u => u.Username).ToList();
            var roles = _db.RolePermissions.Select(r => r.RoleName).ToList();
            ViewBag.Roles = roles;
            return View(users);
        }

        // Testimonials

        [HttpGet]
        public IActionResult Testimonials()
        {
            var testimonials = _db.Testimonials.OrderByDescending(t => t.CreatedOn).ToList();
            return View(testimonials);
        }

        [HttpPost]
        public async Task<IActionResult> SaveTestimonial(TestimonialModel model, IFormFile? Image)
        {
            try
            {
                if (Image != null && Image.Length > 0)
                {
                    try
                    {
                        model.ImageBase64 = await LogoUploadHelper.SaveLogoAsync(Image, _env, "testimonial", _logger) ?? model.ImageBase64;
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Json(new { success = false, message = ex.Message });
                    }
                }

                if (model.TestimonialId == 0)
                {
                    model.CreatedOn = IndianTime.Now;
                    _db.Testimonials.Add(model);
                }
                else
                {
                    var existing = await _db.Testimonials.FindAsync(model.TestimonialId);
                    if (existing == null)
                        return Json(new { success = false, message = "Testimonial not found" });

                    existing.Name = model.Name;
                    existing.Tag = model.Tag;
                    existing.Content = model.Content;
                    existing.Rating = model.Rating;
                    existing.IsActive = model.IsActive;
                    if (!string.IsNullOrEmpty(model.ImageBase64))
                        existing.ImageBase64 = model.ImageBase64;
                }

                await _db.SaveChangesAsync();
                return Json(new { success = true, message = "Testimonial saved successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteTestimonial(int id)
        {
            try
            {
                var testimonial = await _db.Testimonials.FindAsync(id);
                if (testimonial != null)
                {
                    _db.Testimonials.Remove(testimonial);
                    await _db.SaveChangesAsync();
                }
                return Json(new { success = true, message = "Testimonial deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult GetTestimonial(int id)
        {
            var t = _db.Testimonials.Find(id);
            if (t == null) return Json(new { success = false });
            return Json(new { success = true, data = t });
        }
    }
}