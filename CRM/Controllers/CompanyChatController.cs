using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using CRM.Models;
using CRM.Services;
using CRM.Hubs;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace CRM.Controllers
{
    [Authorize]
    public class CompanyChatController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<CompanyChatController> _logger;
        private readonly IWebHostEnvironment _env;
        private readonly IHubContext<RealTimeChatHub> _hubContext;

        public CompanyChatController(AppDbContext context, ILogger<CompanyChatController> logger, IWebHostEnvironment env, IHubContext<RealTimeChatHub> hubContext)
        {
            _context = context;
            _logger = logger;
            _env = env;
            _hubContext = hubContext;
        }

        private (int userId, string username, string role, int tenantId) GetUserInfo()
        {
            var userId = int.TryParse(User.FindFirst("UserId")?.Value, out int uid) ? uid : 0;
            var username = User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";
            var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
            var tenantId = int.TryParse(User.FindFirst("TenantId")?.Value, out int tid) ? tid : 0;
            return (userId, username, role, tenantId);
        }

        [HttpGet]
        public IActionResult Index()
        {
            var (userId, username, role, tenantId) = GetUserInfo();
            ViewBag.CurrentUserId = userId;
            ViewBag.CurrentUsername = username;
            ViewBag.CurrentRole = role;
            ViewBag.TenantId = tenantId;

            return View();
        }

        [HttpGet]
        public IActionResult GetCompanyMembers()
        {
            var (userId, username, role, tenantId) = GetUserInfo();

            try
            {
                var currentUser = _context.Users.FirstOrDefault(u => u.UserId == userId);
                var cpId = currentUser?.ChannelPartnerId;

                // Get users from the same company
                var query = _context.Users.AsQueryable();

                if (role != "SuperAdmin" && role != "Admin")
                {
                    // Non-admin/superadmin see only their company members
                    if (cpId.HasValue)
                        query = query.Where(u => u.ChannelPartnerId == cpId);
                    else if (tenantId > 0)
                        query = query.Where(u => u.TenantId == tenantId);
                }
                else if (role == "Admin")
                {
                    // Admin sees users with null ChannelPartnerId + their partner agents
                    query = query.Where(u => u.ChannelPartnerId == null);
                }
                // SuperAdmin sees all

                var members = query
                    .Where(u => u.IsActive && u.UserId != userId)
                    .Select(u => new
                    {
                        u.UserId,
                        u.Username,
                        u.Role,
                        u.ChannelPartnerId,
                        initial = (u.Username ?? "U").Substring(0, 1).ToUpper()
                    })
                    .ToList();

                // Get online status from AgentChatStatus
                var onlineStatuses = _context.AgentChatStatus
                    .Where(s => s.IsOnline)
                    .Select(s => s.AgentId)
                    .ToList();

                var result = members.Select(m => new
                {
                    m.UserId,
                    m.Username,
                    m.Role,
                    m.ChannelPartnerId,
                    m.initial,
                    isOnline = onlineStatuses.Contains(m.UserId)
                }).OrderByDescending(m => m.isOnline).ThenBy(m => m.Username).ToList();

                return Json(new { success = true, members = result });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult GetConversation(int recipientId)
        {
            var (userId, username, role, tenantId) = GetUserInfo();

            try
            {
                // Get the recipient to verify they are in the same tenant
                var recipient = _context.Users.FirstOrDefault(u => u.UserId == recipientId);
                if (recipient == null)
                    return Json(new { success = false, message = "Recipient not found" });

                // Security: verify users are in the same company
                var sender = _context.Users.FirstOrDefault(u => u.UserId == userId);
                if (role != "SuperAdmin" && sender != null && recipient != null)
                {
                    // SuperAdmin can message anyone, others must be in same company
                    var senderCp = sender.ChannelPartnerId;
                    var recipientCp = recipient.ChannelPartnerId;
                    if (senderCp != recipientCp)
                        return Json(new { success = false, message = "Access denied: different company" });
                }

                // Messages between current user and recipient (both directions)
                var messages = _context.CompanyMessages
                    .Where(m => (m.SenderId == userId && m.RecipientId == recipientId) ||
                                (m.SenderId == recipientId && m.RecipientId == userId))
                    .Where(m => !m.IsDeleted)
                    .OrderBy(m => m.SentAt)
                    .Select(m => new
                    {
                        id = m.Id,
                        m.SenderId,
                        m.SenderName,
                        m.RecipientId,
                        m.MessageText,
                        m.FileName,
                        m.FilePath,
                        m.FileType,
                        m.IsRead,
                        m.ReadAt,
                        SentAt = m.SentAt.ToString("MMM dd, yyyy hh:mm tt"),
                        isMine = m.SenderId == userId
                    })
                    .ToList();

                // Mark unread messages as read
                var unreadMessages = _context.CompanyMessages
                    .Where(m => m.SenderId == recipientId && m.RecipientId == userId && !m.IsRead)
                    .ToList();
                foreach (var msg in unreadMessages)
                {
                    msg.IsRead = true;
                    msg.ReadAt = DateTime.UtcNow;
                }

                return Json(new { success = true, messages });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SendMessage(int recipientId, string message, IFormFile? attachment)
        {
            var (userId, username, role, tenantId) = GetUserInfo();

            try
            {
                if (string.IsNullOrWhiteSpace(message) && attachment == null)
                    return Json(new { success = false, message = "Message or attachment required" });

                // Verify recipient exists
                var recipient = _context.Users.FirstOrDefault(u => u.UserId == recipientId);
                if (recipient == null)
                    return Json(new { success = false, message = "Recipient not found" });

                // Security: verify users are in the same company (unless SuperAdmin)
                if (role != "SuperAdmin")
                {
                    var sender = _context.Users.FirstOrDefault(u => u.UserId == userId);
                    if (sender != null && sender.ChannelPartnerId != recipient.ChannelPartnerId)
                        return Json(new { success = false, message = "Access denied: different company" });
                }

                string? filePath = null;
                string? fileName = null;
                string? fileType = null;
                long? fileSize = null;

                if (attachment != null && attachment.Length > 0)
                {
                    var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "chat");
                    Directory.CreateDirectory(uploadsDir);

                    fileName = $"{Guid.NewGuid():N}_{attachment.FileName}";
                    filePath = $"/uploads/chat/{fileName}";
                    fileType = attachment.ContentType;
                    fileSize = attachment.Length;

                    var fullPath = Path.Combine(uploadsDir, fileName);
                    using (var stream = new FileStream(fullPath, FileMode.Create))
                    {
                        await attachment.CopyToAsync(stream);
                    }
                }

                var msg = new CompanyMessageModel
                {
                    TenantId = tenantId,
                    SenderId = userId,
                    SenderName = username,
                    SenderRole = role,
                    RecipientId = recipientId,
                    MessageText = message ?? "",
                    FileName = fileName,
                    FilePath = filePath,
                    FileType = fileType,
                    FileSize = fileSize,
                    SentAt = DateTime.UtcNow
                };

                _context.CompanyMessages.Add(msg);
                await _context.SaveChangesAsync();

                // Broadcast via SignalR to recipient
                try
                {
                    await _hubContext.Clients.Group($"company_user_{recipientId}").SendAsync("ReceiveCompanyMessage", new
                    {
                        id = msg.Id,
                        senderId = userId,
                        senderName = username,
                        senderRole = role,
                        recipientId = recipientId,
                        messageText = message ?? "",
                        fileName = fileName,
                        filePath = filePath,
                        fileType = fileType,
                        isRead = false,
                        sentAt = msg.SentAt.ToString("MMM dd, yyyy hh:mm tt"),
                        isMine = false
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SignalR broadcast failed for company message");
                }

                return Json(new
                {
                    success = true,
                    message = new
                    {
                        id = msg.Id,
                        msg.SenderId,
                        msg.SenderName,
                        msg.RecipientId,
                        msg.MessageText,
                        msg.FileName,
                        msg.FilePath,
                        msg.FileType,
                        msg.IsRead,
                        SentAt = msg.SentAt.ToString("MMM dd, yyyy hh:mm tt"),
                        isMine = true,
                        recipientName = recipient.Username
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> MarkAsRead(string messageId)
        {
            try
            {
                var msg = await _context.CompanyMessages.FirstOrDefaultAsync(m => m.Id == messageId);
                if (msg != null)
                {
                    msg.IsRead = true;
                    msg.ReadAt = DateTime.UtcNow;
                }
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult GetUnreadCount()
        {
            var (userId, username, role, tenantId) = GetUserInfo();

            try
            {
                var count = _context.CompanyMessages
                    .Count(m => m.RecipientId == userId && !m.IsRead && !m.IsDeleted);
                return Json(new { success = true, count });
            }
            catch
            {
                return Json(new { success = false, count = 0 });
            }
        }

        [HttpGet]
        public IActionResult GetRecentConversations()
        {
            var (userId, username, role, tenantId) = GetUserInfo();

            try
            {
                // Get all conversations where user was sender or recipient
                var allMessages = _context.CompanyMessages
                    .Where(m => (m.SenderId == userId || m.RecipientId == userId) && !m.IsDeleted)
                    .OrderByDescending(m => m.SentAt)
                    .ToList();

                // Group by the other user
                var conversations = allMessages
                    .GroupBy(m => m.SenderId == userId ? m.RecipientId : m.SenderId)
                    .Select(g =>
                    {
                        var lastMsg = g.First();
                        var otherUserId = g.Key;
                        var otherUser = _context.Users.FirstOrDefault(u => u.UserId == otherUserId);
                        var unread = g.Count(m => m.RecipientId == userId && !m.IsRead);
                        return new
                        {
                            userId = otherUserId,
                            username = otherUser?.Username ?? "Unknown",
                            role = otherUser?.Role ?? "",
                            lastMessage = lastMsg.MessageText?.Length > 50 ? lastMsg.MessageText[..50] + "..." : (lastMsg.MessageText ?? ""),
                            lastTime = lastMsg.SentAt.ToString("MMM dd, hh:mm tt"),
                            unread,
                            initial = (otherUser?.Username ?? "U").Substring(0, 1).ToUpper()
                        };
                    })
                    .OrderByDescending(c => c.unread)
                    .ThenByDescending(c => c.lastTime)
                    .ToList();

                return Json(new { success = true, conversations });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}
