using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using CRM.Models;
using CRM.ViewModels;
using CRM.Services;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using CRM.Models.Chatbot;
using CRM.Hubs;

namespace CRM.Controllers
{
    [Authorize]
    public class ChatbotDashboardController : Controller
    {
        private readonly IChatbotService _chatbotService;
        private readonly AppDbContext _context;
        private readonly ILogger<ChatbotDashboardController> _logger;
        private readonly INotificationService _notificationService;
        private readonly IHubContext<RealTimeChatHub> _hubContext;

        public ChatbotDashboardController(
            IChatbotService chatbotService,
            AppDbContext context,
            ILogger<ChatbotDashboardController> logger,
            INotificationService notificationService,
            IHubContext<RealTimeChatHub> hubContext)
        {
            _chatbotService = chatbotService;
            _context = context;
            _logger = logger;
            _notificationService = notificationService;
            _hubContext = hubContext;
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
                   role.Equals("Manager", StringComparison.OrdinalIgnoreCase) ||
                   role.Equals("Channel Partner", StringComparison.OrdinalIgnoreCase);
        }

        private bool CanAssignLeads()
        {
            var role = GetCurrentUserRole();
            return role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                   role.Equals("Manager", StringComparison.OrdinalIgnoreCase) ||
                   role.Equals("Channel Partner", StringComparison.OrdinalIgnoreCase);
        }

