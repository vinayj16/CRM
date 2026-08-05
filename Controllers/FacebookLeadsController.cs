using CRM.Models;
using CRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace CRM.Controllers
{
    [Route("api/facebook")]
    [ApiController]
    [Authorize] // Add authorization to ensure user context
    public class FacebookLeadsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<FacebookLeadsController> _logger;
        private readonly INotificationService _notificationService;
        private readonly SubscriptionService _subscriptionService;
        private readonly HttpClient _httpClient;

        public FacebookLeadsController(AppDbContext context, ILogger<FacebookLeadsController> logger, INotificationService notificationService, SubscriptionService subscriptionService, HttpClient httpClient)
        {
            _context = context;
            _logger = logger;
            _notificationService = notificationService;
            _subscriptionService = subscriptionService;
            _httpClient = httpClient;
        }

        // GET: api/facebook/fetch-leads-bg - Background service endpoint
        [HttpGet("fetch-leads-bg")]
        [AllowAnonymous]
        public async Task<IActionResult> FetchLeadsBackground()
        {
            try
            {
                var settings = await _context.Settings
                    .Where(s => s.SettingKey == "FB_ACCESS_TOKEN" || s.SettingKey == "FB_AD_ID")
                    .ToListAsync();
                    
                var accessToken = settings.FirstOrDefault(s => s.SettingKey == "FB_ACCESS_TOKEN")?.SettingValue;
                var adId = settings.FirstOrDefault(s => s.SettingKey == "FB_AD_ID")?.SettingValue;
                
                if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(adId))
                {
                    return BadRequest(new { error = "Facebook settings not configured." });
                }
                
                var formId = await GetFormIdFromAdId(adId, accessToken);
                if (string.IsNullOrEmpty(formId))
                {
                    return BadRequest(new { error = "Could not retrieve Form ID from Ad ID" });
                }
                
                var url = $"https://graph.facebook.com/v19.0/{formId}/leads?access_token={accessToken}";
                var response = await _httpClient.GetStringAsync(url);
                
                var leadsResponse = JsonSerializer.Deserialize<FacebookLeadsResponse>(response);
                if (leadsResponse?.Data == null || !leadsResponse.Data.Any())
                {
                    return Ok(new { message = "No leads found", formId });
                }
                
                var processedLeads = new List<object>();
                var duplicateCount = 0;
                
                foreach (var leadSummary in leadsResponse.Data)
                {
                    var existingLead = await _context.Leads
                        .FirstOrDefaultAsync(l => l.GroupName == $"FB_{leadSummary.Id}");
                    
                    if (existingLead == null)
                    {
                        var leadData = await GetLeadData(leadSummary.Id, accessToken);
                        if (leadData != null)
                        {
                            var fullName = GetFieldValue(leadData.FieldData, "full name") ?? "";
                            var phone = GetFieldValue(leadData.FieldData, "phone") ?? "";
                            var email = GetFieldValue(leadData.FieldData, "email") ?? "";
                            
                            var lead = new LeadModel
                            {
                                Name = fullName,
                                Contact = phone,
                                Email = email,
                                Source = "Facebook API",
                                Status = "Active",
                                Stage = "New",
                                GroupName = $"FB_{leadSummary.Id}",
                                Comments = $"Facebook Lead ID: {leadSummary.Id}",
                                ChannelPartnerId = null,
                                HandoverStatus = "Admin",
                                IsReadyToBook = false
                            };
                            
                            _context.Leads.Add(lead);
                            await _context.SaveChangesAsync();
                            
                            await _notificationService.NotifyLeadAddedAsync(
                                lead.LeadId,
                                lead.Name ?? "Unknown Lead",
                                "Facebook API"
                            );
                            
                            processedLeads.Add(new { leadId = lead.LeadId, name = lead.Name, status = "created" });
                        }
                    }
                    else
                    {
                        duplicateCount++;
                    }
                }
                
                return Ok(new 
                {
                    success = true,
                    adId,
                    formId,
                    totalLeads = leadsResponse.Data.Count,
                    newLeads = processedLeads.Count,
                    duplicates = duplicateCount,
                    message = $"{processedLeads.Count} new leads processed, {duplicateCount} duplicates skipped",
                    leads = processedLeads
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error fetching leads: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET: api/facebook/fetch-leads - Dynamic fetch using Ad ID
        [HttpGet("fetch-leads")]
        [AllowAnonymous] // Temporarily allow anonymous for background service
        public async Task<IActionResult> FetchLeads()
        {
            try
            {
                // Get current user's context for partner-specific settings
                var userIdClaim = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                int? channelPartnerId = null;
                bool isAdmin = false;
                
                if (int.TryParse(userIdClaim, out int userId))
                {
                    var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
                    channelPartnerId = currentUser?.ChannelPartnerId;
                    isAdmin = currentUser?.Role == "Admin";
                }
                
                // Check if partner has Facebook integration feature access
                if (channelPartnerId.HasValue && !isAdmin)
                {
                    var hasAccess = await _subscriptionService.HasFeatureAccessAsync(channelPartnerId.Value, "facebook");
                    if (!hasAccess)
                    {
                        return BadRequest(new { 
                            error = "Facebook integration not available in your current plan. Please upgrade to access this feature.",
                            showUpgrade = true,
                            upgradeUrl = "/Subscription/MyPlan"
                        });
                    }
                }
                
                // Get settings from database based on user context
                var accessToken = await GetSetting("FB_ACCESS_TOKEN", channelPartnerId);
                var adId = await GetSetting("FB_AD_ID", channelPartnerId);
                
                if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(adId))
                {
                    return BadRequest(new { error = "Facebook settings not configured. Please contact admin." });
                }
                
                // Step 1: Get Form ID from Ad ID
                var formId = await GetFormIdFromAdId(adId, accessToken);
                if (string.IsNullOrEmpty(formId))
                {
                    return BadRequest(new { error = "Could not retrieve Form ID from Ad ID" });
                }
                
                // Step 2: Fetch leads from Form ID
                var url = $"https://graph.facebook.com/v19.0/{formId}/leads?access_token={accessToken}";
                var response = await _httpClient.GetStringAsync(url);
                
                var leadsResponse = JsonSerializer.Deserialize<FacebookLeadsResponse>(response);
                if (leadsResponse?.Data == null || !leadsResponse.Data.Any())
                {
                    return Ok(new { message = "No leads found", formId });
                }
                
                var processedLeads = new List<object>();
                var duplicateCount = 0;
                
                foreach (var leadSummary in leadsResponse.Data)
                {
                    var existingLead = await _context.Leads
                        .FirstOrDefaultAsync(l => l.GroupName == $"FB_{leadSummary.Id}");
                    
                    if (existingLead == null)
                    {
                        // Check subscription limits for partners
                        if (channelPartnerId.HasValue)
                        {
                            var (canAdd, message) = await _subscriptionService.CanAddLeadAsync(channelPartnerId.Value);
                            if (!canAdd)
                            {
                                return BadRequest(new { error = message });
                            }
                        }
                        
                        var leadData = await GetLeadData(leadSummary.Id, accessToken);
                        if (leadData != null)
                        {
                            var fullName = GetFieldValue(leadData.FieldData, "full name") ?? "";
                            var phone = GetFieldValue(leadData.FieldData, "phone") ?? "";
                            var email = GetFieldValue(leadData.FieldData, "email") ?? "";
                            
                            var lead = new LeadModel
                            {
                                Name = fullName,
                                Contact = phone,
                                Email = email,
                                Source = "Facebook API",
                                Status = "Active",
                                Stage = "New",
                                // CreatedOn will use database default
                                GroupName = $"FB_{leadSummary.Id}",
                                Comments = $"Facebook Lead ID: {leadSummary.Id}",
                                ChannelPartnerId = channelPartnerId,
                                HandoverStatus = isAdmin ? "Admin" : "Partner",
                                IsReadyToBook = false
                            };
                            
                            _context.Leads.Add(lead);
                            await _context.SaveChangesAsync();
                            
                            await _notificationService.NotifyLeadAddedAsync(
                                lead.LeadId,
                                lead.Name ?? "Unknown Lead",
                                "Facebook API"
                            );
                            
                            processedLeads.Add(new { leadId = lead.LeadId, name = lead.Name, status = "created" });
                        }
                    }
                    else
                    {
                        duplicateCount++;
                    }
                }
                
                return Ok(new 
                {
                    success = true,
                    adId,
                    formId,
                    totalLeads = leadsResponse.Data.Count,
                    newLeads = processedLeads.Count,
                    duplicates = duplicateCount,
                    message = $"{processedLeads.Count} new leads processed, {duplicateCount} duplicates skipped",
                    leads = processedLeads
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error fetching leads: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private async Task<string?> GetFormIdFromAdId(string adId, string accessToken)
        {
            try
            {
                var url = $"https://graph.facebook.com/v19.0/{adId}?fields=creative{{object_story_spec}}&access_token={accessToken}";
                var response = await _httpClient.GetStringAsync(url);
                
                _logger.LogInformation($"Ad Creative Response: {response}");
                
                var adData = JsonSerializer.Deserialize<FacebookAdResponse>(response);
                return adData?.Creative?.ObjectStorySpec?.LinkData?.CallToAction?.Value?.LeadGenFormId;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting Form ID: {ex.Message}");
                return null;
            }
        }

        private async Task<FacebookLeadData?> GetLeadData(string leadId, string accessToken)
        {
            try
            {
                var url = $"https://graph.facebook.com/v19.0/{leadId}?access_token={accessToken}";
                var response = await _httpClient.GetStringAsync(url);
                return JsonSerializer.Deserialize<FacebookLeadData>(response);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting lead data: {ex.Message}");
                return null;
            }
        }

        private async Task<string?> GetSetting(string key, int? channelPartnerId = null)
        {
            var setting = await _context.Settings
                .FirstOrDefaultAsync(s => s.SettingKey == key && s.ChannelPartnerId == channelPartnerId);
            return setting?.SettingValue;
        }

        private string? GetFieldValue(List<FacebookFieldData>? fieldData, string fieldName)
        {
            var field = fieldData?.FirstOrDefault(f => 
                f.Name?.Equals(fieldName, StringComparison.OrdinalIgnoreCase) == true);
            return field?.Values?.FirstOrDefault();
        }
    }

    public class FacebookLeadsResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("data")]
        public List<FacebookLeadSummary>? Data { get; set; }
    }
    
    public class FacebookLeadSummary
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string? Id { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("created_time")]
        public string? CreatedTime { get; set; }
    }
    
    public class FacebookLeadData
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string? Id { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("created_time")]
        public string? CreatedTime { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("field_data")]
        public List<FacebookFieldData>? FieldData { get; set; }
    }
    
    public class FacebookFieldData
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("values")]
        public List<string>? Values { get; set; }
    }
    
    public class FacebookAdResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("creative")]
        public FacebookCreative? Creative { get; set; }
    }
    
    public class FacebookCreative
    {
        [System.Text.Json.Serialization.JsonPropertyName("object_story_spec")]
        public FacebookObjectStorySpec? ObjectStorySpec { get; set; }
    }
    
    public class FacebookObjectStorySpec
    {
        [System.Text.Json.Serialization.JsonPropertyName("link_data")]
        public FacebookLinkData? LinkData { get; set; }
    }
    
    public class FacebookLinkData
    {
        [System.Text.Json.Serialization.JsonPropertyName("call_to_action")]
        public FacebookCallToAction? CallToAction { get; set; }
    }
    
    public class FacebookCallToAction
    {
        [System.Text.Json.Serialization.JsonPropertyName("value")]
        public FacebookCallToActionValue? Value { get; set; }
    }
    
    public class FacebookCallToActionValue
    {
        [System.Text.Json.Serialization.JsonPropertyName("lead_gen_form_id")]
        public string? LeadGenFormId { get; set; }
    }
}