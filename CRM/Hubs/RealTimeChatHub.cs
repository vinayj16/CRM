using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using CRM.Models.Chatbot;
using CRM.Models;
using CRM.Services;
using System.Security.Claims;

namespace CRM.Hubs
{
    public class RealTimeChatHub : Hub
    {
        private readonly IChatbotService _chatbotService;
        private readonly AppDbContext _context;
        private readonly ILogger<RealTimeChatHub> _logger;
        private readonly INotificationService _notificationService;

        public RealTimeChatHub(
            IChatbotService chatbotService,
            AppDbContext context,
            ILogger<RealTimeChatHub> logger,
            INotificationService notificationService)
        {
            _chatbotService = chatbotService;
            _context = context;
            _logger = logger;
            _notificationService = notificationService;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier;
            var userRole = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
            var userName = Context.User?.Identity?.Name ?? "Unknown";
            var tenantIdClaim = Context.User?.FindFirst("TenantId")?.Value;
            int.TryParse(tenantIdClaim ?? "0", out int tenantId);

            _logger.LogInformation("User {UserId} ({UserName}) connected to chat hub, TenantId={TenantId}", userId ?? "Anonymous", userName, tenantId);

            if (IsAgent(userRole) && userId != null)
            {
                await UpdateAgentStatus(int.Parse(userId), true, "Online");
                await Groups.AddToGroupAsync(Context.ConnectionId, "Agents");
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Agent_{userId}");
                
                if (tenantId > 0)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant_{tenantId}");
                }
                
                await SendAgentActiveConversations(int.Parse(userId));
            }
            else if (tenantId > 0)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant_{tenantId}");
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.UserIdentifier;
            var userRole = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
            var tenantIdClaim = Context.User?.FindFirst("TenantId")?.Value;
            int.TryParse(tenantIdClaim ?? "0", out int tenantId);

            _logger.LogInformation("User {UserId} disconnected from chat hub", userId ?? "Anonymous");

            if (IsAgent(userRole) && userId != null)
            {
                await UpdateAgentStatus(int.Parse(userId), false, "Offline");
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Agents");
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Agent_{userId}");
                
                if (tenantId > 0)
                {
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"tenant_{tenantId}");
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        public async Task JoinConversation(string conversationId)
        {
            var userId = int.Parse(Context.UserIdentifier);
            var userRole = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
            var userName = Context.User?.Identity?.Name ?? "Unknown";

            if (!IsAgent(userRole))
            {
                _logger.LogWarning("Non-agent user {UserId} attempted to join conversation {ConversationId}", userId, conversationId);
                return;
            }

            try
            {
                // Add agent to conversation group
                await Groups.AddToGroupAsync(Context.ConnectionId, $"conversation_{conversationId}");
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Agent_{userId}");
                
                // Update agent status
                await UpdateAgentStatus(userId, true, "InConversation");
                
                // Notify other agents that this agent joined
                await Clients.Group("Agents").SendAsync("AgentJoinedConversation", new
                {
                    conversationId = conversationId,
                    agentId = userId,
                    agentName = userName,
                    timestamp = DateTime.UtcNow
                });

                // Send conversation history to the agent
                await SendConversationHistory(conversationId, userId);

                _logger.LogInformation("Agent {UserId} joined conversation {ConversationId}", userId, conversationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error joining conversation {ConversationId}", conversationId);
                await Clients.Caller.SendAsync("Error", "Failed to join conversation");
            }
        }

        public async Task LeaveConversation(string conversationId)
        {
            var userId = int.Parse(Context.UserIdentifier);
            var userRole = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
            var userName = Context.User?.Identity?.Name ?? "Unknown";

            if (!IsAgent(userRole))
            {
                return;
            }

            try
            {
                // Remove agent from conversation group
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"conversation_{conversationId}");
                
                // Update agent status
                await UpdateAgentStatus(userId, true, "Online");
                
                // Notify other agents that this agent left
                await Clients.Group("Agents").SendAsync("AgentLeftConversation", new
                {
                    conversationId = conversationId,
                    agentId = userId,
                    agentName = userName,
                    timestamp = DateTime.UtcNow
                });

                _logger.LogInformation("Agent {UserId} left conversation {ConversationId}", userId, conversationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error leaving conversation {ConversationId}", conversationId);
            }
        }

        // Allow users to join their session group to receive agent messages
        [AllowAnonymous]
        public async Task JoinSession(string sessionId)
        {
            try
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"session_{sessionId}");
                _logger.LogInformation("User joined session group: session_{SessionId}", sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error joining session {SessionId}", sessionId);
            }
        }

        // Allow users to leave their session group
        [AllowAnonymous]
        public async Task LeaveSession(string sessionId)
        {
            try
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"session_{sessionId}");
                _logger.LogInformation("User left session group: session_{SessionId}", sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error leaving session {SessionId}", sessionId);
            }
        }

