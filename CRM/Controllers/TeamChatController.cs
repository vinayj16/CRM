using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using CRM.Hubs;
using CRM.Models.Chatbot;
using System.Security.Claims;

namespace CRM.Controllers
{
    [Authorize]
    public class TeamChatController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IHubContext<RealTimeChatHub> _hubContext;
        private readonly ILogger<TeamChatController> _logger;

        public TeamChatController(AppDbContext context, IHubContext<RealTimeChatHub> hubContext, ILogger<TeamChatController> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetMembers()
        {
            try
            {
                var currentUserId = User.FindFirst("UserId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(currentUserId ?? "0", out int userId);
                
                var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
                if (currentUser == null)
                {
                    return Json(new { success = false, error = "User not found" });
                }

                var members = await _context.Users
                    .Where(u => u.IsActive && u.TenantId == currentUser.TenantId && u.UserId != userId)
                    .Select(u => new { 
                        name = u.Username, 
                        role = u.Role,
                        isOnline = u.IsActive && (u.LastActivity == null || u.LastActivity.Value > DateTime.UtcNow.AddMinutes(-5))
                    })
                    .ToListAsync();

                return Json(new { success = true, members = members });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading team members");
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetMessages(int take = 50, int skip = 0)
        {
            try
            {
                var currentUserId = User.FindFirst("UserId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(currentUserId ?? "0", out int userId);
                
                var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
                if (currentUser == null)
                {
                    return Json(new { success = false, error = "User not found" });
                }

                var messages = await _context.RealTimeChatMessages
                    .Where(m => m.ConversationId == "team-chat" && m.TenantId == currentUser.TenantId)
                    .OrderByDescending(m => m.SentAt)
                    .Take(take)
                    .Skip(skip)
                    .ToListAsync();

                var result = messages.Select(m => new
                {
                    id = m.Id,
                    message = m.MessageText,
                    messageType = m.MessageType,
                    senderId = m.SenderId,
                    senderName = m.SenderName,
                    timestamp = m.SentAt,
                    imageData = m.ImageData,
                    fileName = m.Metadata
                }).Reverse().ToList();

                return Json(new { success = true, messages = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading team chat messages");
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SendMessage([FromBody] TeamChatMessageRequest request)
        {
            try
            {
                var userId = User.FindFirst("UserId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var userName = User.FindFirst(ClaimTypes.Name)?.Value ?? User.FindFirst("name")?.Value ?? "Unknown";
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? "User";

                if (string.IsNullOrWhiteSpace(request.Message) && string.IsNullOrWhiteSpace(request.ImageData))
                {
                    return Json(new { success = false, error = "Message or image is required" });
                }

                int.TryParse(userId ?? "0", out int senderId);
                var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.UserId == senderId);

                var chatMessage = new RealTimeChatMessage
                {
                    SessionId = "team-chat",
                    ConversationId = "team-chat",
                    MessageType = "Team",
                    SenderId = senderId,
                    SenderName = userName,
                    MessageText = request.Message ?? string.Empty,
                    ImageData = request.ImageData,
                    Metadata = request.FileName,
                    SentAt = DateTime.UtcNow,
                    Priority = "Normal",
                    Status = "Active",
                    TenantId = currentUser?.TenantId ?? 0
                };

                _context.RealTimeChatMessages.Add(chatMessage);
                await _context.SaveChangesAsync();

                await _hubContext.Clients.All.SendAsync("ReceiveTeamMessage", new
                {
                    id = chatMessage.Id,
                    message = chatMessage.MessageText,
                    messageType = "Team",
                    senderId = chatMessage.SenderId,
                    senderName = chatMessage.SenderName,
                    senderRole = userRole,
                    timestamp = chatMessage.SentAt,
                    imageData = chatMessage.ImageData,
                    fileName = chatMessage.Metadata
                });

                return Json(new { success = true, message = "Message sent", timestamp = chatMessage.SentAt });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending team chat message");
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UploadFile(IFormFile file, string message = "")
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return Json(new { success = false, error = "File is required" });
                }

                if (file.Length > 10 * 1024 * 1024)
                {
                    return Json(new { success = false, error = "File size must be less than 10MB" });
                }

                using var memoryStream = new MemoryStream();
                await file.CopyToAsync(memoryStream);
                var fileBytes = memoryStream.ToArray();
                var base64 = Convert.ToBase64String(fileBytes);
                var dataUrl = $"data:{file.ContentType};base64,{base64}";

                var userId = User.FindFirst("UserId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var userName = User.FindFirst(ClaimTypes.Name)?.Value ?? User.FindFirst("name")?.Value ?? "Unknown";
                int.TryParse(userId ?? "0", out int senderId);
                var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.UserId == senderId);

                var chatMessage = new RealTimeChatMessage
                {
                    SessionId = "team-chat",
                    ConversationId = "team-chat",
                    MessageType = "Team",
                    SenderId = senderId,
                    SenderName = userName,
                    MessageText = message ?? string.Empty,
                    ImageData = dataUrl,
                    Metadata = file.FileName,
                    SentAt = DateTime.UtcNow,
                    Priority = "Normal",
                    Status = "Active",
                    TenantId = currentUser?.TenantId ?? 0
                };

                _context.RealTimeChatMessages.Add(chatMessage);
                await _context.SaveChangesAsync();

                await _hubContext.Clients.All.SendAsync("ReceiveTeamMessage", new
                {
                    id = chatMessage.Id,
                    message = chatMessage.MessageText,
                    messageType = "Team",
                    senderId = chatMessage.SenderId,
                    senderName = chatMessage.SenderName,
                    senderRole = User.FindFirst(ClaimTypes.Role)?.Value ?? "User",
                    timestamp = chatMessage.SentAt,
                    imageData = chatMessage.ImageData,
                    fileName = chatMessage.Metadata
                });

                return Json(new { success = true, message = "File sent", timestamp = chatMessage.SentAt });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading team chat file");
                return Json(new { success = false, error = ex.Message });
            }
        }
    }

    public class TeamChatMessageRequest
    {
        public string? Message { get; set; }
        public string? ImageData { get; set; }
        public string? FileName { get; set; }
    }
}
