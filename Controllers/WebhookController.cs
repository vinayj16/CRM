using CRM.Helpers;
using CRM.Models;
using CRM.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace CRM.Controllers
{
    [Route("api/meta")]
    [ApiController]
    [IgnoreAntiforgeryToken]
    public class WebhookController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<WebhookController> _logger;
        private readonly INotificationService _notificationService;
        private const string VERIFY_TOKEN = "MY_SECRET_TOKEN_123";

        public WebhookController(AppDbContext context, ILogger<WebhookController> logger, INotificationService notificationService)
        {
            _context = context;
            _logger = logger;
            _notificationService = notificationService;
        }

        // GET: api/meta/test - Simple test endpoint
        [HttpGet("test")]
        public IActionResult Test()
        {
            return Ok(new { status = "Webhook is working", timestamp = IndianTime.Now });
        }

        // GET: api/meta/webhook - Facebook Webhook Verification
        [HttpGet("webhook")]
        public IActionResult VerifyWebhook([FromQuery(Name = "hub.mode")] string? mode,
                                          [FromQuery(Name = "hub.verify_token")] string? token,
                                          [FromQuery(Name = "hub.challenge")] string? challenge)
        {
            _logger.LogInformation($"Webhook verification - Mode: {mode}, Token: {token}, Challenge: {challenge}");

            if (mode == "subscribe")
            {
                _logger.LogInformation("Webhook verified successfully - accepting any token for now");
                return Content(challenge ?? "VERIFIED", "text/plain");
            }

            _logger.LogWarning($"Invalid verification request - Mode: {mode}");
            return Unauthorized(new { error = "Invalid verify token" });
        }

        // POST: api/meta/webhook - Receive Lead Data
        [HttpPost("webhook")]
        public async Task<IActionResult> ReceiveWebhook()
        {
            try
            {
                using var reader = new StreamReader(Request.Body);
                var rawBody = await reader.ReadToEndAsync();
                
                _logger.LogInformation($"Webhook received: {rawBody}");

                var webhookData = JsonSerializer.Deserialize<MetaWebhookPayload>(rawBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var leads = new List<object>();

                if (webhookData?.Entry != null)
                {
                    foreach (var entry in webhookData.Entry)
                    {
                        if (entry.Changes != null)
                        {
                            foreach (var change in entry.Changes)
                            {
                                if (change.Field == "leadgen" && change.Value?.FieldData != null)
                                {
                                    var lead = await ProcessWebhookLead(change.Value.FieldData, change.Value);
                                    if (lead != null)
                                    {
                                        leads.Add(lead);
                                    }
                                }
                            }
                        }
                    }
                }

                return Ok(new { status = "success", leadsReceived = leads.Count, leads });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing webhook: {ex.Message}");
                return StatusCode(500, new { status = "error", message = ex.Message });
            }
        }

        // GET: api/meta/leads - Get all Facebook leads from database
        [HttpGet("leads")]
        public async Task<IActionResult> GetLeads()
        {
            try
            {
                var leads = await _context.Leads
                    .Where(l => l.Source == "Facebook Webhook")
                    .OrderByDescending(l => l.CreatedOn)
                    .Select(l => new {
                        Name = l.Name,
                        Phone = l.Contact,
                        Email = l.Email,
                        LeadId = l.GroupName,
                        CreatedTime = l.CreatedOn,
                        Source = l.Source
                    })
                    .ToListAsync();

                return Ok(new { success = true, count = leads.Count, leads });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting leads: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }



        private async Task<object?> ProcessWebhookLead(List<FieldData> fieldData, WebhookValue webhookValue)
        {
            try
            {
                var firstName = GetFieldValue(fieldData, "first_name") ?? "";
                var lastName = GetFieldValue(fieldData, "last_name") ?? "";
                var phone = GetFieldValue(fieldData, "phone_number") ?? "";
                var email = GetFieldValue(fieldData, "email") ?? "";

                var leadModel = new LeadModel
                {
                    Name = $"{firstName} {lastName}".Trim(),
                    Contact = !string.IsNullOrEmpty(phone) ? phone : null,
                    Email = !string.IsNullOrEmpty(email) ? email : null,
                    Source = "Facebook Webhook",
                    Status = "New",
                    Stage = "Lead",
                    CreatedOn = !string.IsNullOrEmpty(webhookValue?.CreatedTime) 
                        ? DateTime.Parse(webhookValue.CreatedTime) 
                        : IndianTime.Now,
                    GroupName = $"FB_{webhookValue?.LeadgenId}",
                    Comments = $"Facebook Lead ID: {webhookValue?.LeadgenId}"
                };

                // Check if lead already exists
                var existingLead = await _context.Leads
                    .FirstOrDefaultAsync(l => l.GroupName == leadModel.GroupName);
                
                if (existingLead == null)
                {
                    _context.Leads.Add(leadModel);
                    await _context.SaveChangesAsync();
                    
                    await _notificationService.NotifyLeadAddedAsync(
                        leadModel.LeadId,
                        leadModel.Name ?? "Unknown Lead",
                        "Facebook Webhook"
                    );
                }

                return new
                {
                    Name = leadModel.Name,
                    Phone = phone,
                    Email = email,
                    LeadId = webhookValue?.LeadgenId,
                    CreatedTime = webhookValue?.CreatedTime,
                    Source = "Facebook Webhook"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing webhook lead: {ex.Message}");
                return null;
            }
        }

        private string? GetFieldValue(List<FieldData> fieldData, string fieldName)
        {
            return fieldData?.FirstOrDefault(f => f.Name?.Equals(fieldName, StringComparison.OrdinalIgnoreCase) == true)?.Values?.FirstOrDefault();
        }
    }

    // Models
    public class MetaWebhookPayload
    {
        public List<WebhookEntry>? Entry { get; set; }
    }

    public class WebhookEntry
    {
        public List<WebhookChange>? Changes { get; set; }
    }

    public class WebhookChange
    {
        public string? Field { get; set; }
        public WebhookValue? Value { get; set; }
    }

    public class WebhookValue
    {
        [System.Text.Json.Serialization.JsonPropertyName("leadgen_id")]
        public string? LeadgenId { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("created_time")]
        public string? CreatedTime { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("field_data")]
        public List<FieldData>? FieldData { get; set; }
    }

    public class FieldData
    {
        public string? Name { get; set; }
        public List<string>? Values { get; set; }
    }
}