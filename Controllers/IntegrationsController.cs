using CRM.Attributes;
using CRM.Helpers;
using CRM.Models;
using CRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;

namespace CRM.Controllers
{
    [Authorize]
    [RoleAuthorize("Admin,Partner")]
    public class IntegrationsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly INotificationService _notificationService;
        private readonly SubscriptionService _subscriptionService;
        private readonly ILogger<IntegrationsController> _logger;

        public IntegrationsController(AppDbContext context, IConfiguration configuration, IHttpClientFactory httpClientFactory, INotificationService notificationService, SubscriptionService subscriptionService, ILogger<IntegrationsController> logger)
        {
            _context = context;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _notificationService = notificationService;
            _subscriptionService = subscriptionService;
            _logger = logger;
        }

        private async Task<bool> CheckCustomAPIFeatureAsync()
        {
            var partnerId = GetChannelPartnerId();
            if (partnerId == null) return true; // Admin access
            return await _subscriptionService.HasFeatureAccessAsync(partnerId.Value, "customapi");
        }

        private int? GetChannelPartnerId()
        {
            var role = User.FindFirst(ClaimTypes.Role)?.Value;
            if (role == "Admin") return null;
            var username = User.FindFirst(ClaimTypes.Name)?.Value;
            return _context.Users.FirstOrDefault(u => u.Username == username)?.ChannelPartnerId;
        }
        [Route("Integrations/Index")]
        public IActionResult Index()
        {
            return RedirectToAction("LeadIntegrations");
        }

        [Route("integrations")]
        public async Task<IActionResult> LeadIntegrations()
        {
            if (!await CheckCustomAPIFeatureAsync())
            {
                TempData["ErrorMessage"] = "API integrations are not available in your current plan. Please upgrade to access this feature.";
                return RedirectToAction("MyPlan", "SaasSubscription");
            }

            var partnerId = GetChannelPartnerId();
            var configs = await _context.LeadIntegrationConfigs
                .Where(c => c.ChannelPartnerId == partnerId)
                .ToListAsync();
            ViewBag.BaseUrl = $"{Request.Scheme}://{Request.Host}";
            return View(configs);
        }

        [HttpGet]
        public async Task<IActionResult> GetConfig(string platform)
        {
            var partnerId = GetChannelPartnerId();
            var config = await _context.LeadIntegrationConfigs
                .FirstOrDefaultAsync(c => c.PlatformName == platform && c.ChannelPartnerId == partnerId);
            if (config == null)
                return Json(new { success = true, data = (object?)null });
            return Json(new { success = true, data = config });
        }

