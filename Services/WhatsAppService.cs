using CRM.Helpers;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using CRM.Models;

namespace CRM.Services
{
    public interface IWhatsAppService
    {
        Task<bool> SendMessageAsync(string phoneNumber, string message);
        Task<bool> SendTemplateMessageAsync(string phoneNumber, string templateName, Dictionary<string, string> parameters);
        Task<bool> SendLeadCreatedMessageAsync(LeadModel lead);
        Task<bool> SendLeadAssignedMessageAsync(LeadModel lead, string assignedToName);
        Task<bool> SendSiteVisitScheduledMessageAsync(LeadModel lead, DateTime visitDate);
        Task<bool> SendQuotationSentMessageAsync(LeadModel lead, int quotationId);
        Task<bool> SendInvoiceSentMessageAsync(LeadModel lead, int invoiceId);
        Task<bool> SendPaymentReceivedMessageAsync(LeadModel lead, decimal amount);
        Task<bool> SendBookingConfirmedMessageAsync(LeadModel lead, string propertyName);
        Task<bool> SendDocumentSentMessageAsync(string phoneNumber, string documentName, string documentUrl);
        Task<bool> SendFollowUpReminderAsync(LeadModel lead, string reminderMessage);
    }

    public class WhatsAppService : IWhatsAppService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<WhatsAppService> _logger;
        private readonly AppDbContext _context;
        private readonly string _apiUrl;
        private readonly string _accountSid;
        private readonly string _authToken;
        private readonly string _fromNumber;
        private readonly bool _enabled;
        private readonly string _provider;
        
        // Meta WhatsApp fields
        private readonly string _metaApiUrl;
        private readonly string _metaAccessToken;
        private readonly string _metaPhoneNumberId;
        private readonly bool _metaEnabled;

        public WhatsAppService(HttpClient httpClient, IConfiguration configuration, ILogger<WhatsAppService> logger, AppDbContext context)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _context = context;
            
            // Read Twilio configuration
            _provider = _configuration["WhatsApp:Provider"] ?? "Twilio";
            _apiUrl = _configuration["WhatsApp:ApiUrl"] ?? "";
            _accountSid = _configuration["WhatsApp:AccountSid"] ?? "";
            _authToken = _configuration["WhatsApp:AuthToken"] ?? "";
            _fromNumber = _configuration["WhatsApp:FromNumber"] ?? "";
            _enabled = _configuration.GetValue<bool>("WhatsApp:Enabled");
            
