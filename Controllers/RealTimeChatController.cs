using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using CRM.Services;
using CRM.Models.Chatbot;
using System.Security.Claims;

namespace CRM.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class RealTimeChatController : Controller
    {
        private readonly IChatbotService _chatbotService;
        private readonly AppDbContext _context;
        private readonly ILogger<RealTimeChatController> _logger;

        public RealTimeChatController(
            IChatbotService chatbotService,
            AppDbContext context,
            ILogger<RealTimeChatController> logger)
        {
            _chatbotService = chatbotService;
            _context = context;
            _logger = logger;
        }

        private int GetCurrentUserId()
        {
            var userId = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userId, out int id) ? id : 0;
        }

        private string GetCurrentUserRole()
        {
            return User?.FindFirst(ClaimTypes.Role)?.Value ?? "Public";
        }

        private bool IsAgent()
        {
            var role = GetCurrentUserRole();
            return role.Equals("Agent", StringComparison.OrdinalIgnoreCase) ||
                   role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                   role.Equals("Manager", StringComparison.OrdinalIgnoreCase);
        }

        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboardData()
        {
            if (!IsAgent())
            {
                return Forbid();
            }

            try
            {
                var agentId = GetCurrentUserId();
                
                // Get agent's active conversations
                var myAssignments = await _context.ChatConversationAssignments
                    .Where(a => a.AssignedAgentId == agentId && a.Status != "Resolved")
                    
                    .ToListAsync();

                // Get all unassigned conversations
                var unassignedConversations = await _context.ChatConversationAssignments
                    .Where(a => a.Status == "Unassigned")
                    .ToListAsync();

                // Get online agents
                var onlineAgents = await _chatbotService.GetOnlineAgentsAsync();

                // Get recent messages
                var recentMessages = await _context.RealTimeChatMessages
                    
                    .Where(m => m.SentAt >= DateTime.UtcNow.AddHours(-24))
                    .OrderByDescending(m => m.SentAt)
                    .Take(50)
                    .ToListAsync();

                // Get unread notifications
                var unreadNotifications = await _context.ChatNotifications
                    .Where(n => n.AgentId == agentId && !n.IsRead && (n.ExpiresAt == null || n.ExpiresAt > DateTime.UtcNow))
                    .OrderByDescending(n => n.CreatedAt)
                    .Take(20)
                    .ToListAsync();

                var dashboardData = new
                {
                    myActiveConversations = myAssignments.Select(a => new
                    {
                        conversationId = a.ConversationId,
                        assignedAt = a.AssignedAt,
                        status = a.Status,
                        priority = a.Priority,
                        lastAgentActivityAt = a.LastAgentActivityAt
                    }),
                    unassignedConversations = unassignedConversations.Select(a => new
                    {
                        conversationId = a.ConversationId,
                        assignedAt = a.AssignedAt,
                        priority = a.Priority
                    }),
                    onlineAgents = onlineAgents.Select(a => new
                    {
                        agentId = a.AgentId,
                        agentName = a.Agent?.Username ?? a.Agent?.Email,
                        currentStatus = a.CurrentStatus,
                        currentChatCount = a.CurrentChatCount,
                        maxConcurrentChats = a.MaxConcurrentChats,
                        averageResponseTime = a.AverageResponseTime
                    }),
                    recentMessages = recentMessages.Select(m => new
                    {
                        id = m.Id,
                        conversationId = m.ConversationId,
                        messageText = m.MessageText,
                        messageType = m.MessageType,
                        senderName = m.SenderName,
                        sentAt = m.SentAt,
                        isRead = m.IsRead
                    }),
                    unreadNotifications = unreadNotifications.Select(n => new
                    {
                        id = n.Id,
                        type = n.NotificationType,
                        title = n.Title,
                        message = n.Message,
                        relatedConversationId = n.RelatedConversationId,
                        createdAt = n.CreatedAt,
                        actionRequired = n.ActionRequired,
                        actionUrl = n.ActionUrl
                    }),
                    stats = new
                    {
                        totalActiveConversations = myAssignments.Count,
                        unassignedConversationsCount = unassignedConversations.Count,
                        onlineAgentsCount = onlineAgents.Count,
                        unreadNotificationsCount = unreadNotifications.Count
                    }
                };

                return Json(new { success = true, data = dashboardData });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dashboard data for agent {AgentId}", GetCurrentUserId());
                return Json(new { success = false, error = "Failed to get dashboard data" });
            }
        }

        [HttpGet("conversations/{conversationId}/messages")]
        public async Task<IActionResult> GetConversationMessages(string conversationId)
        {
            if (!IsAgent())
            {
                return Forbid();
            }

            try
            {
                var messages = await _chatbotService.GetConversationMessagesAsync(conversationId);
                
                var messageList = messages.Select(m => new
                {
                    id = m.Id,
                    sessionId = m.SessionId,
                    messageType = m.MessageType,
                    senderId = m.SenderId,
                    senderName = m.SenderName,
                    messageText = m.MessageText,
                    imageData = m.ImageData,
                    sentAt = m.SentAt,
                    isRead = m.IsRead,
                    readAt = m.ReadAt,
                    assignedAgentId = m.AssignedAgentId,
                    priority = m.Priority,
                    status = m.Status,
                    intent = m.Intent,
                    confidence = m.Confidence,
                    leadId = m.LeadId,
                    isLeadGenerated = m.IsLeadGenerated,
                    parentMessageId = m.ParentMessageId
                }).OrderBy(m => m.sentAt).ToList();

                return Json(new { success = true, messages = messageList });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting messages for conversation {ConversationId}", conversationId);
                return Json(new { success = false, error = "Failed to get messages" });
            }
        }

        [HttpPost("conversations/{conversationId}/assign")]
        public async Task<IActionResult> AssignConversation(string conversationId, [FromBody] AssignConversationRequest request)
        {
            if (!IsAgent())
            {
                return Forbid();
            }

            try
            {
                var agentId = GetCurrentUserId();
                var success = await _chatbotService.AssignAgentToConversationAsync(conversationId, request.AgentId ?? agentId, agentId);
                
                if (success)
                {
                    return Json(new { success = true, message = "Conversation assigned successfully" });
                }
                else
                {
                    return Json(new { success = false, error = "Failed to assign conversation" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning conversation {ConversationId}", conversationId);
                return Json(new { success = false, error = "Failed to assign conversation" });
            }
        }

        [HttpPost("conversations/{conversationId}/auto-assign")]
        public async Task<IActionResult> AutoAssignConversation(string conversationId, [FromBody] AssignConversationRequest request)
        {
            if (!IsAgent())
            {
                return Forbid();
            }

            try
            {
                var assignedAgentId = await _chatbotService.AutoAssignAgentAsync(conversationId, request.Priority);
                
                if (assignedAgentId.HasValue)
                {
                    return Json(new { success = true, assignedAgentId = assignedAgentId.Value, message = "Conversation auto-assigned successfully" });
                }
                else
                {
                    return Json(new { success = false, error = "No available agents found" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error auto-assigning conversation {ConversationId}", conversationId);
                return Json(new { success = false, error = "Failed to auto-assign conversation" });
            }
        }

        [HttpPost("messages/{messageId}/read")]
        public async Task<IActionResult> MarkMessageAsRead(int messageId)
        {
            if (!IsAgent())
            {
                return Forbid();
            }

            try
            {
                var agentId = GetCurrentUserId();
                
                var message = await _context.RealTimeChatMessages
                    .FirstOrDefaultAsync(m => m.Id == messageId);

                if (message != null)
                {
                    message.IsRead = true;
                    message.ReadAt = DateTime.UtcNow;
                    _context.RealTimeChatMessages.Update(message);
                    await _context.SaveChangesAsync();
                }

                return Json(new { success = true, message = "Message marked as read" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking message {MessageId} as read", messageId);
                return Json(new { success = false, error = "Failed to mark message as read" });
            }
        }

        [HttpPost("notifications/{notificationId}/read")]
        public async Task<IActionResult> MarkNotificationAsRead(int notificationId)
        {
            if (!IsAgent())
            {
                return Forbid();
            }

            try
            {
                var agentId = GetCurrentUserId();
                
                var notification = await _context.ChatNotifications
                    .FirstOrDefaultAsync(n => n.Id == notificationId && n.AgentId == agentId);

                if (notification != null)
                {
                    notification.IsRead = true;
                    notification.ReadAt = DateTime.UtcNow;
                    _context.ChatNotifications.Update(notification);
                    await _context.SaveChangesAsync();
                }

                return Json(new { success = true, message = "Notification marked as read" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking notification {NotificationId} as read", notificationId);
                return Json(new { success = false, error = "Failed to mark notification as read" });
            }
        }

        [HttpPost("status/update")]
        public async Task<IActionResult> UpdateAgentStatus([FromBody] UpdateAgentStatusRequest request)
        {
            if (!IsAgent())
            {
                return Forbid();
            }

            try
            {
                var agentId = GetCurrentUserId();
                var success = await _chatbotService.UpdateAgentChatStatusAsync(agentId, true, request.Status);
                
                if (success)
                {
                    return Json(new { success = true, message = "Status updated successfully" });
                }
                else
                {
                    return Json(new { success = false, error = "Failed to update status" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating agent status");
                return Json(new { success = false, error = "Failed to update status" });
            }
        }

        [HttpGet("analytics")]
        public async Task<IActionResult> GetAnalytics()
        {
            if (!IsAgent())
            {
                return Forbid();
            }

            try
            {
                var agentId = GetCurrentUserId();
                var today = DateTime.UtcNow.Date;
                
                // Get today's metrics
                var todayMetrics = await _context.ChatMessageMetrics
                    .Where(m => m.AgentId == agentId && m.MetricDate == today)
                    .FirstOrDefaultAsync();

                // Get conversation assignments
                var assignments = await _context.ChatConversationAssignments
                    .Where(a => a.AssignedAgentId == agentId)
                    .ToListAsync();

                // Get messages sent/received
                var messages = await _context.RealTimeChatMessages
                    .Where(m => m.AssignedAgentId == agentId)
                    .ToListAsync();

                var analytics = new
                {
                    todayStats = todayMetrics != null ? new
                    {
                        totalMessages = todayMetrics.TotalMessages,
                        userMessages = todayMetrics.UserMessages,
                        agentMessages = todayMetrics.AgentMessages,
                        averageResponseTime = todayMetrics.AverageResponseTime,
                        firstResponseTime = todayMetrics.FirstResponseTime,
                        conversationDuration = todayMetrics.ConversationDuration,
                        resolutionTime = todayMetrics.ResolutionTime,
                        customerSatisfaction = todayMetrics.CustomerSatisfaction,
                        leadGenerated = todayMetrics.LeadGenerated,
                        leadValue = todayMetrics.LeadValue
                    } : null,
                    overallStats = new
                    {
                        totalAssignments = assignments.Count,
                        resolvedAssignments = assignments.Count(a => a.Status == "Resolved"),
                        activeAssignments = assignments.Count(a => a.Status == "Assigned" || a.Status == "InProgress"),
                        totalMessages = messages.Count,
                        userMessages = messages.Count(m => m.MessageType == "User"),
                        agentMessages = messages.Count(m => m.MessageType == "Agent"),
                        averageResponseTime = messages.Where(m => m.MessageType == "Agent").Average(m => m.SentAt.Hour * 60 + m.SentAt.Minute) // simplified calculation
                    }
                };

                return Json(new { success = true, analytics = analytics });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting analytics for agent {AgentId}", GetCurrentUserId());
                return Json(new { success = false, error = "Failed to get analytics" });
            }
        }
    }

    // Request/Response Models
    public class AssignConversationRequest
    {
        public int? AgentId { get; set; }
        public string Priority { get; set; } = "Normal";
    }

    public class UpdateAgentStatusRequest
    {
        public string Status { get; set; } = string.Empty;
    }

    public class UpdateDeviceTokenRequest
    {
        public string DeviceToken { get; set; } = string.Empty;
    }
}