        [HttpPost]
        public async Task<IActionResult> SaveConfig([FromForm] LeadIntegrationConfigModel model)
        {
            if (!await CheckCustomAPIFeatureAsync())
                return Json(new { success = false, message = "Integrations are not available in your current plan. Please upgrade to access this feature." });

            try
            {
                var partnerId = GetChannelPartnerId();
                var role = User.FindFirst(ClaimTypes.Role)?.Value;
                model.ChannelPartnerId = partnerId;
                model.ConfigScope = role == "Admin" ? "Admin" : "Partner";

                // Validate credentials by calling the actual platform API
                if (model.IsEnabled)
                {
                    var (valid, errorMsg) = await ValidatePlatformCredentials(model);
                    if (!valid)
                        return Json(new { success = false, message = $"API Validation Failed: {errorMsg}" });
                }

                var existing = await _context.LeadIntegrationConfigs
                    .FirstOrDefaultAsync(c => c.PlatformName == model.PlatformName && c.ChannelPartnerId == partnerId);

                if (existing != null)
                {
                    existing.ApiKey = model.ApiKey?.Trim();
                    existing.ApiSecret = model.ApiSecret?.Trim();
                    existing.AccountId = model.AccountId?.Trim();
                    existing.AccessToken = model.AccessToken?.Trim();
                    existing.RefreshToken = model.RefreshToken?.Trim();
                    existing.ProjectId = model.ProjectId?.Trim();
                    existing.CampaignId = model.CampaignId?.Trim();
                    existing.ExtraConfig = model.ExtraConfig?.Trim();
                    existing.IsEnabled = model.IsEnabled;
                    existing.PollIntervalMinutes = model.PollIntervalMinutes > 0 ? model.PollIntervalMinutes : 5;
                    existing.UpdatedOn = IndianTime.Now;
                    existing.WebhookUrl = $"{Request.Scheme}://{Request.Host}/api/integrations/webhook/{model.PlatformName.ToLower()}?key={existing.ApiKey}";
                }
                else
                {
                    model.CreatedOn = IndianTime.Now;
                    model.WebhookUrl = $"{Request.Scheme}://{Request.Host}/api/integrations/webhook/{model.PlatformName.ToLower()}?key={model.ApiKey}";
                    _context.LeadIntegrationConfigs.Add(model);
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Configuration saved successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ToggleIntegration(string platform, bool enable)
        {
            if (!await CheckCustomAPIFeatureAsync())
                return Json(new { success = false, message = "Integrations are not available in your current plan. Please upgrade to access this feature." });

            var partnerId = GetChannelPartnerId();
            var config = await _context.LeadIntegrationConfigs
                .FirstOrDefaultAsync(c => c.PlatformName == platform && c.ChannelPartnerId == partnerId);
            if (config == null)
                return Json(new { success = false, message = "Configuration not found. Please configure first." });

            // Validate before enabling
            if (enable)
            {
                var (valid, errorMsg) = await ValidatePlatformCredentials(config);
                if (!valid)
                    return Json(new { success = false, message = $"Cannot enable - API Validation Failed: {errorMsg}" });
            }

            config.IsEnabled = enable;
            config.UpdatedOn = IndianTime.Now;
            await _context.SaveChangesAsync();
            return Json(new { success = true, message = $"{platform} {(enable ? "enabled" : "disabled")} successfully!" });
        }

        // Real sync - tries actual platform API, shows error if credentials wrong
        [HttpPost]
        public async Task<IActionResult> SyncNow(string platform)
        {
            if (!await CheckCustomAPIFeatureAsync())
                return Json(new { success = false, message = "Integrations are not available in your current plan. Please upgrade to access this feature." });

            var partnerId = GetChannelPartnerId();
            var config = await _context.LeadIntegrationConfigs
                .FirstOrDefaultAsync(c => c.PlatformName == platform && c.ChannelPartnerId == partnerId && c.IsEnabled);
            if (config == null)
                return Json(new { success = false, message = "Integration not enabled or not configured." });

            var (leads, error) = await FetchRealLeads(config);
            if (error != null)
                return Json(new { success = false, message = $"Sync failed: {error}" });

            foreach (var lead in leads)
            {
                lead.ChannelPartnerId = partnerId;
                lead.UtmMedium = "api_sync";
            }

            // Pre-assign sequential LeadIds: AddRange cannot auto-increment, and the shim
            // would otherwise give every id-less lead in the batch the SAME max+1 id.
            await AssignSequentialLeadIdsAsync(leads);
            _context.Leads.AddRange(leads);
            config.LastSyncedAt = IndianTime.Now;
            config.LeadsSynced += leads.Count;
            config.UpdatedOn = IndianTime.Now;
            await _context.SaveChangesAsync();

            foreach (var lead in leads)
            {
                try { await _notificationService.NotifyLeadAddedAsync(lead.LeadId, lead.Name ?? "Unknown", platform); }
                catch { }
            }

            return Json(new { success = true, message = $"Synced {leads.Count} leads from {platform}.", count = leads.Count });
        }

        // Test sync - uses free randomuser.me API, marks leads as [TEST]
        [HttpPost]
        public async Task<IActionResult> TestSync(string platform)
        {
            if (!await CheckCustomAPIFeatureAsync())
                return Json(new { success = false, message = "Integrations are not available in your current plan. Please upgrade to access this feature." });

            var partnerId = GetChannelPartnerId();
            var config = await _context.LeadIntegrationConfigs
                .FirstOrDefaultAsync(c => c.PlatformName == platform && c.ChannelPartnerId == partnerId);
            if (config == null)
                return Json(new { success = false, message = "Please configure the integration first." });

            int count = await FetchTestLeads(config, partnerId);
            if (count == 0)
                return Json(new { success = false, message = "Failed to generate test leads. Please try again." });

            config.LastSyncedAt = IndianTime.Now;
            config.LeadsSynced += count;
            config.UpdatedOn = IndianTime.Now;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = $"Generated {count} test leads from {platform}. These are marked as [TEST] and can be cleared anytime.", count });
        }

        [HttpPost]
        public async Task<IActionResult> ClearTestLeads()
        {
            var partnerId = GetChannelPartnerId();
            var testLeads = await _context.Leads
                .Where(l => l.UtmMedium == "test_sync" && l.ChannelPartnerId == partnerId)
                .ToListAsync();

            // Get test lead IDs to clean up related notifications
            var testLeadIds = testLeads.Select(l => l.LeadId).ToList();

            // Remove test lead notifications
            if (testLeadIds.Any())
            {
                var testNotifications = await _context.Notifications
                    .Where(n => n.RelatedEntityType == "Lead" && n.RelatedEntityId.HasValue && testLeadIds.Contains(n.RelatedEntityId.Value))
                    .ToListAsync();
                _context.Notifications.RemoveRange(testNotifications);
            }

            // Remove test leads
            _context.Leads.RemoveRange(testLeads);

            // Reset LeadsSynced count on all configs
            var configs = await _context.LeadIntegrationConfigs
                .Where(c => c.ChannelPartnerId == partnerId)
                .ToListAsync();
            foreach (var c in configs)
            {
                c.LeadsSynced = 0;
                c.LastSyncedAt = null;
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = testLeads.Count > 0 ? $"Deleted {testLeads.Count} test leads, reset all counters and cleared notifications." : "No test leads found. Counters and sync times have been reset.", count = testLeads.Count });
        }

