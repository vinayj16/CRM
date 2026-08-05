using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace CRM.Services
{
    public class FcmService
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;
        private static bool _firebaseInitialized = false;
        private static bool _firebaseInitializationAttempted = false;
        private static readonly object _lock = new object();

        public FcmService(AppDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
            EnsureFirebaseInitialized();
        }

        private void EnsureFirebaseInitialized()
        {
            if (_firebaseInitialized) return;

            lock (_lock)
            {
                if (_firebaseInitialized || _firebaseInitializationAttempted) return;
                _firebaseInitializationAttempted = true;

                try
                {
                    var credentialsPath = Path.Combine(Directory.GetCurrentDirectory(), "firebase-credentials.json");
                    if (!File.Exists(credentialsPath))
                    {
                        return;
                    }

                    var credential = GoogleCredential.FromFile(credentialsPath);
                    FirebaseApp.Create(new AppOptions { Credential = credential });
                    _firebaseInitialized = true;
                }
                catch
                {
                    _firebaseInitialized = false;
                }
            }
        }

        public async Task<bool> SendNotificationToUser(int userId, string title, string body, string? link = null, string? type = null, int? relatedEntityId = null)
        {
            if (!_firebaseInitialized) return false;

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user?.DeviceToken == null) return false;

            return await SendNotification(user.DeviceToken, title, body, link, type, relatedEntityId);
        }

        public async Task<bool> SendNotification(string deviceToken, string title, string body, string? link = null, string? type = null, int? relatedEntityId = null)
        {
            try
            {
                if (!_firebaseInitialized)
                {
                    Console.WriteLine("Firebase not initialized");
                    return false;
                }

                // Validate and fix the link URL for Firebase
                var validLink = ValidateAndFixUrl(link);
                Console.WriteLine($"DEBUG: Original link: '{link}', Valid link: '{validLink}'");

                var message = new Message()
                {
                    Token = deviceToken,
                    Notification = new Notification
                    {
                        Title = title ?? "CRM Notification",
                        Body = body ?? ""
                    },
                    Webpush = new WebpushConfig
                    {
                        FcmOptions = new WebpushFcmOptions
                        {
                            Link = validLink
                        }
                    },
                    Data = new Dictionary<string, string>()
                    {
                        { "title", title ?? "" },
                        { "body", body ?? "" },
                        { "link", link ?? "/" }, // Keep original link in data
                        { "type", type ?? "info" },
                        { "relatedEntityId", relatedEntityId?.ToString() ?? "" },
                        { "priority", "Normal" }
                    }
                };

                await FirebaseMessaging.DefaultInstance.SendAsync(message);
                return true;
            }
            catch (FirebaseMessagingException ex)
            {
                if (ex.MessagingErrorCode == MessagingErrorCode.Unregistered)
                {
                    await RemoveInvalidToken(deviceToken);
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private string ValidateAndFixUrl(string? link)
        {
            // Use the deployed BaseUrl (set via appsettings/env var on Railway) so
            // push notification links point at the public domain instead of localhost.
            var baseUrl = _config["BaseUrl"] ?? "https://localhost:5139";

            // Ensure base URL ends with no trailing slash for consistency
            baseUrl = baseUrl.TrimEnd('/');

            // If link is null, empty, or just "#", return default
            if (string.IsNullOrWhiteSpace(link) || link == "#")
            {
                return $"{baseUrl}/home";
            }

            // If it's already a valid HTTPS URL, return as-is
            if (Uri.TryCreate(link, UriKind.Absolute, out Uri? absoluteUri) &&
                (absoluteUri.Scheme == Uri.UriSchemeHttps || absoluteUri.Scheme == Uri.UriSchemeHttp))
            {
                // Convert HTTP to HTTPS for Firebase
                if (absoluteUri.Scheme == Uri.UriSchemeHttp)
                {
                    return absoluteUri.ToString().Replace("http://", "https://");
                }
                return absoluteUri.ToString();
            }

            // If it's a relative URL, convert to absolute HTTPS URL
            if (link.StartsWith("/"))
            {
                return $"{baseUrl}{link}";
            }

            // If it doesn't start with /, add the leading slash
            return $"{baseUrl}/{link.TrimStart('/')}";
        }

        public async Task SaveDeviceToken(int userId, string deviceToken)
        {
            // Clear this token from any other user (e.g. previous login on same device)
            var previousUsers = await _context.Users
                .Where(u => u.DeviceToken == deviceToken && u.UserId != userId)
                .ToListAsync();
            foreach (var prev in previousUsers)
            {
                prev.DeviceToken = null;
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user != null)
            {
                user.DeviceToken = deviceToken;
                await _context.SaveChangesAsync();
            }
        }

        private async Task RemoveInvalidToken(string deviceToken)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.DeviceToken == deviceToken);
            if (user != null)
            {
                user.DeviceToken = null;
                await _context.SaveChangesAsync();
            }
        }
    }
}