        public async Task SendMessage(string conversationId, string message, string? messageType = null, int? parentMessageId = null)
        {
            var userId = int.Parse(Context.UserIdentifier);
            var userRole = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
            var userName = Context.User?.Identity?.Name ?? "Unknown";

            try
            {
                var chatMessage = new RealTimeChatMessage
                {
                    SessionId = conversationId,
                    ConversationId = conversationId,
                    MessageType = messageType ?? (IsAgent(userRole) ? "Agent" : "User"),
                    SenderId = userId,
                    SenderName = userName,
                    MessageText = message,
                    SentAt = DateTime.UtcNow,
                    ParentMessageId = parentMessageId
                };

                _context.RealTimeChatMessages.Add(chatMessage);
                await _context.SaveChangesAsync();

                if (IsAgent(userRole))
                {
                    await UpdateAgentActivity(userId);
                }

                await Clients.Group($"conversation_{conversationId}").SendAsync("ReceiveMessage", new
                {
                    id = chatMessage.Id,
                    conversationId = conversationId,
                    message = message,
                    messageType = chatMessage.MessageType,
                    senderType = chatMessage.MessageType,
                    senderId = userId,
                    senderName = userName,
                    timestamp = chatMessage.SentAt,
                    parentMessageId = parentMessageId
                });

                if (!IsAgent(userRole))
                {
                    await NotifyAllAgentsAboutNewMessage(conversationId, message, chatMessage.Id);
                }

                _logger.LogInformation("Message sent in conversation {ConversationId} by {UserId}", conversationId, userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message to conversation {ConversationId}", conversationId);
                await Clients.Caller.SendAsync("Error", "Failed to send message");
            }
        }

        public async Task SendTeamMessage(string message, string? imageData = null, string? fileName = null)
        {
            var userId = Context.UserIdentifier;
            var userRole = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
            var userName = Context.User?.Identity?.Name ?? "Unknown";
            var tenantIdClaim = Context.User?.FindFirst("TenantId")?.Value;
            int.TryParse(tenantIdClaim ?? "0", out int tenantId);

            try
            {
                var chatMessage = new RealTimeChatMessage
                {
                    TenantId = tenantId,
                    SessionId = "team-chat",
                    ConversationId = "team-chat",
                    MessageType = "Team",
                    SenderId = int.Parse(userId ?? "0"),
                    SenderName = userName,
                    MessageText = message,
                    ImageData = imageData,
                    SentAt = DateTime.UtcNow,
                    Priority = "Normal",
                    Status = "Active"
                };

                _context.RealTimeChatMessages.Add(chatMessage);
                await _context.SaveChangesAsync();

                if (tenantId > 0)
                {
                    await Clients.Group($"tenant_{tenantId}").SendAsync("ReceiveTeamMessage", new
                    {
                        id = chatMessage.Id,
                        message = message,
                        messageType = "Team",
                        senderId = int.Parse(userId ?? "0"),
                        senderName = userName,
                        senderRole = userRole,
                        timestamp = chatMessage.SentAt,
                        imageData = imageData,
                        fileName = fileName
                    });
                }

                _logger.LogInformation("Team message sent by {UserId} ({UserName}) to tenant {TenantId}", userId, userName, tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending team message by {UserId}", userId);
                await Clients.Caller.SendAsync("Error", "Failed to send team message");
            }
        }

        public async Task MarkMessageAsRead(int messageId)
        {
            var userId = int.Parse(Context.UserIdentifier);
            var userRole = Context.User?.FindFirst(ClaimTypes.Role)?.Value;

            try
            {
                var message = await _context.RealTimeChatMessages
                    .FirstOrDefaultAsync(m => m.Id == messageId);

                if (message != null)
                {
                    message.IsRead = true;
                    message.ReadAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();

                    if (message.TenantId > 0)
                    {
                        await Clients.Group($"tenant_{message.TenantId}").SendAsync("MessageRead", new
                        {
                            messageId = messageId,
                            readBy = userId,
                            timestamp = DateTime.UtcNow
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking message {MessageId} as read", messageId);
            }
        }

        public async Task UpdateAgentStatus(string status)
        {
            var userId = int.Parse(Context.UserIdentifier);
            var userRole = Context.User?.FindFirst(ClaimTypes.Role)?.Value;

            if (!IsAgent(userRole))
            {
                return;
            }

            try
            {
                await UpdateAgentStatus(userId, true, status);
                
                // Broadcast status update to all agents
                await Clients.Group("Agents").SendAsync("AgentStatusUpdate", new
                {
                    agentId = userId,
                    agentName = Context.User?.Identity?.Name,
                    status = status,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating agent status for {UserId}", userId);
            }
        }

        public async Task AssignAgentToConversation(string conversationId, int agentId)
        {
            var userId = int.Parse(Context.UserIdentifier);
            var userRole = Context.User?.FindFirst(ClaimTypes.Role)?.Value;

            if (!IsAgent(userRole))
            {
                return;
            }

            try
            {
                // Update conversation assignment
                var assignment = await _context.ChatConversationAssignments
                    .FirstOrDefaultAsync(a => a.ConversationId == conversationId);

                if (assignment == null)
                {
                    assignment = new ChatConversationAssignment
                    {
                        ConversationId = conversationId,
                        AssignedAgentId = agentId,
                        AssignedByAgentId = userId,
                        AssignedAt = DateTime.UtcNow,
                        Status = "Assigned"
                    };
                    _context.ChatConversationAssignments.Add(assignment);
                }
                else
                {
                    assignment.AssignedAgentId = agentId;
                    assignment.AssignedByAgentId = userId;
                    assignment.AssignedAt = DateTime.UtcNow;
                    assignment.Status = "Assigned";
                }

                await _context.SaveChangesAsync();

                // Notify the assigned agent
                await Clients.User(agentId.ToString()).SendAsync("ConversationAssigned", new
                {
                    conversationId = conversationId,
                    assignedBy = userId,
                    timestamp = DateTime.UtcNow
                });

                // Broadcast assignment to all agents
                await Clients.Group("Agents").SendAsync("ConversationAssignmentUpdated", new
                {
                    conversationId = conversationId,
                    assignedAgentId = agentId,
                    assignedBy = userId,
                    status = "Assigned",
                    timestamp = DateTime.UtcNow
                });

                _logger.LogInformation("Agent {AgentId} assigned to conversation {ConversationId} by {UserId}", agentId, conversationId, userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning agent {AgentId} to conversation {ConversationId}", agentId, conversationId);
            }
        }

        private async Task UpdateAgentStatus(int agentId, bool isOnline, string status)
        {
            try
            {
                var agentStatus = await _context.AgentChatStatus
                    .FirstOrDefaultAsync(a => a.AgentId == agentId);

                if (agentStatus == null)
                {
                    agentStatus = new AgentChatStatus
                    {
                        AgentId = agentId,
                        IsOnline = isOnline,
                        CurrentStatus = status,
                        LastActivityAt = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.AgentChatStatus.Add(agentStatus);
                }
                else
                {
                    agentStatus.IsOnline = isOnline;
                    agentStatus.CurrentStatus = status;
                    agentStatus.LastActivityAt = DateTime.UtcNow;
                    agentStatus.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating agent {AgentId} status", agentId);
            }
        }

        private async Task UpdateAgentActivity(int agentId)
        {
            try
            {
                var agentStatus = await _context.AgentChatStatus
                    .FirstOrDefaultAsync(a => a.AgentId == agentId);

                if (agentStatus != null)
                {
                    agentStatus.LastActivityAt = DateTime.UtcNow;
                    agentStatus.LastMessageAt = DateTime.UtcNow;
                    agentStatus.TotalMessagesSent += 1;
                    agentStatus.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating agent {AgentId} activity", agentId);
            }
        }

        private async Task SendConversationHistory(string conversationId, int agentId)
        {
            try
            {
                var messages = await _context.RealTimeChatMessages
                    .Include(m => m.Sender)
                    .Where(m => m.ConversationId == conversationId)
                    .OrderBy(m => m.SentAt)
                    .ToListAsync();

                await Clients.User(agentId.ToString()).SendAsync("ConversationHistory", new
                {
                    conversationId = conversationId,
                    messages = messages.Select(m => new
                    {
                        id = m.Id,
                        message = m.MessageText,
                        messageType = m.MessageType,
                        senderType = m.MessageType,
                        senderId = m.SenderId,
                        senderName = m.SenderName,
                        timestamp = m.SentAt,
                        isRead = m.IsRead,
                        parentMessageId = m.ParentMessageId
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending conversation history for {ConversationId}", conversationId);
            }
        }

        private async Task SendAgentActiveConversations(int agentId)
        {
            try
            {
                var assignments = await _context.ChatConversationAssignments
                    .Where(a => a.AssignedAgentId == agentId && a.Status != "Resolved")
                    .ToListAsync();

                await Clients.User(agentId.ToString()).SendAsync("ActiveConversations", assignments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending active conversations to agent {AgentId}", agentId);
            }
        }

        private async Task NotifyAllAgentsAboutNewMessage(string conversationId, string message, int messageId)
        {
            try
            {
                var onlineAgents = await _context.AgentChatStatus
                    .Where(a => a.IsOnline && a.CurrentStatus != "Offline")
                    .ToListAsync();

                foreach (var agent in onlineAgents)
                {
                    // Create notification
                    var notification = new ChatNotification
                    {
                        AgentId = agent.AgentId,
                        NotificationType = "NewMessage",
                        Title = "New User Message",
                        Message = $"New message in conversation: {message.Substring(0, Math.Min(100, message.Length))}...",
                        RelatedConversationId = conversationId,
                        RelatedMessageId = messageId,
                        CreatedAt = DateTime.UtcNow,
                        ExpiresAt = DateTime.UtcNow.AddHours(1),
                        ActionRequired = true,
                        ActionType = "View",
                        ActionUrl = $"/Chat/Conversation/{conversationId}"
                    };

                    _context.ChatNotifications.Add(notification);
                }

                await _context.SaveChangesAsync();

                // Broadcast to all agents
                await Clients.Group("Agents").SendAsync("NewMessageNotification", new
                {
                    conversationId = conversationId,
                    message = message,
                    messageId = messageId,
                    timestamp = DateTime.UtcNow
                });

                _logger.LogInformation("Notified {Count} agents about new message in conversation {ConversationId}", 
                    onlineAgents.Count, conversationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error notifying agents about new message in conversation {ConversationId}", conversationId);
            }
        }

        // ============================================================
        // COMPANY MESSAGING - Group management only
        // Messages are sent via HTTP API (CompanyChatController.SendMessage)
        // which saves to DB and broadcasts via IHubContext.
        // ============================================================

        /// <summary>
        /// Join the user's personal company chat group to receive real-time messages.
        /// </summary>
        public async Task JoinCompanyChat()
        {
            var userId = Context.UserIdentifier;
            if (userId != null)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"company_user_{userId}");
                _logger.LogInformation("User {UserId} joined company chat group", userId);
            }
        }

        /// <summary>
        /// Leave the user's personal company chat group.
        /// </summary>
        public async Task LeaveCompanyChat()
        {
            var userId = Context.UserIdentifier;
            if (userId != null)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"company_user_{userId}");
                _logger.LogInformation("User {UserId} left company chat group", userId);
            }
        }

        private bool IsAgent(string? userRole)
        {
            return !string.IsNullOrEmpty(userRole) && 
                   (userRole.Equals("Agent", StringComparison.OrdinalIgnoreCase) ||
                    userRole.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                    userRole.Equals("Manager", StringComparison.OrdinalIgnoreCase));
        }
    }
}
