using System.Net;
using System.Net.Mail;
using CRM.Models;

namespace CRM.Services
{
    public class EmailService
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        public EmailService(AppDbContext context, IConfiguration config, ILogger<EmailService> logger)
        {
            _context = context;
            _config = config;
            _logger = logger;
        }

        public async Task<(string from, string password)> GetEmailCredentials(int userId)
        {
            // Note: FindAsync(int) searches MongoDB by _id (ObjectId) which won't match int UserId
            // Use FirstOrDefaultAsync with lambda to search by the correct UserId field

            // First check if there are user-specific email settings
            var emailSetting = await _context.EmailSettings.FirstOrDefaultAsync(e => e.UserId == userId);
            if (emailSetting != null)
                return (emailSetting.SmtpFrom, emailSetting.SmtpPassword);

            // Check partner settings if user is an Agent with a ChannelPartner
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user != null && user.Role == "Agent" && user.ChannelPartnerId.HasValue)
            {
                var partnerSetting = await _context.EmailSettings.FirstOrDefaultAsync(e => e.UserId == user.ChannelPartnerId.Value);
                if (partnerSetting != null)
                    return (partnerSetting.SmtpFrom, partnerSetting.SmtpPassword);
            }

            // Fallback to global config from appsettings.json
            return (_config["EmailSettings:From"], _config["EmailSettings:Password"]);
        }

        public async Task SendEmailAsync(int userId, string toEmail, string subject, string body, string? templateName = null, string? category = null)
        {
            var (from, password) = await GetEmailCredentials(userId);
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(password))
                throw new Exception("Email credentials not configured");

            using var smtp = new SmtpClient("smtp.gmail.com", 587)
            {
                Credentials = new NetworkCredential(from, password),
                EnableSsl = true
            };

            var mail = new MailMessage(from, toEmail, subject, body) { IsBodyHtml = true };
            await smtp.SendMailAsync(mail);

            // Log the sent email
            try
            {
                _context.EmailLogs.Add(new EmailLogModel
                {
                    ToEmail = toEmail,
                    Subject = subject,
                    BodyPreview = body.Length > 200 ? body.Substring(0, 200) + "..." : body,
                    TemplateName = templateName,
                    UserId = userId > 0 ? userId : null,
                    SentByUser = "System",
                    SentByRole = "System",
                    Status = "Sent",
                    SentOn = DateTime.UtcNow,
                    Category = category ?? "General"
                });
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to log sent email");
            }
        }

        /// <summary>
        /// Log an email that was sent externally (e.g. via raw SMTP in a controller).
        /// </summary>
        public async Task LogEmailAsync(string toEmail, string subject, string status, string? errorMessage = null, string? category = null, string? templateName = null)
        {
            try
            {
                _context.EmailLogs.Add(new EmailLogModel
                {
                    ToEmail = toEmail,
                    Subject = subject,
                    Status = status,
                    ErrorMessage = errorMessage,
                    TemplateName = templateName,
                    SentOn = DateTime.UtcNow,
                    Category = category ?? "General"
                });
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to log email");
            }
        }

        /// <summary>
        /// Send an email using a named template from the EmailTemplates collection.
        /// Looks up the template by name, replaces variables, and sends the composed email.
        /// </summary>
        /// <param name="templateName">The TemplateName field in EmailTemplates collection</param>
        /// <param name="toEmail">Recipient email address</param>
        /// <param name="userId">User ID for credential lookup (0 = use config fallback)</param>
        /// <param name="variables">Dictionary of variable replacements: {Name} -> value, {Link} -> value</param>
        /// <param name="category">Category for email log (e.g. "Welcome", "Subscription", "Team")</param>
        public async Task<bool> SendTemplateEmailAsync(string templateName, string toEmail, int userId = 0, Dictionary<string, string>? variables = null, string? category = null)
        {
            try
            {
                // Find the active template
                var template = await _context.EmailTemplates.FirstOrDefaultAsync(t => t.TemplateName == templateName && t.IsActive);
                if (template == null)
                {
                    _logger.LogWarning("Email template '{TemplateName}' not found or inactive", templateName);
                    return false;
                }

                // Replace variables in subject and body
                var subject = template.Subject;
                var body = template.BodyHtml;

                if (variables != null)
                {
                    foreach (var kvp in variables)
                    {
                        var placeholder = "{" + kvp.Key + "}";
                        subject = subject.Replace(placeholder, kvp.Value ?? "");
                        body = body.Replace(placeholder, kvp.Value ?? "");
                    }
                }

                // Send via existing method (handles credentials + logging)
                await SendEmailAsync(userId, toEmail, subject, body, templateName, category ?? "Template");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send template email '{TemplateName}' to {ToEmail}", templateName, toEmail);
                return false;
            }
        }

        /// <summary>
        /// Generate a dynamic reset/payment link using the current request context.
        /// Avoids hardcoded BaseUrl values.
        /// </summary>
        public static string GetBaseUrl(HttpRequest request)
        {
            return $"{request.Scheme}://{request.Host}";
        }
    }
}