        private bool CanViewAllConversations()
        {
            var role = GetCurrentUserRole();
            return role.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                   role.Equals("Manager", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<IActionResult> Index()
        {
            if (!IsAgent())
            {
                return RedirectToAction("Index", "Home");
            }

            try
            {
                var userId = GetCurrentUserId();
                var userRole = GetCurrentUserRole();
                ViewBag.CurrentUserId = userId;
                ViewBag.CurrentUserRole = userRole;
                ViewBag.CanAssignLeads = CanAssignLeads();
                ViewBag.CanViewAllConversations = CanViewAllConversations();
                var today = DateTime.UtcNow.Date;
                
                // Get actual chatbot sessions from landing page
                List<ChatSessionModel> mySessions;
                List<ChatSessionModel> unassignedSessions;

                // Log total sessions for debugging
                var totalSessions = await _context.ChatSessions.CountAsync();
                _logger.LogInformation("Total ChatSessions in database: {TotalSessions}", totalSessions);

                if (CanViewAllConversations())
                {
                    // Admin/Manager can see all sessions - remove status filter to see all
                    mySessions = await _context.ChatSessions
                        .OrderByDescending(s => s.StartedAt)
                        .ToListAsync();

                    unassignedSessions = await _context.ChatSessions
                        .Where(s => !s.AssignedAgentId.HasValue || s.AssignedAgentId == 0)
                        .OrderByDescending(s => s.StartedAt)
                        .ToListAsync();
                }
                else if (userRole.Equals("Channel Partner", StringComparison.OrdinalIgnoreCase))
                {
                    // Channel Partner can see their assigned sessions and unassigned ones - remove status filter for debugging
                    mySessions = await _context.ChatSessions
                        .Where(s => s.AssignedAgentId == userId)
                        .OrderByDescending(s => s.StartedAt)
                        .ToListAsync();

                    unassignedSessions = await _context.ChatSessions
                        .Where(s => !s.AssignedAgentId.HasValue || s.AssignedAgentId == 0)
                        .OrderByDescending(s => s.StartedAt)
                        .ToListAsync();
                }
                else
                {
                    // Agent can see their assigned sessions AND unassigned sessions they can pick up
                    mySessions = await _context.ChatSessions
                        .Where(s => s.AssignedAgentId == userId)
                        .OrderByDescending(s => s.StartedAt)
                        .ToListAsync();

                    // Also show unassigned sessions for agents to pick up
                    unassignedSessions = await _context.ChatSessions
                        .Where(s => !s.AssignedAgentId.HasValue || s.AssignedAgentId == 0)
                        .OrderByDescending(s => s.StartedAt)
                        .ToListAsync();
                }

                var onlineAgents = new List<AgentChatStatus>(); // Temporarily disabled due to missing AgentChatStatus table

                // Get recent actual chat messages (exclude ImageData column)
                var recentMessages = await _context.ChatbotMessages
                    .OrderByDescending(m => m.SentAt)
                    .Take(50)
                    .Select(m => new ChatbotMessage 
                    {
                        Id = m.Id,
                        ConversationId = m.ConversationId,
                        SenderType = m.SenderType,
                        MessageText = m.MessageText,
                        SentAt = m.SentAt,
                        MessageType = m.MessageType,
                        IsRead = m.IsRead,
                        Intent = m.Intent,
                        EntityData = m.EntityData,
                    })
                    .ToListAsync();

                // Get actual notifications from the notification system
                var unreadNotifications = await _notificationService.GetUserNotificationsAsync(userId, userRole);
                var unreadNotificationsList = unreadNotifications.Where(n => !n.IsRead).Take(20).ToList();

                // Get today's metrics - use session count as a simple metric
                var todayMetrics = new ChatMessageMetrics
                {
                    AgentId = userId,
                    MetricDate = today,
                    TotalMessages = mySessions.Count,
                    UserMessages = mySessions.Count,
                    AgentMessages = 0,
                    LeadGenerated = mySessions.Any(s => s.IsLeadGenerated)
                };

                // Get member login count (total users who logged in today)
                var memberLoginCount = await _context.Users
                    .Where(u => u.LastActivity.HasValue && u.LastActivity.Value.Date == today)
                    .CountAsync();

                var viewModel = new ChatbotDashboardViewModel
                {
                    MyActiveConversations = mySessions.Select(s => new ConversationViewModel
                    {
                        ConversationId = s.SessionId.ToString(),
                        AssignedAt = s.StartedAt,
                        Status = s.Status,
                        Priority = s.IsLeadGenerated ? "High" : "Normal",
                        LastAgentActivityAt = s.StartedAt
                    }).ToList(),

                    UnassignedConversations = unassignedSessions.Select(s => new ConversationViewModel
                    {
                        ConversationId = s.SessionId.ToString(),
                        AssignedAt = s.StartedAt,
                        Priority = s.IsLeadGenerated ? "High" : "Normal"
                    }).ToList(),
                    
                    OnlineAgents = onlineAgents.Select(a => new AgentStatusViewModel
                    {
                        AgentId = a.AgentId,
                        AgentName = a.Agent?.Username ?? a.Agent?.Email,
                        CurrentStatus = a.CurrentStatus,
                        CurrentChatCount = a.CurrentChatCount,
                        MaxConcurrentChats = a.MaxConcurrentChats,
                        AverageResponseTime = a.AverageResponseTime
                    }).ToList(),
                    
                    RecentMessages = recentMessages.Select(m => new MessageViewModel
                    {
                        Id = m.Id,
                        ConversationId = m.ConversationId.ToString(),
                        MessageText = m.MessageText,
                        MessageType = m.SenderType,
                        SenderName = m.SenderType == "User" ? "User" : "Bot",
                        SentAt = m.SentAt,
                        IsRead = m.IsRead,
                        Intent = m.Intent
                    }).ToList(),
                    
                    UnreadNotifications = unreadNotificationsList.Select(n => new NotificationViewModel
                    {
                        Id = n.NotificationId,
                        Type = n.Type,
                        Title = n.Title,
                        Message = n.Message,
                        RelatedConversationId = n.RelatedEntityId?.ToString(),
                        CreatedAt = n.CreatedOn,
                        ActionRequired = true,
                        ActionUrl = n.Link
                    }).ToList(),
                    
                    Stats = new DashboardStatsViewModel
                    {
                        TotalActiveConversations = mySessions.Count,
                        UnassignedConversationsCount = unassignedSessions.Count,
                        OnlineAgentsCount = onlineAgents.Count,
                        UnreadNotificationsCount = unreadNotifications.Count,
                        TodayTotalMessages = todayMetrics?.TotalMessages ?? 0,
                        TodayUserMessages = todayMetrics?.UserMessages ?? 0,
                        TodayAgentMessages = todayMetrics?.AgentMessages ?? 0,
                        TodayAverageResponseTime = todayMetrics?.AverageResponseTime,
                        TodayLeadsGenerated = todayMetrics?.LeadGenerated ?? false,
                        MemberLoginCount = memberLoginCount
                    }
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading chatbot dashboard for agent {AgentId}", GetCurrentUserId());
                return View(new ChatbotDashboardViewModel());
            }
        }

        public async Task<IActionResult> Conversation(string conversationId)
        {
            var userRole = GetCurrentUserRole();
            var userId = GetCurrentUserId();
            
            _logger.LogInformation("Conversation requested - Role: {Role}, UserId: {UserId}, ConversationId: {ConversationId}", 
                userRole, userId, conversationId);

            if (!IsAgent())
            {
                _logger.LogWarning("Access denied - User role {Role} is not authorized", userRole);
                return RedirectToAction("Index", "Home");
            }

            try
            {
                ViewBag.CurrentUserId = userId;
                
                _logger.LogInformation("Looking for conversation with ID: '{ConversationId}'", conversationId);

                ChatSessionModel session;
                
                // Try parsing as SessionId (int) first, then try as SessionGuid (string)
                int sessionId;
                if (int.TryParse(conversationId, out sessionId))
                {
                    _logger.LogInformation("Parsed as SessionId (int): {SessionId}", sessionId);
                    session = await _context.ChatSessions
                        .FirstOrDefaultAsync(s => s.SessionId == sessionId);
                }
                else
                {
                    _logger.LogInformation("Parsed as SessionGuid (string): '{SessionGuid}'", conversationId);
                    session = await _context.ChatSessions
                        .FirstOrDefaultAsync(s => s.SessionGuid == conversationId);
                }

                if (session == null)
                {
                    _logger.LogWarning("Session not found for conversation ID: {ConversationId}", conversationId);
                    TempData["Error"] = "Conversation not found";
                    return RedirectToAction("Index");
                }

                _logger.LogInformation("Found session: {SessionId}, SessionGuid: {SessionGuid}, Status: {Status}", session.SessionId, session.SessionGuid, session.Status);
                
                // Load ALL messages - both from ChatLogs (user-chatbot) and ChatbotMessages (agent)
                var allMessages = new List<MessageViewModel>();
                
                // 1. Load user-chatbot conversation from ChatLogs
                var chatLogs = await _context.ChatLogs
                    .Where(l => l.SessionId == session.SessionGuid)
                    .OrderBy(l => l.CreatedOn)
                    .ToListAsync();
                
                _logger.LogInformation("Found {ChatLogCount} chat log entries for session {SessionId}", chatLogs.Count, session.SessionId);
                
                foreach (var log in chatLogs)
                {
                    // Add user message
                    if (!string.IsNullOrEmpty(log.UserMessage))
                    {
                        allMessages.Add(new MessageViewModel
                        {
                            Id = log.ChatLogId,
                            ConversationId = conversationId,
                            MessageText = log.UserMessage,
                            MessageType = "User",
                            SenderName = session.UserName ?? "User",
                            SentAt = log.CreatedOn,
                            IsRead = true,
                            Intent = log.Intent
                        });
                    }
                    
                    // Add bot response
                    if (!string.IsNullOrEmpty(log.AiResponse))
                    {
                        allMessages.Add(new MessageViewModel
                        {
                            Id = log.ChatLogId,
                            ConversationId = conversationId,
                            MessageText = log.AiResponse,
                            MessageType = "Bot",
                            SenderName = "Chatbot",
                            SentAt = log.CreatedOn.AddSeconds(1), // Bot replies 1 second after user
                            IsRead = true,
                            Intent = log.Intent
                        });
                    }
                }
                
                // 2. Load agent messages from ChatbotMessages (exclude ImageData column)
                var conversation = await _context.ChatbotConversations
                    .FirstOrDefaultAsync(c => c.SessionId == session.SessionGuid);
                
                if (conversation != null)
                {
                    var agentMessages = await _context.ChatbotMessages
                        .Where(m => m.ConversationId == conversation.Id)
                        .OrderBy(m => m.SentAt)
                        .Select(m => new ChatbotMessage 
                        {
                            Id = m.Id,
                            ConversationId = m.ConversationId,
                            SenderType = m.SenderType,
                            MessageText = m.MessageText,
                            SentAt = m.SentAt,
                            MessageType = m.MessageType,
                            IsRead = m.IsRead,
                            Intent = m.Intent,
                            EntityData = m.EntityData,
                            // ImageData excluded - column doesn't exist in DB
                        })
                        .ToListAsync();
                    
                    _logger.LogInformation("Found {AgentMessageCount} agent messages for conversation {ConvId}", agentMessages.Count, conversation.Id);
                    
                    foreach (var msg in agentMessages)
                    {
                        allMessages.Add(new MessageViewModel
                        {
                            Id = msg.Id,
                            ConversationId = conversationId,
                            MessageText = msg.MessageText,
                            MessageType = msg.SenderType, // "Agent"
                            SenderName = "Agent",
                            SentAt = msg.SentAt,
                            IsRead = msg.IsRead
                        });
                    }
                }
                
                // Create a simple assignment object
                var assignment = new AssignmentViewModel
                {
                    ConversationId = conversationId,
                    AssignedAgentId = session.AssignedAgentId,
                    AssignedAt = session.StartedAt,
                    Status = session.Status,
                    Priority = session.IsLeadGenerated ? "High" : "Normal",
                    Notes = session.LastIntent
                };

                // Sort all messages by time
                allMessages = allMessages.OrderBy(m => m.SentAt).ToList();
                
                _logger.LogInformation("Total messages to display: {TotalMessages}", allMessages.Count);
                
                var viewModel = new ConversationViewModel
                {
                    ConversationId = conversationId,
                    Messages = allMessages,
                    Assignment = assignment
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading conversation {ConversationId}", conversationId);
                TempData["Error"] = $"Error loading conversation: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        public async Task<IActionResult> AssignSessionToAgent(string sessionGuid, int agentId)
        {
            if (!CanAssignLeads())
            {
                return Json(new { success = false, error = "You don't have permission to assign leads" });
            }

            try
            {
                var session = await _context.ChatSessions
                    .FirstOrDefaultAsync(s => s.SessionGuid == sessionGuid);

                if (session == null)
                {
                    return Json(new { success = false, error = "Session not found" });
                }

                session.AssignedAgentId = agentId;
                session.Status = "Assigned";
                await _context.SaveChangesAsync();

                // If session has a lead, update the lead assignment too
                if (session.GeneratedLeadId.HasValue)
                {
                    var lead = await _context.Leads.FindAsync(session.GeneratedLeadId.Value);
                    if (lead != null)
                    {
                        lead.ExecutiveId = agentId;
                        lead.ModifiedOn = DateTime.UtcNow;
                        _context.Leads.Update(lead);
                        await _context.SaveChangesAsync();

                        // Send notification to the assigned agent
                        var agentName = await _context.Users
                            .Where(u => u.UserId == agentId)
                            .Select(u => u.Username ?? u.Email)
                            .FirstOrDefaultAsync();

                        await _notificationService.NotifyLeadAssignedAsync(
                            leadId: lead.LeadId,
                            leadName: lead.Name ?? "New Lead",
                            assignedToUserId: agentId,
                            assignedByUserName: GetCurrentUserRole()
                        );
                    }
                }

                return Json(new { success = true, message = "Session assigned successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning session {SessionGuid} to agent {AgentId}", sessionGuid, agentId);
                return Json(new { success = false, error = "Error assigning session" });
            }
        }

        public class SendMessageRequest
    {
        public string SessionGuid { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    [HttpPost]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
    {
        var sessionGuid = request?.SessionGuid ?? "";
        var message = request?.Message ?? "";
        
        _logger.LogInformation("SendMessage called - sessionGuid: '{SessionGuid}', message: '{Message}'", sessionGuid, message);
            
            if (!IsAgent())
            {
                _logger.LogWarning("SendMessage - User not authorized");
                return Json(new { success = false, error = "You don't have permission to send messages" });
            }

            try
            {
                var userId = GetCurrentUserId();
                
                _logger.LogInformation("SendMessage - UserId: {UserId}, parsing sessionGuid: '{SessionGuid}'", userId, sessionGuid);
                
                // Parse sessionGuid as SessionId (int)
                int sessionId;
                if (!int.TryParse(sessionGuid, out sessionId))
                {
                    _logger.LogWarning("SendMessage - Invalid session ID format: '{SessionGuid}'", sessionGuid);
                    return Json(new { success = false, error = $"Invalid session ID format: '{sessionGuid}'" });
                }
                
                _logger.LogInformation("SendMessage - Successfully parsed SessionId: {SessionId}", sessionId);

                _logger.LogInformation("SendMessage - Looking up session: {SessionId}", sessionId);

                var session = await _context.ChatSessions
                    .FirstOrDefaultAsync(s => s.SessionId == sessionId);

                if (session == null)
                {
                    _logger.LogWarning("SendMessage - Session not found: {SessionId}", sessionId);
                    return Json(new { success = false, error = "Session not found" });
                }

                _logger.LogInformation("SendMessage - Found session: {SessionId}, AssignedAgentId: {AssignedAgentId}, UserId: {UserId}", 
                    session.SessionId, session.AssignedAgentId, userId);

                // Check if user is assigned to this session (or admin/channel partner)
                if (!CanViewAllConversations() && session.AssignedAgentId != userId)
                {
                    _logger.LogWarning("SendMessage - User {UserId} not assigned to session {SessionId}", userId, sessionId);
                    return Json(new { success = false, error = "You are not assigned to this conversation" });
                }

                _logger.LogInformation("SendMessage - Finding or creating ChatbotConversation for session {SessionId}", sessionId);

                // Find or create the ChatbotConversation record
                var conversation = await _context.ChatbotConversations
                    .FirstOrDefaultAsync(c => c.SessionId == session.SessionGuid);
                
                if (conversation == null)
                {
                    _logger.LogInformation("SendMessage - Creating new ChatbotConversation for session {SessionId}", sessionId);
                    conversation = new ChatbotConversation
                    {
                        SessionId = session.SessionGuid,
                        UserId = session.UserId,
                        UserRole = "Public",
                        StartedAt = session.StartedAt,
                        LastActivityAt = DateTime.UtcNow,
                        IsActive = true,
                        VisitorName = session.UserName,
                        VisitorEmail = session.UserEmail
                    };
                    _context.ChatbotConversations.Add(conversation);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("SendMessage - Created ChatbotConversation with Id: {ConvId}", conversation.Id);
                }
                else
                {
                    _logger.LogInformation("SendMessage - Found existing ChatbotConversation with Id: {ConvId}", conversation.Id);
                }

                // Create the message using the conversation Id
                var chatMessage = new Models.ChatbotMessage
                {
                    ConversationId = conversation.Id,
                    MessageText = message,
                    SenderType = "Agent",
                    SentAt = DateTime.UtcNow,
                    IsRead = true
                };

                _logger.LogInformation("SendMessage - About to save message. ConversationId: {ConvId}, MessageText: {MsgText}, SenderType: {Sender}", 
                    chatMessage.ConversationId, chatMessage.MessageText, chatMessage.SenderType);

                _context.ChatbotMessages.Add(chatMessage);
                await _context.SaveChangesAsync();

                _logger.LogInformation("SendMessage - Message saved with ID: {MessageId}", chatMessage.Id);

                // Update session message count
                session.MessageCount = await _context.ChatbotMessages
                    .CountAsync(m => m.ConversationId == session.SessionId);
                await _context.SaveChangesAsync();

                _logger.LogInformation("SendMessage - Successfully sent message to session {SessionId}", sessionId);

                // Broadcast message to user in real-time via SignalR
                try
                {
                    await _hubContext.Clients.Group($"session_{session.SessionGuid}")
                        .SendAsync("ReceiveAgentMessage", new
                        {
                            id = chatMessage.Id,
                            message = message,
                            senderType = "Agent",
                            senderName = "Agent",
                            timestamp = chatMessage.SentAt
                        });
                    _logger.LogInformation("SendMessage - Broadcasted message via SignalR to session_{SessionGuid}", session.SessionGuid);
                }
                catch (Exception signalREx)
                {
                    _logger.LogWarning(signalREx, "SendMessage - Failed to broadcast via SignalR, but message was saved");
                }

                return Json(new { success = true, message = "Message sent successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message to session {SessionGuid}. Exception: {ExceptionMessage}, Inner: {InnerException}", 
                    sessionGuid, ex.Message, ex.InnerException?.Message);
                return Json(new { success = false, error = "Error sending message: " + ex.Message + (ex.InnerException != null ? " - " + ex.InnerException.Message : "") });
            }
        }

        private async Task<string> GetAgentNameAsync(int userId)
        {
            var user = await _context.Users.FindAsync(userId);
            return user?.Username ?? user?.Email ?? "Agent";
        }

        public async Task<IActionResult> Analytics()
        {
            if (!IsAgent())
            {
                return RedirectToAction("Index", "Home");
            }

            try
            {
                var agentId = GetCurrentUserId();
                var today = DateTime.UtcNow.Date;
                var last30Days = today.AddDays(-30);
                
                // Get metrics for the last 30 days
                var metrics = await _context.ChatMessageMetrics
                    .Where(m => m.AgentId == agentId && m.MetricDate >= last30Days)
                    .OrderBy(m => m.MetricDate)
                    .ToListAsync();

                // Get conversation assignments
                var assignments = await _context.ChatConversationAssignments
                    .Where(a => a.AssignedAgentId == agentId)
                    .ToListAsync();

                // Get messages sent/received
                var messages = await _context.RealTimeChatMessages
                    .Where(m => m.AssignedAgentId == agentId)
                    .ToListAsync();

                var viewModel = new AnalyticsViewModel
                {
                    DailyMetrics = metrics.Select(m => new DailyMetricViewModel
                    {
                        Date = m.MetricDate,
                        TotalMessages = m.TotalMessages,
                        UserMessages = m.UserMessages,
                        AgentMessages = m.AgentMessages,
                        AverageResponseTime = m.AverageResponseTime,
                        FirstResponseTime = m.FirstResponseTime,
                        ConversationDuration = m.ConversationDuration,
                        ResolutionTime = m.ResolutionTime,
                        CustomerSatisfaction = m.CustomerSatisfaction,
                        LeadGenerated = m.LeadGenerated,
                        LeadValue = m.LeadValue
                    }).ToList()
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading analytics for agent {AgentId}", GetCurrentUserId());
                return RedirectToAction("Index");
            }
        }
    }

    // View Models
    public class ChatbotDashboardViewModel
    {
        public List<ConversationViewModel> MyActiveConversations { get; set; } = new();
        public List<ConversationViewModel> UnassignedConversations { get; set; } = new();
        public List<AgentStatusViewModel> OnlineAgents { get; set; } = new();
        public List<MessageViewModel> RecentMessages { get; set; } = new();
        public List<NotificationViewModel> UnreadNotifications { get; set; } = new();
        public DashboardStatsViewModel Stats { get; set; } = new();
    }

    public class ConversationViewModel
    {
        public string ConversationId { get; set; } = string.Empty;
        public DateTime AssignedAt { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public DateTime? LastAgentActivityAt { get; set; }
        public List<MessageViewModel> Messages { get; set; } = new();
        public AssignmentViewModel? Assignment { get; set; }
    }

    public class AgentStatusViewModel
    {
        public int AgentId { get; set; }
        public string? AgentName { get; set; }
        public string CurrentStatus { get; set; } = string.Empty;
        public int CurrentChatCount { get; set; }
        public int MaxConcurrentChats { get; set; }
        public decimal? AverageResponseTime { get; set; }
    }

    public class MessageViewModel
    {
        public int Id { get; set; }
        public string ConversationId { get; set; } = string.Empty;
        public string MessageText { get; set; } = string.Empty;
        public string MessageType { get; set; } = string.Empty;
        public int? SenderId { get; set; }
        public string SenderName { get; set; } = string.Empty;
        public string? ImageData { get; set; }
        public DateTime SentAt { get; set; }
        public bool IsRead { get; set; }
        public DateTime? ReadAt { get; set; }
        public int? AssignedAgentId { get; set; }
        public string Priority { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Intent { get; set; }
        public decimal? Confidence { get; set; }
        public int? LeadId { get; set; }
        public bool IsLeadGenerated { get; set; }
        public int? ParentMessageId { get; set; }
    }

    public class NotificationViewModel
    {
        public int Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? RelatedConversationId { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsRead { get; set; }
        public DateTime? ReadAt { get; set; }
        public bool ActionRequired { get; set; }
        public string? ActionType { get; set; }
        public string? ActionUrl { get; set; }
    }

    public class AssignmentViewModel
    {
        public string ConversationId { get; set; } = string.Empty;
        public int? AssignedAgentId { get; set; }
        public DateTime AssignedAt { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public DateTime? LastAgentActivityAt { get; set; }
        public int TransferCount { get; set; }
    }

    public class DashboardStatsViewModel
    {
        public int TotalActiveConversations { get; set; }
        public int UnassignedConversationsCount { get; set; }
        public int OnlineAgentsCount { get; set; }
        public int UnreadNotificationsCount { get; set; }
        public int TodayTotalMessages { get; set; }
        public int TodayUserMessages { get; set; }
        public int TodayAgentMessages { get; set; }
        public decimal? TodayAverageResponseTime { get; set; }
        public bool TodayLeadsGenerated { get; set; }
        public int MemberLoginCount { get; set; }
    }

    public class AnalyticsViewModel
    {
        public List<DailyMetricViewModel> DailyMetrics { get; set; } = new();
        public OverallStatsViewModel OverallStats { get; set; } = new();
    }

    public class OverallStatsViewModel
    {
        public int TotalAssignments { get; set; }
        public int ResolvedAssignments { get; set; }
        public int ActiveAssignments { get; set; }
        public int TotalMessages { get; set; }
        public decimal? AverageResponseTime { get; set; }
        public int TotalLeadsGenerated { get; set; }
        public decimal? TotalLeadValue { get; set; }
    }

    public class DailyMetricViewModel
    {
        public DateTime Date { get; set; }
        public int TotalMessages { get; set; }
        public int UserMessages { get; set; }
        public int AgentMessages { get; set; }
        public decimal? AverageResponseTime { get; set; }
        public decimal? FirstResponseTime { get; set; }
        public int? ConversationDuration { get; set; }
        public int? ResolutionTime { get; set; }
        public int? CustomerSatisfaction { get; set; }
        public bool LeadGenerated { get; set; }
        public decimal? LeadValue { get; set; }
    }
}