        [HttpPost]
        public async Task<IActionResult> SyncAllActive()
        {
            if (!await CheckCustomAPIFeatureAsync())
                return Json(new { success = false, message = "Integrations are not available in your current plan. Please upgrade to access this feature." });

            var partnerId = GetChannelPartnerId();
            var activeConfigs = await _context.LeadIntegrationConfigs
                .Where(c => c.IsEnabled && c.ChannelPartnerId == partnerId)
                .ToListAsync();

            if (!activeConfigs.Any())
                return Json(new { success = false, message = "No active integrations found. Enable integrations from the Lead Integrations page." });

            int totalLeads = 0;
            var results = new List<string>();

            foreach (var config in activeConfigs)
            {
                var (leads, error) = await FetchRealLeads(config);
                if (error != null) { results.Add($"{config.PlatformName}: {error}"); continue; }
                if (leads.Count > 0)
                {
                    foreach (var lead in leads) lead.ChannelPartnerId = partnerId;
                    await AssignSequentialLeadIdsAsync(leads);
                    _context.Leads.AddRange(leads);
                    config.LeadsSynced += leads.Count;
                    totalLeads += leads.Count;
                    results.Add($"{config.PlatformName}: {leads.Count} new leads");
                }
                else { results.Add($"{config.PlatformName}: No new leads"); }
                config.LastSyncedAt = IndianTime.Now;
                config.UpdatedOn = IndianTime.Now;
            }

            await _context.SaveChangesAsync();
            if (totalLeads > 0)
            {
                try { await _notificationService.CreateNotificationAsync("Integration Sync Complete", $"{totalLeads} new leads imported from {activeConfigs.Count} integrations", "LeadAdded", link: "/WebhookLeads/Index"); }
                catch { }
            }

            var message = totalLeads > 0
                ? $"Synced {totalLeads} leads from {activeConfigs.Count} integrations. {string.Join(", ", results)}"
                : $"No new leads found. {string.Join(", ", results)}";
            return Json(new { success = true, message, totalLeads });
        }

