using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using CRM.Services;
using CRM.Models;
using CRM.Models.Chatbot;
using System.Security.Claims;

namespace CRM.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatbotController : Controller
    {
        private readonly IChatbotService _chatbotService;
        private readonly AppDbContext _context;
        private readonly SubscriptionService _subscriptionService;
        private readonly ILogger<ChatbotController> _logger;

        public ChatbotController(
            IChatbotService chatbotService,
            AppDbContext context,
            SubscriptionService subscriptionService,
            ILogger<ChatbotController> logger)
        {
            _chatbotService = chatbotService;
            _context = context;
            _subscriptionService = subscriptionService;
            _logger = logger;
        }

        private async Task<bool> CheckCustomAPIFeatureAsync()
        {
            var userId = GetUserId();
            if (userId == null) return true;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId.Value);
            if (user?.ChannelPartnerId == null) return true; // Admin access
            return await _subscriptionService.HasFeatureAccessAsync(user.ChannelPartnerId.Value, "customapi");
        }

        // ✅ FIXED: unique route
        [HttpGet("widget")]
        public IActionResult ChatWidget()
        {
            return PartialView("_ChatWidget");
        }

        // ✅ FIXED: unique route
        [HttpGet("test")]
        public IActionResult TestChatbot()
        {
            return Json(new
            {
                success = true,
                message = "Chatbot controller is working",
                timestamp = DateTime.Now
            });
        }

        [HttpGet("getconversation")]
        public async Task<IActionResult> GetConversation(string sessionId)
        {
            try
            {
                _logger.LogInformation("GetConversation endpoint called with sessionId: {SessionId}", sessionId);

                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    _logger.LogWarning("Session ID is null or whitespace");
                    return BadRequest(new { success = false, error = "Session ID is required" });
                }

                _logger.LogInformation("Calling GetSessionLogsAsync...");
                var messages = await _chatbotService.GetSessionLogsAsync(sessionId);
                _logger.LogInformation("GetSessionLogsAsync returned {Count} messages", messages?.Count ?? 0);

                return Ok(new { success = true, messages });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting conversation history for sessionId: {SessionId}", sessionId);

                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        [HttpPost("uploadimage")]
        public async Task<IActionResult> UploadImage([FromForm] IFormFile image, [FromForm] string sessionId, [FromForm] string message = "")
        {
            try
            {
                if (image == null || image.Length == 0)
                {
                    return BadRequest(new { success = false, error = "Image is required" });
                }

                // Ensure sessionId exists
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    sessionId = Guid.NewGuid().ToString();
                }

                var userId = GetUserId();

                // Validate image
                if (!image.ContentType.StartsWith("image/"))
                {
                    return BadRequest(new { success = false, error = "Only image files are allowed" });
                }

                if (image.Length > 5 * 1024 * 1024) // 5MB limit
                {
                    return BadRequest(new { success = false, error = "Image size must be less than 5MB" });
                }

                // Convert image to base64
                using var memoryStream = new MemoryStream();
                await image.CopyToAsync(memoryStream);
                var imageData = Convert.ToBase64String(memoryStream.ToArray());

                // Add image message to chat
                await _chatbotService.AddImageMessageAsync(sessionId, imageData, userId);

                // Analyze image with AI
                var analysisResult = await _chatbotService.AnalyzeImageAsync(imageData, message, sessionId, userId);

                return Ok(new
                {
                    success = true,
                    response = analysisResult,
                    sessionId = sessionId,
                    timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading and analyzing image");
                return StatusCode(500, new
                {
                    success = false,
                    error = "Failed to process image: " + ex.Message
                });
            }
        }

        [HttpPost("sendmessage")]
        public async Task<IActionResult> SendMessage([FromBody] ChatMessageRequest request)
        {
            try
            {
                // Check feature access for authenticated users with a partner channel
                if (User.Identity?.IsAuthenticated == true)
                {
                    if (!await CheckCustomAPIFeatureAsync())
                    {
                        return Ok(new
                        {
                            success = true,
                            response = "Chatbot is not available in your current plan. Please upgrade to access this feature.",
                            intent = "feature_locked",
                            confidence = 1.0,
                            shouldTransferToAgent = false
                        });
                    }
                }

                if (request == null || string.IsNullOrWhiteSpace(request.Message))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "Message is required"
                    });
                }

                // ✅ Ensure sessionId exists
                if (string.IsNullOrWhiteSpace(request.SessionId))
                {
                    request.SessionId = Guid.NewGuid().ToString();
                }

                var userId = GetUserId();
                
                // Debug logging to understand authentication state
                _logger.LogInformation("Chatbot Request - IsAuthenticated: {IsAuth}, UserId: {UserId}, User: {User}", 
                    User.Identity?.IsAuthenticated == true, userId, User.Identity?.Name);

                var botMessage = await _chatbotService.ProcessMessageAsync(
                    request.Message,
                    request.SessionId,
                    userId
                );

                return Ok(new
                {
                    success = true,
                    response = botMessage.Response,
                    intent = botMessage.Intent,
                    confidence = botMessage.Confidence,
                    shouldTransferToAgent = botMessage.ShouldTransferToAgent,
                    assignedAgentId = botMessage.AssignedAgentId,
                    assignedAgentName = botMessage.AssignedAgentName,
                    shouldCreateLead = botMessage.ShouldCreateLead,
                    generatedLeadId = botMessage.GeneratedLeadId,
                    propertyQuery = botMessage.PropertyQuery
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: " + ex.ToString());
                _logger.LogError(ex, "Error processing chat message");

                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        [HttpGet("analytics")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAnalytics()
        {
            try
            {
                var analytics = await _chatbotService.GetChatAnalyticsAsync();

                return Ok(new { success = true, analytics });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting chat analytics");

                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        [HttpGet("intents")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetIntents()
        {
            try
            {
                var intents = await _chatbotService.GetActiveIntentsAsync();

                return Ok(new { success = true, intents });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting chat intents");

                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        [HttpPost("intents")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AddIntent([FromBody] ChatIntentModel intent)
        {
            try
            {
                intent.CreatedOn = DateTime.UtcNow;
                intent.IsActive = true;

                _context.ChatIntents.Add(intent);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Intent added successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding intent");

                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        [HttpGet("sessions")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetSessions()
        {
            try
            {
                var conversations = await _context.ChatSessions
                    .Include(c => c.GeneratedLead)
                    .OrderByDescending(c => c.StartedAt)
                    .Take(50)
                    .Select(c => new
                    {
                        c.SessionId,
                        c.UserId,
                        c.UserName,
                        c.UserPhone,
                        c.UserEmail,
                        c.StartedAt,
                        c.Status,
                        c.MessageCount,
                        c.LastIntent,
                        c.IsLeadGenerated,
                        c.GeneratedLeadId,
                        c.AssignedAgentId,
                        LeadName = c.GeneratedLead != null ? c.GeneratedLead.Name : null
                    })
                    .ToListAsync();

                return Ok(new { success = true, conversations });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting chat sessions");

                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        private string GetUserRole()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                if (User.IsInRole("Admin")) return "Admin";
                if (User.IsInRole("ChannelPartner")) return "ChannelPartner";
                if (User.IsInRole("Agent")) return "Agent";
                return "User";
            }
            return "Public";
        }

        private int? GetUserId()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                // Log all available claims for debugging
                var claims = User.Claims.Select(c => $"{c.Type}: {c.Value}").ToList();
                _logger.LogInformation("Available claims: {Claims}", string.Join(", ", claims));

                // Try different claim types that might contain the user ID
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) 
                               ?? User.FindFirst("UserId")
                               ?? User.FindFirst("sub")
                               ?? User.FindFirst("user_id")
                               ?? User.FindFirst("nameid");

                if (userIdClaim != null && int.TryParse(userIdClaim.Value, out var userId))
                {
                    _logger.LogInformation("Found user ID from claim {ClaimType}: {UserId}", userIdClaim.Type, userId);
                    return userId;
                }

                // Try to get from User.Identity.Name if it's a user ID
                if (User.Identity.Name != null && int.TryParse(User.Identity.Name, out var nameUserId))
                {
                    _logger.LogInformation("Found user ID from User.Identity.Name: {UserId}", nameUserId);
                    return nameUserId;
                }

                _logger.LogWarning("User is authenticated but no valid user ID found in claims");
            }
            else
            {
                _logger.LogInformation("User is not authenticated - treating as public user");
            }
            return null;
        }
    }

    public class ChatMessageRequest
    {
        public string SessionId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class LeadCaptureRequest
    {
        public string SessionId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? Email { get; set; }
    }
}
