using Microsoft.AspNetCore.Mvc;
using CRM.Services;
using System.Security.Claims;

namespace CRM.Controllers
{
    public class FcmController : Controller
    {
        private readonly FcmService _fcmService;        private readonly AppDbContext _context;
        private readonly ILogger<FcmController> _logger;
        public FcmController(FcmService fcmService, AppDbContext context, ILogger<FcmController> logger)
        {
            _fcmService = fcmService;
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> TestPush()
        {
            var results = new List<string>();

            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            results.Add($"1. Current UserId claim: {uid ?? "NULL"}");

            if (!int.TryParse(uid, out int userId) || userId <= 0)
            {
                results.Add("FAILED: No valid UserId found in claims");
                return Json(new { results });
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            results.Add($"2. User: {user?.Username ?? "NULL"}, DeviceToken: {(string.IsNullOrEmpty(user?.DeviceToken) ? "NULL" : user.DeviceToken.Substring(0, 20) + "...")}");

            if (string.IsNullOrEmpty(user?.DeviceToken))
            {
                results.Add("FAILED: No DeviceToken for this user");
                return Json(new { results });
            }

            try
            {
                results.Add($"3. Firebase initialized: {FirebaseAdmin.FirebaseApp.DefaultInstance != null}");
                try
                {
                    var message = new FirebaseAdmin.Messaging.Message()
                    {
                        Token = user.DeviceToken,
                        Notification = new FirebaseAdmin.Messaging.Notification
                        {
                            Title = "Test Push",
                            Body = "If you see this, FCM works!"
                        }
                    };
                    var response = await FirebaseAdmin.Messaging.FirebaseMessaging.DefaultInstance.SendAsync(message);
                    results.Add($"4. FCM SUCCESS: {response}");
                }
                catch (FirebaseAdmin.Messaging.FirebaseMessagingException fex)
                {
                    results.Add($"4. FCM MessagingError: {fex.MessagingErrorCode} - {fex.Message}");
                }
                catch (Exception sendEx)
                {
                    results.Add($"4. FCM SendError: {sendEx.Message}");
                }
            }
            catch (Exception ex)
            {
                results.Add($"3. FCM ERROR: {ex.Message}");
                results.Add($"4. Inner: {ex.InnerException?.Message}");
            }

            return Json(new { results });
        }

        [HttpPost]
        public async Task<IActionResult> SaveToken(string token)
        {
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(uid, out int userId) && userId > 0)
            {
                await _fcmService.SaveDeviceToken(userId, token);
                return Json(new { success = true });
            }

            // Fallback: try session/cookie
            var username = HttpContext.Session.GetString("Username") ?? HttpContext.Request.Cookies["Username"];
            if (!string.IsNullOrEmpty(username))
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
                if (user != null)
                {
                    await _fcmService.SaveDeviceToken(user.UserId, token);
                    return Json(new { success = true });
                }
            }

            return Json(new { success = false });
        }

        [HttpPost]
        public async Task<IActionResult> SendTestNotification(string title, string body)
        {
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(uid, out int userId) && userId > 0)
            {
                var success = await _fcmService.SendNotificationToUser(userId, title, body);
                return Json(new { success });
            }
            return Json(new { success = false });
        }
    }
}