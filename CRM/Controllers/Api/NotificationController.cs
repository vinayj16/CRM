using CRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CRM.Controllers.Api
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class NotificationController : ControllerBase
    {
        private readonly FcmService _fcmService;

        public NotificationController(FcmService fcmService)
        {
            _fcmService = fcmService;
        }

        [HttpPost("save-token")]
        public async Task<IActionResult> SaveDeviceToken([FromBody] SaveTokenRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == 0)
                {
                    return Unauthorized(new { success = false, message = "User not authenticated" });
                }

                if (string.IsNullOrEmpty(request.Token))
                {
                    return BadRequest(new { success = false, message = "Token is required" });
                }

                await _fcmService.SaveDeviceToken(userId, request.Token);
                return Ok(new { success = true, message = "Device token saved successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("test-notification")]
        public async Task<IActionResult> SendTestNotification()
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == 0)
                {
                    return Unauthorized(new { success = false, message = "User not authenticated" });
                }

                var success = await _fcmService.SendNotificationToUser(userId, "Test Notification", "This is a test notification from Firebase!");
                
                if (success)
                {
                    return Ok(new { success = true, message = "Test notification sent successfully" });
                }
                else
                {
                    return BadRequest(new { success = false, message = "Failed to send test notification. Make sure you have a valid device token." });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User?.FindFirst("UserId")?.Value ?? 
                            User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            
            if (int.TryParse(userIdClaim, out int userId))
            {
                return userId;
            }
            
            return 0;
        }
    }

    public class SaveTokenRequest
    {
        public string Token { get; set; } = string.Empty;
    }
}
