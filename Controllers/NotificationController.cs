using CRM.Helpers;
using CRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CRM.Controllers
{
    [Authorize]
    public class NotificationController : Controller
    {
        private readonly INotificationService _notificationService;
        private readonly ILogger<NotificationController> _logger;
        public NotificationController(INotificationService notificationService, ILogger<NotificationController> logger)
        {
            _notificationService = notificationService;
            _logger = logger;
        }

        [HttpGet]
        [Route("Notifications/Index")]
        [Route("Notification/Index")]
        public IActionResult Index()
        {
            return RedirectToAction("GetNotifications");
        }

        [HttpGet]
        public async Task<IActionResult> GetNotifications()
        {
            try
            {
                var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var role = User?.FindFirst(ClaimTypes.Role)?.Value ?? "";

                if (!int.TryParse(uid, out int userId))
                {
                    return Json(new { success = false, message = "Invalid user" });
                }

                var notifications = await _notificationService.GetUserNotificationsAsync(userId, role);
                var unreadCount = await _notificationService.GetUnreadCountAsync(userId, role);

                var notificationData = notifications.Select(n => new
                {
                    id = n.NotificationId,
                    title = n.Title,
                    message = n.Message,
                    type = n.Type,
                    link = n.Link,
                    priority = n.Priority,
                    createdOn = n.CreatedOn.ToString("MMM dd, HH:mm"),
                    isRead = n.IsRead,
                    relatedEntityType = n.RelatedEntityType,
                    relatedEntityId = n.RelatedEntityId
                }).ToList();

                return Json(new
                {
                    success = true,
                    count = unreadCount,
                    notifications = notificationData
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> MarkAsRead(int notificationId)
        {
            try
            {
                await _notificationService.MarkAsReadAsync(notificationId);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> MarkAllAsRead()
        {
            try
            {
                var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (!int.TryParse(uid, out int userId))
                {
                    return Json(new { success = false, message = "Invalid user" });
                }

                await _notificationService.MarkAllAsReadAsync(userId);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateTestNotification()
        {
            try
            {
                var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var username = User?.FindFirst(ClaimTypes.Name)?.Value ?? User?.Identity?.Name ?? "User";

                if (!int.TryParse(uid, out int userId))
                {
                    return Json(new { success = false, message = "Invalid user" });
                }

                await _notificationService.CreateTestNotificationAsync(userId);
                return Json(new { success = true, message = "Test notification created successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ClearAllNotifications()
        {
            try
            {
                var role = User?.FindFirst(ClaimTypes.Role)?.Value;

                // Only allow Admin to clear all notifications
                if (role?.ToLower() != "admin")
                {
                    return Json(new { success = false, message = "Only admins can clear all notifications" });
                }

                // Add method to notification service
                await _notificationService.ClearAllNotificationsAsync();

                return Json(new { success = true, message = "All notifications cleared successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateAgentTestNotifications(int agentId)
        {
            try
            {
                var role = User?.FindFirst(ClaimTypes.Role)?.Value;

                // Only allow Admin to create test notifications for agents
                if (role?.ToLower() != "admin")
                {
                    return Json(new { success = false, message = "Only admins can create test notifications" });
                }

                // Create multiple test notifications for the agent
                await _notificationService.CreateNotificationAsync(
                    "Lead Assignment Test #1",
                    "You have been assigned Lead #101 - John Doe for immediate follow-up",
                    "LeadAssigned",
                    agentId,
                    $"/leaddetails/{IdObfuscator.Encode(101)}",
                    101,
                    "Lead",
                    "High"
                );

                await _notificationService.CreateNotificationAsync(
                    "Lead Assignment Test #2",
                    "You have been assigned Lead #102 - Jane Smith for site visit",
                    "LeadAssigned",
                    agentId,
                    $"/leaddetails/{IdObfuscator.Encode(102)}",
                    102,
                    "Lead",
                    "High"
                );

                await _notificationService.CreateNotificationAsync(
                    "Follow-up Reminder",
                    "You have 3 follow-ups due today. Please check your tasks.",
                    "FollowUpDue",
                    agentId,
                    "/Tasks/Index",
                    null,
                    null,
                    "Medium"
                );

                return Json(new { success = true, message = $"Test notifications created for agent {agentId}!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

    }
}