            // Read Meta WhatsApp configuration
            _metaApiUrl = _configuration["WhatsAppMeta:ApiUrl"] ?? "https://graph.facebook.com/v18.0";
            _metaAccessToken = _configuration["WhatsAppMeta:AccessToken"] ?? "";
            _metaPhoneNumberId = _configuration["WhatsAppMeta:PhoneNumberId"] ?? "";
            _metaEnabled = _configuration.GetValue<bool>("WhatsAppMeta:Enabled");
        }

        public async Task<bool> SendMessageAsync(string phoneNumber, string message)
        {
            try
            {
                // Clean phone number (remove spaces, dashes, etc.)
                phoneNumber = CleanPhoneNumber(phoneNumber);
                
                if (string.IsNullOrEmpty(phoneNumber) || phoneNumber.Length < 10)
                {
                    _logger.LogWarning($"Invalid phone number: {phoneNumber}");
                    return false;
                }

                // Add country code if not present (assuming India +91)
                if (!phoneNumber.StartsWith("+") && !phoneNumber.StartsWith("whatsapp:"))
                {
                    // Check if number already has country code (length > 10 digits)
                    if (phoneNumber.Length <= 10)
                    {
                        // Indian mobile number without country code
                        phoneNumber = "+91" + phoneNumber;
                    }
                    else if (phoneNumber.StartsWith("91") && phoneNumber.Length == 12)
                    {
                        // Number like "919154886214" - add + prefix
                        phoneNumber = "+" + phoneNumber;
                    }
                    else
                    {
                        // Assume it needs +91
                        phoneNumber = "+91" + phoneNumber;
                    }
                }
                else if (phoneNumber.StartsWith("+") && !phoneNumber.StartsWith("whatsapp:"))
                {
                    // Already has + prefix, keep as is
                }

                // Format for WhatsApp (Twilio needs "whatsapp:" prefix, Meta doesn't)
                string formattedPhone = phoneNumber;
                if (_provider == "Twilio" && !phoneNumber.StartsWith("whatsapp:"))
                {
                    formattedPhone = "whatsapp:" + phoneNumber;
                }
                else if (_provider == "Meta" && phoneNumber.StartsWith("whatsapp:"))
                {
                    // Meta doesn't use "whatsapp:" prefix, just the number
                    formattedPhone = phoneNumber.Replace("whatsapp:", "");
                }

                _logger.LogInformation($"Sending WhatsApp via {_provider} to {formattedPhone}: {message}");

                // Check if provider is enabled
                if (_provider == "Twilio" && !_enabled)
                {
                    _logger.LogInformation("Twilio WhatsApp is disabled. Message logged to database only.");
                    await SaveWhatsAppLogAsync(formattedPhone, message, "text", false, "Disabled - Not Sent");
                    return true;
                }
                
                if (_provider == "Meta" && !_metaEnabled)
                {
                    _logger.LogInformation("Meta WhatsApp is disabled. Message logged to database only.");
                    await SaveWhatsAppLogAsync(formattedPhone, message, "text", false, "Disabled - Not Sent");
                    return true;
                }

                // Send via selected provider
                if (_provider == "Twilio")
                {
                    return await SendViaTwilioAsync(formattedPhone, message);
                }
                else if (_provider == "Meta")
                {
                    return await SendViaMetaAsync(formattedPhone, message);
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending WhatsApp to {phoneNumber}");
                await SaveWhatsAppLogAsync(phoneNumber, message, "text", false, ex.Message);
                return false;
            }
        }

        private async Task<bool> SendViaTwilioAsync(string phoneNumber, string message)
        {
            try
            {
                // Twilio API URL
                var url = $"{_apiUrl}/{_accountSid}/Messages.json";

                // Create form data
                var formData = new Dictionary<string, string>
                {
                    { "From", _fromNumber },
                    { "To", phoneNumber },
                    { "Body", message }
                };

                var content = new FormUrlEncodedContent(formData);

                // Add Basic Authentication
                var authBytes = Encoding.ASCII.GetBytes($"{_accountSid}:{_authToken}");
                var authHeader = Convert.ToBase64String(authBytes);

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {authHeader}");

                // Send request
                var response = await _httpClient.PostAsync(url, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"WhatsApp sent successfully via Twilio to {phoneNumber}");
                    await SaveWhatsAppLogAsync(phoneNumber, message, "text", true);
                    return true;
                }
                else
                {
                    _logger.LogError($"Twilio API Error: {response.StatusCode} - {responseBody}");
                    await SaveWhatsAppLogAsync(phoneNumber, message, "text", false, $"HTTP {response.StatusCode}: {responseBody}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending via Twilio");
                await SaveWhatsAppLogAsync(phoneNumber, message, "text", false, ex.Message);
                return false;
            }
        }

        private async Task<bool> SendViaMetaAsync(string phoneNumber, string message)
        {
            try
            {
                // Meta WhatsApp API URL
                var url = $"{_metaApiUrl}/{_metaPhoneNumberId}/messages";

                // Remove "whatsapp:" prefix if present
                phoneNumber = phoneNumber.Replace("whatsapp:", "");
                
                // Remove + from phone number for Meta API
                var cleanPhone = phoneNumber.Replace("+", "");

                // Create Meta WhatsApp message payload
                var payload = new
                {
                    messaging_product = "whatsapp",
                    to = cleanPhone,
                    type = "text",
                    text = new
                    {
                        body = message
                    }
                };

                var jsonContent = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // Add Bearer Token Authentication
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_metaAccessToken}");

                // Send request
                var response = await _httpClient.PostAsync(url, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"WhatsApp sent successfully via Meta to {phoneNumber}");
                    await SaveWhatsAppLogAsync("whatsapp:" + phoneNumber, message, "text", true);
                    return true;
                }
                else
                {
                    _logger.LogError($"Meta WhatsApp API Error: {response.StatusCode} - {responseBody}");
                    await SaveWhatsAppLogAsync("whatsapp:" + phoneNumber, message, "text", false, $"HTTP {response.StatusCode}: {responseBody}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending via Meta WhatsApp");
                await SaveWhatsAppLogAsync(phoneNumber, message, "text", false, ex.Message);
                return false;
            }
        }


        public async Task<bool> SendTemplateMessageAsync(string phoneNumber, string templateName, Dictionary<string, string> parameters)
        {
            try
            {
                phoneNumber = CleanPhoneNumber(phoneNumber);
                
                // Build message from template
                var message = await GetTemplateMessage(templateName, parameters);
                
                return await SendMessageAsync(phoneNumber, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending template WhatsApp to {phoneNumber}");
                return false;
            }
        }

        public async Task<bool> SendLeadCreatedMessageAsync(LeadModel lead)
        {
            var message = $"Dear {lead.Name},\n\n" +
                         $"Thank you for showing interest in our properties! 🏡\n\n" +
                         $"Your inquiry has been received and our team will contact you shortly.\n\n" +
                         $"Reference ID: LEAD-{lead.LeadId}\n" +
                         $"Property Interest: {lead.PropertyType ?? "Any"}\n" +
                         $"Budget: {lead.Requirement ?? "As discussed"}\n\n" +
                         $"For any queries, feel free to reply to this message.\n\n" +
                         $"Best Regards,\n" +
                         $"Your Property Team";

            return await SendMessageAsync(lead.Contact, message);
        }

        public async Task<bool> SendLeadAssignedMessageAsync(LeadModel lead, string assignedToName)
        {
            var message = $"Hello {lead.Name},\n\n" +
                         $"Great news! Your property inquiry has been assigned to *{assignedToName}*, our property consultant.\n\n" +
                         $"They will be reaching out to you shortly to understand your requirements better.\n\n" +
                         $"Reference: LEAD-{lead.LeadId}\n\n" +
                         $"Looking forward to helping you find your dream property!\n\n" +
                         $"Best Regards,\n" +
                         $"Property Team";

            return await SendMessageAsync(lead.Contact, message);
        }

        public async Task<bool> SendSiteVisitScheduledMessageAsync(LeadModel lead, DateTime visitDate)
        {
            var message = $"Dear {lead.Name},\n\n" +
                         $"Your site visit has been scheduled!\n\n" +
                         $"*Date & Time:* {visitDate:dddd, dd MMMM yyyy 'at' hh:mm tt}\n" +
                         $"*Property:* {lead.PreferredLocation ?? "As discussed"}\n" +
                         $"*Reference:* LEAD-{lead.LeadId}\n\n" +
                         $"Please arrive 10 minutes early. Carry a valid ID.\n\n" +
                         $"Need to reschedule? Reply to this message.\n\n" +
                         $"See you soon!";

            return await SendMessageAsync(lead.Contact, message);
        }

        public async Task<bool> SendQuotationSentMessageAsync(LeadModel lead, int quotationId)
        {
            var message = $"Dear {lead.Name},\n\n" +
                         $"Your property quotation is ready!\n\n" +
                         $"*Quotation ID:* QUO-{quotationId}\n" +
                         $"*Lead Reference:* LEAD-{lead.LeadId}\n\n" +
                         $"Please review the quotation details shared via email or download from our portal.\n\n" +
                         $"Have questions? Our team is here to help!\n\n" +
                         $"Best Regards,\n" +
                         $"Property Team";

            return await SendMessageAsync(lead.Contact, message);
        }

        public async Task<bool> SendInvoiceSentMessageAsync(LeadModel lead, int invoiceId)
        {
            var message = $"Dear {lead.Name},\n\n" +
                         $"Your invoice has been generated!\n\n" +
                         $"*Invoice Number:* INV-{invoiceId}\n" +
                         $"*Lead Reference:* LEAD-{lead.LeadId}\n\n" +
                         $"Please check your email for detailed invoice.\n\n" +
                         $"Payment can be made via:\n" +
                         $"• Bank Transfer\n" +
                         $"• Online Payment Portal\n" +
                         $"• Cheque\n\n" +
                         $"For payment assistance, reply to this message.\n\n" +
                         $"Thank you for choosing us!";

            return await SendMessageAsync(lead.Contact, message);
        }

        public async Task<bool> SendPaymentReceivedMessageAsync(LeadModel lead, decimal amount)
        {
            var message = $"Dear {lead.Name},\n\n" +
                         $"Payment Received Successfully!\n\n" +
                         $"*Amount:* ₹{amount:N2}\n" +
                         $"*Reference:* LEAD-{lead.LeadId}\n" +
                         $"*Date:* {IndianTime.Now:dd MMM yyyy}\n\n" +
                         $"Thank you for your payment. Your receipt will be emailed shortly.\n\n" +
                         $"Next steps will be communicated soon.\n\n" +
                         $"Best Regards,\n" +
                         $"Finance Team";

            return await SendMessageAsync(lead.Contact, message);
        }

        public async Task<bool> SendBookingConfirmedMessageAsync(LeadModel lead, string propertyName)
        {
            var message = $"Congratulations {lead.Name}!\n\n" +
                         $"Your property booking has been confirmed!\n\n" +
                         $"*Property:* {propertyName}\n" +
                         $"*Booking ID:* BOOK-{lead.LeadId}\n" +
                         $"*Date:* {IndianTime.Now:dd MMMM yyyy}\n\n" +
                         $"Welcome to our family!\n\n" +
                         $"Our team will contact you for documentation and next steps.\n\n" +
                         $"Thank you for trusting us with your dream property!\n\n" +
                         $"Best Regards,\n" +
                         $"Property Team";

            return await SendMessageAsync(lead.Contact, message);
        }

        public async Task<bool> SendDocumentSentMessageAsync(string phoneNumber, string documentName, string documentUrl)
        {
            var message = $"Document Shared\n\n" +
                         $"*Document:* {documentName}\n\n" +
                         $"You can download it from:\n{documentUrl}\n\n" +
                         $"This link is valid for 7 days.\n\n" +
                         $"Need help? Reply to this message.";

            return await SendMessageAsync(phoneNumber, message);
        }

        public async Task<bool> SendFollowUpReminderAsync(LeadModel lead, string reminderMessage)
        {
            var message = $"Hello {lead.Name},\n\n" +
                         $"Follow-up Reminder\n\n" +
                         $"{reminderMessage}\n\n" +
                         $"*Reference:* LEAD-{lead.LeadId}\n\n" +
                         $"Our team is available to assist you.\n\n" +
                         $"Best Regards,\n" +
                         $"Property Team";

            return await SendMessageAsync(lead.Contact, message);
        }

        // Helper Methods

        private string CleanPhoneNumber(string phoneNumber)
        {
            if (string.IsNullOrEmpty(phoneNumber))
                return string.Empty;

            // Remove all non-numeric characters except +
            return new string(phoneNumber.Where(c => char.IsDigit(c) || c == '+').ToArray());
        }

        private Task<string> GetTemplateMessage(string templateName, Dictionary<string, string> parameters)
        {
            // You can store templates in database or configuration
            // For now, returning a basic template
            var message = $"Template: {templateName}\n";
            foreach (var param in parameters)
            {
                message += $"{param.Key}: {param.Value}\n";
            }
            return Task.FromResult(message);
        }

        private async Task SaveWhatsAppLogAsync(string phoneNumber, string message, string messageType, bool success, string? errorMessage = null)
        {
            try
            {
                var log = new WhatsAppLogModel
                {
                    PhoneNumber = phoneNumber,
                    Message = message,
                    MessageType = messageType,
                    Status = success ? "Sent" : "Failed",
                    ErrorMessage = errorMessage,
                    SentOn = IndianTime.Now
                };

                _context.WhatsAppLogs.Add(log);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving WhatsApp log");
            }
        }
    }
}