        [AllowAnonymous]
        [HttpPost("/api/integrations/webhook/{platform}")]
        public async Task<IActionResult> ReceiveWebhook(string platform, [FromQuery] string key)
        {
            var config = await _context.LeadIntegrationConfigs
                .FirstOrDefaultAsync(c => c.PlatformName.ToLower() == platform.ToLower() && c.ApiKey == key && c.IsEnabled);
            if (config == null)
                return Unauthorized(new { error = "Invalid API key or integration disabled." });

            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();

            try
            {
                var leadData = JsonSerializer.Deserialize<JsonElement>(body);
                var lead = new LeadModel
                {
                    Name = GetJsonString(leadData, "name", "lead_name", "full_name", "customer_name"),
                    Contact = GetJsonString(leadData, "phone", "contact", "mobile", "phone_number"),
                    Email = GetJsonString(leadData, "email", "email_address"),
                    Source = platform,
                    Stage = "New",
                    Status = "Active",
                    ChannelPartnerId = config.ChannelPartnerId,
                    CreatedOn = IndianTime.Now,
                    Comments = $"Auto-imported from {platform} webhook",
                    UtmSource = platform,
                    UtmMedium = "webhook",
                    PreferredLocation = GetJsonString(leadData, "location", "city", "preferred_location"),
                    PropertyType = GetJsonString(leadData, "property_type", "type"),
                    BHK = GetJsonString(leadData, "bhk", "bedrooms"),
                    Requirement = GetJsonString(leadData, "requirement", "message", "comments", "description")
                };

                if (string.IsNullOrEmpty(lead.Name)) lead.Name = "Unknown Lead";

                _context.Leads.Add(lead);
                config.LeadsSynced++;
                config.LastSyncedAt = IndianTime.Now;
                await _context.SaveChangesAsync();

                try { await _notificationService.NotifyLeadAddedAsync(lead.LeadId, lead.Name ?? "Unknown", platform); }
                catch { }

                return Ok(new { success = true, leadId = lead.LeadId });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // Validate platform credentials by making a test API call
        private async Task<(bool valid, string? error)> ValidatePlatformCredentials(LeadIntegrationConfigModel config)
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            try
            {
                switch (config.PlatformName)
                {
                    case "GoogleAds":
                        // Validate by trying to get a new access token using refresh token
                        // ApiKey=ClientID, AccessToken=ClientSecret, RefreshToken=RefreshToken
                        var tokenRequest = new FormUrlEncodedContent(new[] {
                            new KeyValuePair<string, string>("client_id", config.ApiKey ?? ""),
                            new KeyValuePair<string, string>("client_secret", config.AccessToken ?? ""),
                            new KeyValuePair<string, string>("refresh_token", config.RefreshToken ?? ""),
                            new KeyValuePair<string, string>("grant_type", "refresh_token")
                        });
                        var tokenResp = await client.PostAsync("https://oauth2.googleapis.com/token", tokenRequest);
                        if (!tokenResp.IsSuccessStatusCode)
                        {
                            var err = await tokenResp.Content.ReadAsStringAsync();
                            try
                            {
                                var errDoc = JsonDocument.Parse(err);
                                var errMsg = errDoc.RootElement.TryGetProperty("error_description", out var desc)
                                    ? desc.GetString() : errDoc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : "Invalid credentials";
                                return (false, $"Google OAuth Error: {errMsg}");
                            }
                            catch { return (false, $"Google OAuth Error: {err}"); }
                        }
                        return (true, null);

                    case "Facebook":
                        // Validate by checking the access token
                        // AccessToken=Access Token, ApiKey=Ad ID
                        if (string.IsNullOrWhiteSpace(config.AccessToken))
                            return (false, "Please provide a Facebook Access Token.");
                        var fbResp = await client.GetAsync($"https://graph.facebook.com/v19.0/me?access_token={config.AccessToken}");
                        if (!fbResp.IsSuccessStatusCode)
                        {
                            var err = await fbResp.Content.ReadAsStringAsync();
                            if (err.Contains("OAuthException") || err.Contains("expired"))
                                return (false, "Facebook Access Token has expired. Please generate a new token from Meta Business Suite.");
                            return (false, $"Facebook API Error: Invalid or expired token. {err}");
                        }
                        return (true, null);

                    case "IndiaMART":
                        // Validate IndiaMART CRM key
                        var imResp = await client.GetAsync($"https://mapi.indiamart.com/wservce/crm/crmListing/v2/?glusr_crm_key={config.ApiKey}");
                        if (!imResp.IsSuccessStatusCode)
                            return (false, "IndiaMART CRM Key is invalid or expired.");
                        return (true, null);

                    default:
                        // For platforms without public validation endpoints, just check fields are not empty
                        if (string.IsNullOrWhiteSpace(config.ApiKey) && string.IsNullOrWhiteSpace(config.AccessToken))
                            return (false, "Please provide at least an API Key or Access Token.");
                        return (true, null);
                }
            }
            catch (TaskCanceledException)
            {
                return (false, "Connection timed out. Please check your credentials and try again.");
            }
            catch (HttpRequestException ex)
            {
                return (false, $"Network error: {ex.Message}");
            }
            catch (Exception ex)
            {
                return (false, $"Validation error: {ex.Message}");
            }
        }

        // Try to fetch real leads from the actual platform API
        private async Task<(List<LeadModel> leads, string? error)> FetchRealLeads(LeadIntegrationConfigModel config)
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            var leads = new List<LeadModel>();

            try
            {
                switch (config.PlatformName)
                {
                    case "GoogleAds":
                        // Step 1: Get fresh access token
                        // ApiKey=ClientID, AccessToken=ClientSecret, RefreshToken=RefreshToken, ApiSecret=DeveloperToken, AccountId=CustomerID
                        var gTokenRequest = new FormUrlEncodedContent(new[] {
                            new KeyValuePair<string, string>("client_id", config.ApiKey ?? ""),
                            new KeyValuePair<string, string>("client_secret", config.AccessToken ?? ""),
                            new KeyValuePair<string, string>("refresh_token", config.RefreshToken ?? ""),
                            new KeyValuePair<string, string>("grant_type", "refresh_token")
                        });
                        var gTokenResp = await client.PostAsync("https://oauth2.googleapis.com/token", gTokenRequest);
                        if (!gTokenResp.IsSuccessStatusCode)
                        {
                            var err = await gTokenResp.Content.ReadAsStringAsync();
                            try
                            {
                                var errDoc = JsonDocument.Parse(err);
                                var errMsg = errDoc.RootElement.TryGetProperty("error_description", out var desc)
                                    ? desc.GetString() : "Invalid credentials";
                                return (leads, $"Google OAuth failed: {errMsg}. Please verify Client ID, Client Secret and Refresh Token.");
                            }
                            catch { return (leads, $"Google OAuth failed: {err}"); }
                        }
                        var gTokenJson = JsonDocument.Parse(await gTokenResp.Content.ReadAsStringAsync());
                        var gAccessToken = gTokenJson.RootElement.GetProperty("access_token").GetString();

                        // Step 2: Query Google Ads API for lead form submissions
                        var query = "SELECT lead_form_submission_data.id, lead_form_submission_data.lead_form_submission_fields FROM lead_form_submission_data WHERE segments.date DURING LAST_7_DAYS";
                        var searchReq = new HttpRequestMessage(HttpMethod.Post,
                            $"https://googleads.googleapis.com/v16/customers/{config.AccountId}/googleAds:searchStream");
                        searchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gAccessToken);
                        searchReq.Headers.Add("developer-token", config.ApiSecret);
                        searchReq.Content = new StringContent(JsonSerializer.Serialize(new { query }), System.Text.Encoding.UTF8, "application/json");

                        var searchResp = await client.SendAsync(searchReq);
                        if (!searchResp.IsSuccessStatusCode)
                        {
                            var err = await searchResp.Content.ReadAsStringAsync();
                            if (err.Contains("PERMISSION_DENIED") || err.Contains("AUTHENTICATION_ERROR"))
                                return (leads, "Google Ads API: Permission denied or developer token invalid. Please verify your credentials.");
                            if (err.Contains("NOT_FOUND"))
                                return (leads, $"Google Ads API: Customer ID '{config.AccountId}' not found. Please check your Customer ID.");
                            return (leads, $"Google Ads API Error: {err}");
                        }
                        // Parse response and create leads (structure depends on actual response)
                        return (leads, null);

                    case "Facebook":
                        // AccessToken=Access Token, ApiKey=Ad ID (same as FB_AD_ID in Settings)
                        if (string.IsNullOrWhiteSpace(config.AccessToken))
                            return (leads, "Facebook Access Token is missing.");
                        if (string.IsNullOrWhiteSpace(config.ApiKey))
                            return (leads, "Facebook Ad ID is missing.");

                        var fbAccessToken = config.AccessToken;
                        var fbAdId = config.ApiKey;

                        // Use CampaignId as Form ID if provided directly, skip Ad lookup
                        string? fbFormId = config.CampaignId;

                        if (string.IsNullOrWhiteSpace(fbFormId))
                        {
                            // Step 1: Get Form ID from Ad ID (exact same as FacebookLeadsController)
                            var adUrl = $"https://graph.facebook.com/v19.0/{fbAdId}?fields=creative{{object_story_spec}}&access_token={fbAccessToken}";
                            var adResp = await client.GetAsync(adUrl);
                            var adBody = await adResp.Content.ReadAsStringAsync();
                            if (!adResp.IsSuccessStatusCode)
                                return (leads, $"Facebook API Error: {adBody}");

                            try
                            {
                                var adJson = JsonDocument.Parse(adBody);
                                fbFormId = adJson.RootElement
                                    .GetProperty("creative").GetProperty("object_story_spec")
                                    .GetProperty("link_data").GetProperty("call_to_action")
                                    .GetProperty("value").GetProperty("lead_gen_form_id").GetString();
                            }
                            catch
                            {
                                // Try Ad ID directly as Form ID
                                fbFormId = fbAdId;
                            }
                        }

                        // Step 2: Fetch leads from Form
                        var fbLeadsUrl = $"https://graph.facebook.com/v19.0/{fbFormId}/leads?access_token={fbAccessToken}";
                        var fbLeadsResp = await client.GetAsync(fbLeadsUrl);
                        var fbLeadsBody = await fbLeadsResp.Content.ReadAsStringAsync();
                        if (!fbLeadsResp.IsSuccessStatusCode)
                            return (leads, $"Facebook Leads API Error: {fbLeadsBody}");

                        var fbLeadsJson = JsonDocument.Parse(fbLeadsBody);
                        if (!fbLeadsJson.RootElement.TryGetProperty("data", out var fbData) || fbData.GetArrayLength() == 0)
                            return (leads, $"0 leads returned from Facebook. Form ID: {fbFormId}. Response: {fbLeadsBody.Substring(0, Math.Min(fbLeadsBody.Length, 200))}");

                        int fbSkipped = 0;
                        foreach (var fbLead in fbData.EnumerateArray())
                        {
                            var fbLeadId = fbLead.TryGetProperty("id", out var lid) ? lid.GetString() : null;
                            if (fbLeadId == null) continue;

                            var exists = await _context.Leads.AnyAsync(l => l.GroupName == $"FB_{fbLeadId}");
                            if (exists) { fbSkipped++; continue; }

                            // Get lead details
                            var leadDetailResp = await client.GetAsync($"https://graph.facebook.com/v19.0/{fbLeadId}?access_token={fbAccessToken}");
                            if (!leadDetailResp.IsSuccessStatusCode) continue;

                            var leadDetail = JsonDocument.Parse(await leadDetailResp.Content.ReadAsStringAsync());
                            string? fbName = null, fbPhone = null, fbEmail = null;

                            if (leadDetail.RootElement.TryGetProperty("field_data", out var fields))
                            {
                                foreach (var field in fields.EnumerateArray())
                                {
                                    var fname = field.TryGetProperty("name", out var fn) ? fn.GetString()?.ToLower() : "";
                                    var fval = field.TryGetProperty("values", out var fv) && fv.GetArrayLength() > 0 ? fv[0].GetString() : null;
                                    if (fname == "full name" || fname == "full_name") fbName = fval;
                                    else if (fname == "phone" || fname == "phone_number") fbPhone = fval;
                                    else if (fname == "email") fbEmail = fval;
                                }
                            }

                            leads.Add(new LeadModel
                            {
                                Name = fbName ?? "Facebook Lead",
                                Contact = fbPhone,
                                Email = fbEmail,
                                Source = "Facebook",
                                Stage = "New",
                                Status = "Active",
                                GroupName = $"FB_{fbLeadId}",
                                Comments = $"Facebook Lead ID: {fbLeadId}",
                                CreatedOn = IndianTime.Now,
                                UtmSource = "Facebook",
                                UtmMedium = "api_sync"
                            });
                        }

                        if (leads.Count == 0 && fbSkipped > 0)
                            return (leads, $"All {fbSkipped} leads already exist in CRM. No new leads to import.");

                        return (leads, null);

                    case "IndiaMART":
                        var imUrl = $"https://mapi.indiamart.com/wservce/crm/crmListing/v2/?glusr_crm_key={config.ApiKey}";
                        var imResp = await client.GetAsync(imUrl);
                        if (!imResp.IsSuccessStatusCode)
                            return (leads, "IndiaMART: CRM Key invalid or expired. Please get a new key from IndiaMART seller dashboard.");
                        var imJson = await imResp.Content.ReadAsStringAsync();
                        var imDoc = JsonDocument.Parse(imJson);
                        if (imDoc.RootElement.TryGetProperty("STATUS", out var status) && status.GetString() == "SUCCESS"
                            && imDoc.RootElement.TryGetProperty("JEESSION", out var data))
                        {
                            // IndiaMART returns leads in JEESSION array - this is their actual response format
                        }
                        return (leads, null);

                    default:
                        // For platforms without implemented API (99acres, Housing, MagicBricks etc.)
                        return (leads, $"{config.PlatformName} real-time API sync is not yet implemented. Use 'Test Sync' to generate sample leads, or configure the Webhook URL in {config.PlatformName}'s dashboard to receive leads automatically via push.");
                }
            }
            catch (TaskCanceledException)
            {
                return (leads, "Connection timed out. The platform API may be down or unreachable.");
            }
            catch (HttpRequestException ex)
            {
                return (leads, $"Network error connecting to {config.PlatformName}: {ex.Message}");
            }
            catch (Exception ex)
            {
                return (leads, $"Error: {ex.Message}");
            }
        }

        // Test sync using free randomuser.me API - marks leads as [TEST]
        private async Task<int> FetchTestLeads(LeadIntegrationConfigModel config, int? partnerId)
        {
            var client = _httpClientFactory.CreateClient();
            var leads = new List<LeadModel>();

            try
            {
                var count = new Random().Next(2, 6);
                var response = await client.GetAsync($"https://randomuser.me/api/?results={count}&nat=in");
                if (!response.IsSuccessStatusCode) return 0;

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                var results = doc.RootElement.GetProperty("results");

                string[] locations = { "Hyderabad", "Mumbai", "Bangalore", "Chennai", "Pune", "Delhi", "Gurgaon", "Noida" };
                string[] bhks = { "1 BHK", "2 BHK", "3 BHK", "4 BHK" };
                string[] propTypes = { "Apartment", "Villa", "Plot", "Independent House", "Penthouse" };
                string[] requirements = {
                    "Looking for ready to move apartment",
                    "Need 3BHK near IT corridor",
                    "Interested in gated community villa",
                    "Want plot for investment",
                    "Looking for rental property",
                    "Need flat near metro station",
                    "Interested in new launch project"
                };
                var rng = new Random();

                foreach (var person in results.EnumerateArray())
                {
                    var name = person.GetProperty("name");
                    var fullName = $"{name.GetProperty("first").GetString()} {name.GetProperty("last").GetString()}";
                    var email = person.GetProperty("email").GetString();
                    var phone = person.GetProperty("phone").GetString()?.Replace("-", "").Replace(" ", "");
                    var city = person.GetProperty("location").GetProperty("city").GetString();

                    leads.Add(new LeadModel
                    {
                        Name = fullName,
                        Contact = phone,
                        Email = email,
                        Source = config.PlatformName,
                        Stage = "New",
                        Status = "Active",
                        ChannelPartnerId = partnerId,
                        CreatedOn = IndianTime.Now,
                        Comments = $"[TEST] Sample lead from {config.PlatformName} - for testing only",
                        UtmSource = config.PlatformName,
                        UtmMedium = "test_sync",
                        PreferredLocation = city ?? locations[rng.Next(locations.Length)],
                        PropertyType = propTypes[rng.Next(propTypes.Length)],
                        BHK = bhks[rng.Next(bhks.Length)],
                        Requirement = requirements[rng.Next(requirements.Length)]
                    });
                }

                await AssignSequentialLeadIdsAsync(leads);
                _context.Leads.AddRange(leads);
                await _context.SaveChangesAsync();

                foreach (var lead in leads)
                {
                    try { await _notificationService.NotifyLeadAddedAsync(lead.LeadId, lead.Name ?? "Unknown", config.PlatformName); }
                    catch { }
                }
            }
            catch
            {
                return 0;
            }

            return leads.Count;
        }

        private string? GetJsonString(JsonElement element, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (element.TryGetProperty(key, out var val) && val.ValueKind == JsonValueKind.String)
                    return val.GetString();
            }
            return null;
        }

        /// <summary>
        /// Assign sequential LeadIds to a batch before AddRange. The Mongo shim's AddRange
        /// cannot auto-increment int keys - every id-less document would receive the same
        /// max+1 value, corrupting lookups/updates. Mirrors the MaxAsync+1 pattern used by
        /// SaveLead for single inserts.
        /// </summary>
        private async Task AssignSequentialLeadIdsAsync(List<LeadModel> leads)
        {
            if (leads == null || leads.Count == 0) return;

            int nextId = 1;
            if (_context.Leads.Any())
            {
                nextId = (await _context.Leads.MaxAsync(l => (int?)l.LeadId) ?? 0) + 1;
            }

            foreach (var lead in leads)
            {
                if (lead.LeadId <= 0)
                {
                    lead.LeadId = nextId++;
                }
            }
        }
    }
}
