using CRM.Helpers;
using CRM.Models;
using CRM.Models.Chatbot;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CRM.Services
{
    public interface IChatbotService
    {
        Task<ChatbotResponse> ProcessMessageAsync(string message, string sessionId, int? userId = null);
        Task<string> DetectIntentAsync(string message);
        Task<bool> ShouldCreateLeadAsync(string message, string intent);
        Task<LeadModel> CreateLeadFromChatAsync(ChatSessionModel session, string message, string intent);
        Task<int?> AssignAgentAsync();
        Task<ChatSessionModel> GetOrCreateSessionAsync(string sessionId, int? userId = null);
        Task LogMessageAsync(ChatLogModel log);
        Task<List<ChatIntentModel>> GetActiveIntentsAsync();
        Task<string> GetSettingAsync(string key);
        Task UpdateSessionAsync(ChatSessionModel session);
        Task<List<ChatLogModel>> GetSessionLogsAsync(string sessionId);
        Task<ChatAnalytics> GetChatAnalyticsAsync();
        Task AddImageMessageAsync(string sessionId, string imageData, int? userId = null);
        Task<string> AnalyzeImageAsync(string imageData, string message, string sessionId, int? userId = null);
        
        // Real-time chat methods
        Task<RealTimeChatMessage> CreateRealTimeMessageAsync(string sessionId, string conversationId, string message, string messageType = "User", int? senderId = null, string? senderName = null, int? parentMessageId = null);
        Task<int?> AutoAssignAgentAsync(string conversationId, string priority = "Normal");
        Task<bool> AssignAgentToConversationAsync(string conversationId, int agentId, int? assignedByAgentId = null);
        Task<List<RealTimeChatMessage>> GetConversationMessagesAsync(string conversationId);
        Task<List<AgentChatStatus>> GetOnlineAgentsAsync();
        Task<bool> UpdateAgentChatStatusAsync(int agentId, bool isOnline, string status);
        Task NotifyAgentsAboutNewMessageAsync(string conversationId, string message, int messageId);
        Task<bool> CreateConversationAssignmentAsync(string conversationId, int agentId, int? assignedByAgentId = null);
    }

    public class ChatbotService : IChatbotService
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly ILogger<ChatbotService> _logger;
        private readonly INotificationService _notificationService;
        private readonly IMongoDbService? _mongoDbService;

        private const string AwaitingBudgetIntent = "awaiting_budget";
        private const string AwaitingBudgetPrefix = "awaiting_budget:";

        public ChatbotService(
            AppDbContext context,
            IConfiguration configuration,
            HttpClient httpClient,
            ILogger<ChatbotService> logger,
            INotificationService notificationService,
            IMongoDbService? mongoDbService = null)
        {
            _context = context;
            _configuration = configuration;
            _httpClient = httpClient;
            _logger = logger;
            _notificationService = notificationService;
            _mongoDbService = mongoDbService;

            if (_mongoDbService != null)
            {
                _logger.LogInformation("MongoDB service injected - chat logs will also be stored in MongoDB.");
            }
        }

        public async Task<ChatbotResponse> ProcessMessageAsync(string message, string sessionId, int? userId = null)
        {
            var startTime = DateTime.UtcNow;
            message ??= string.Empty;

            try
            {
                var session = await GetOrCreateSessionAsync(sessionId, userId);
                session.EndedAt = DateTime.UtcNow;
                if (userId.HasValue && session.UserId != userId.Value)
                {
                    session.UserId = userId.Value;
                    await UpdateSessionAsync(session);
                }

                ChatbotResponse response;
                string intentForLog;
                string? propertyQueryForLog = null;

                if (userId.HasValue)
                {
                    if (session.IsLeadGenerated && session.GeneratedLeadId.HasValue && IsAwaitingBudgetIntent(session.LastIntent))
                    {
                        response = await ProcessBudgetCaptureAsync(message, session);
                        intentForLog = response.Intent;
                    }
                    else
                    {
                        var staffIntent = await DetectIntentAsync(message);
                        response = await ProcessCrmStaffFlowAsync(message, staffIntent, userId.Value);
                        intentForLog = staffIntent;
                        propertyQueryForLog = response.PropertyQuery;
                    }
                }
                else if (session.IsLeadGenerated && session.GeneratedLeadId.HasValue && IsAwaitingBudgetIntent(session.LastIntent))
                {
                    response = await ProcessBudgetCaptureAsync(message, session);
                    intentForLog = response.Intent;
                }
                else if (!session.IsLeadGenerated)
                {
                    var lowerMsg = (message ?? string.Empty).ToLowerInvariant();
                    
                    if (lowerMsg.Contains("properties") || lowerMsg.Contains("property") || lowerMsg.Contains("flats") || 
                        lowerMsg.Contains("houses") || lowerMsg.Contains("bangalore") || lowerMsg.Contains("hyderabad") ||
                        lowerMsg.Contains("mumbai") || lowerMsg.Contains("chennai") || lowerMsg.Contains("delhi") ||
                        lowerMsg.Contains("real estate") || lowerMsg.Contains("apartment") || lowerMsg.Contains("villa"))
                    {
                        response = await ProcessPublicUserQueryAsync(message, session);
                        
                        if (!session.IsLeadGenerated && response.Intent.Contains("property"))
                        {
                            response.Response += "\n\nTo help you better with property details and personalized assistance, I'd like to connect you with our team. Could you please share your name?";
                            session.LastIntent = "property_inquiry_lead_collection";
                            await UpdateSessionAsync(session);
                        }
                        intentForLog = response.Intent;
                    }
                    else if (TryGetVisitorCrmHelp(lowerMsg).HasValue)
                    {
                        var earlyVisitor = TryGetVisitorCrmHelp(lowerMsg);
                        var text = earlyVisitor.Value.Text;
                        response = new ChatbotResponse
                        {
                            Response = text,
                            Intent = ChatIntents.GENERAL_QUERY,
                            Confidence = 0.95
                        };
                        intentForLog = response.Intent;
                    }
                    else if (IsVisitorInformationalQuestionOnly(message, lowerMsg))
                    {
                        var aiResponse = await GenerateAIResponseAsync(message ?? string.Empty, staffContext: false, staffRole: null, session: session);
                        response = new ChatbotResponse
                        {
                            Response = aiResponse,
                            Intent = ChatIntents.GENERAL_QUERY,
                            Confidence = 0.8
                        };
                        intentForLog = response.Intent;
                    }
                    else if (lowerMsg.Contains("skip") || lowerMsg.Contains("don't want") || lowerMsg.Contains("no thanks") || 
                            lowerMsg.Contains("just browsing") || lowerMsg.Contains("no information"))
                    {
                        response = new ChatbotResponse
                        {
                            Response = "No problem! You can continue browsing. Feel free to ask me any questions about properties, pricing, or our services. If you need personalized assistance later, just let me know!",
                            Intent = ChatIntents.GENERAL_QUERY,
                            Confidence = 0.9
                        };
                        intentForLog = response.Intent;
                    }
                    else if (lowerMsg == "hi" || lowerMsg == "hello" || lowerMsg == "hey" || 
                            lowerMsg.Contains("help") || lowerMsg.Contains("assist") || lowerMsg.Contains("support"))
                    {
                        response = await ProcessLeadCollectionAsync(message, session);
                        intentForLog = response.Intent;
                    }
                    else
                    {
                        response = await ProcessPublicUserQueryAsync(message, session);
                        intentForLog = response.Intent;
                    }
                }
                else
                {
                    var detectedIntent = await DetectIntentAsync(message);
                    response = await ProcessNormalFlowAsync(message, detectedIntent, session);
                    intentForLog = detectedIntent;
                    propertyQueryForLog = response.PropertyQuery;
                }

                session.MessageCount++;
                session.LastIntent = response.Intent;
                await UpdateSessionAsync(session);

                var elapsed = DateTime.UtcNow - startTime;
                await LogMessageAsync(new ChatLogModel
                {
                    SessionId = sessionId,
                    UserId = userId,
                    UserMessage = message,
                    AiResponse = response.Response,
                    Intent = intentForLog,
                    Confidence = response.Confidence.ToString("0.00"),
                    GeneratedLeadId = session.GeneratedLeadId,
                    IsLeadGenerated = session.IsLeadGenerated,
                    PropertyQuery = propertyQueryForLog,
                    ResponseTime = $"{elapsed.TotalMilliseconds:0}ms"
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing chat message: {Message}", message);
                return new ChatbotResponse
                {
                    Response = "I apologize, but I encountered an error. Please try again.",
                    Intent = ChatIntents.UNKNOWN,
                    Confidence = 0.0
                };
            }
        }

        public async Task<string> DetectIntentAsync(string message)
        {
            var intents = await GetActiveIntentsAsync();
            var messageLower = (message ?? string.Empty).ToLowerInvariant();

            foreach (var intent in intents.OrderByDescending(i => i.Priority))
            {
                if (string.IsNullOrWhiteSpace(intent.TriggerKeywords))
                    continue;

                List<string>? keywords = null;
                try { keywords = JsonSerializer.Deserialize<List<string>>(intent.TriggerKeywords); }
                catch { }

                if (keywords?.Any(k => !string.IsNullOrWhiteSpace(k) && messageLower.Contains(k.ToLowerInvariant())) == true)
                    return intent.IntentName;
            }

            if (Regex.IsMatch(messageLower, @"\b(price|cost|emi|payment|rate|sqft|area|amenit|floor|possession|rera)\b"))
                return ChatIntents.PRICING_QUERY;

            if (Regex.IsMatch(messageLower, @"\b(meeting|site\s*visit|schedule|appointment|book\s+a\s+visit|visit\s+property|assign\s+meeting)\b"))
                return ChatIntents.APPOINTMENT_BOOKING;

            return ChatIntents.UNKNOWN;
        }

        public Task<bool> ShouldCreateLeadAsync(string message, string intent)
        {
            var createFor = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ChatIntents.PROPERTY_SEARCH,
                ChatIntents.LEAD_GENERATION,
                ChatIntents.APPOINTMENT_BOOKING,
                ChatIntents.PRICING_QUERY,
                ChatIntents.AGENT_REQUEST
            };
            return Task.FromResult(createFor.Contains(intent));
        }

        public async Task<LeadModel> CreateLeadFromChatAsync(ChatSessionModel session, string message, string intent)
        {
            if (session.GeneratedLeadId.HasValue)
            {
                var existing = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == session.GeneratedLeadId.Value);
                if (existing != null)
                    return existing;
            }

            var name = (session.UserName ?? string.Empty).Trim();
            var phone = NormalizePhone(session.UserPhone);
            var preferredLocation = ExtractLocation(message) ?? "Unknown";

            var lead = new LeadModel
            {
                Name = name,
                Contact = phone,
                Email = session.UserEmail,
                PreferredLocation = preferredLocation,
                Status = "New",
                Stage = "New",
                Source = "Chatbot"
            };

            _context.Leads.Add(lead);
            await _context.SaveChangesAsync();

            session.IsLeadGenerated = true;
            session.GeneratedLeadId = lead.LeadId;
            await UpdateSessionAsync(session);

            await AssignRandomExecutiveToChatbotLeadAsync(lead);

            return lead;
        }

        public async Task<int?> AssignAgentAsync()
        {
            var onlineIds = await _context.ChatAgents
                .Where(a => a.IsAvailable && a.IsOnline)
                .Select(a => a.UserId)
                .ToListAsync();

            var pool = onlineIds.Count > 0
                ? onlineIds
                : await _context.ChatAgents
                    .Where(a => a.IsAvailable)
                    .Select(a => a.UserId)
                    .ToListAsync();

            if (pool.Count > 0)
                return pool[Random.Shared.Next(pool.Count)];

            var salesPool = await _context.Users
                .AsNoTracking()
                .Where(u => u.IsActive &&
                    (u.Role == "Sales" || u.Role == "Agent") &&
                    u.ChannelPartnerId == null)
                .Select(u => u.UserId)
                .ToListAsync();

            if (salesPool.Count == 0)
            {
                salesPool = await _context.Users
                    .AsNoTracking()
                    .Where(u => u.IsActive && (u.Role == "Sales" || u.Role == "Agent"))
                    .Select(u => u.UserId)
                    .ToListAsync();
            }

            if (salesPool.Count == 0)
                return null;

            return salesPool[Random.Shared.Next(salesPool.Count)];
        }

        public async Task<ChatSessionModel> GetOrCreateSessionAsync(string sessionId, int? userId = null)
        {
            var normalizedSessionId = string.IsNullOrWhiteSpace(sessionId)
                ? Guid.NewGuid().ToString("N")
                : sessionId.Trim();

            var session = await _context.ChatSessions.FirstOrDefaultAsync(s => s.SessionGuid == normalizedSessionId);
            if (session != null)
                return session;

            session = new ChatSessionModel
            {
                SessionGuid = normalizedSessionId,
                UserId = userId,
                StartedAt = IndianTime.Now,
                Status = "Active",
                MessageCount = 0,
                IsLeadGenerated = false
            };

            try
            {
                _context.ChatSessions.Add(session);
                await _context.SaveChangesAsync();
                return session;
            }
            catch (Exception ex) when (ex.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("E11000", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(ex, "Detected duplicate chat session insert for {SessionId}; retrying lookup", normalizedSessionId);
                return await _context.ChatSessions.FirstOrDefaultAsync(s => s.SessionGuid == normalizedSessionId)
                    ?? session;
            }
        }

        public async Task LogMessageAsync(ChatLogModel log)
        {
            _context.ChatLogs.Add(log);
            await _context.SaveChangesAsync();

            // Also store in MongoDB if available (non-blocking, fire-and-forget)
            if (_mongoDbService != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var mongoMsg = new Models.MongoDb.ChatMessageDocument
                        {
                            SessionId = log.SessionId,
                            ConversationId = log.SessionId, // Use session ID as conversation ID for chatbot logs
                            SenderType = log.UserId.HasValue ? "User" : "Visitor",
                            SenderId = log.UserId,
                            SenderName = log.UserId?.ToString() ?? "Guest",
                            MessageText = log.UserMessage ?? string.Empty,
                            MessageType = log.Intent == "image_upload" ? "image" : "text",
                            Intent = log.Intent,
                            EntityData = log.PropertyQuery,
                            SentAt = log.CreatedOn.Kind == DateTimeKind.Utc ? log.CreatedOn : log.CreatedOn.ToUniversalTime(),
                            TenantId = 0 // Will be populated from context when available
                        };
                        await _mongoDbService.SaveChatMessageAsync(mongoMsg);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to store chat log in MongoDB (non-fatal)");
                    }
                });
            }
        }

        public async Task<List<ChatIntentModel>> GetActiveIntentsAsync()
        {
            return await _context.ChatIntents
                .Where(i => i.IsActive)
                .OrderByDescending(i => i.Priority)
                .ToListAsync();
        }

        public async Task<string> GetSettingAsync(string key)
        {
            var normalized = (key ?? string.Empty).Trim().ToLowerInvariant();
            var setting = await _context.ChatbotSettings.FirstOrDefaultAsync(s => s.IsActive && s.SettingKey.ToLower() == normalized);
            return setting?.SettingValue ?? string.Empty;
        }

        public async Task UpdateSessionAsync(ChatSessionModel session)
        {
            _context.ChatSessions.Update(session);
            await _context.SaveChangesAsync();
        }

        public async Task<List<ChatLogModel>> GetSessionLogsAsync(string sessionId)
        {
            _logger.LogInformation("GetSessionLogsAsync called with sessionId: {SessionId}", sessionId);

            int sessionIdInt;
            ChatSessionModel? session = null;

            if (int.TryParse(sessionId, out sessionIdInt))
            {
                _logger.LogInformation("Parsing sessionId as integer: {SessionIdInt}", sessionIdInt);
                session = await _context.ChatSessions
                    .FirstOrDefaultAsync(s => s.SessionId == sessionIdInt);
            }
            else
            {
                _logger.LogInformation("Looking up session by SessionGuid: {SessionId}", sessionId);
                session = await _context.ChatSessions
                    .FirstOrDefaultAsync(s => s.SessionGuid == sessionId);
            }

            if (session == null)
            {
                _logger.LogWarning("Session not found for sessionId: {SessionId}", sessionId);
                return new List<ChatLogModel>();
            }

            _logger.LogInformation("Session found: SessionId={SessionId}, SessionGuid={SessionGuid}", session.SessionId, session.SessionGuid);

            // Query ChatLogs table
            var logs = await _context.ChatLogs
                .Where(l => l.SessionId == sessionId)
                .OrderBy(l => l.CreatedOn)
                .ToListAsync();

            _logger.LogInformation("Found {Count} logs for sessionId: {SessionId}", logs.Count, sessionId);

            return logs;
        }

        public async Task<ChatAnalytics> GetChatAnalyticsAsync()
        {
            var totalSessions = await _context.ChatSessions.CountAsync();
            var totalMessages = await _context.ChatLogs.CountAsync();
            var leadsGenerated = await _context.Leads.CountAsync(l => l.Source == "Chatbot");
            var agentTransfers = await _context.ChatSessions.CountAsync(s => s.Status == "Transferred");

            var topIntentsRaw = await _context.ChatLogs
                .GroupBy(l => l.Intent)
                .Select(g => new { Intent = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync();

            var topIntents = topIntentsRaw
                .Select(x => new IntentAnalytics
                {
                    Intent = x.Intent ?? string.Empty,
                    Count = x.Count,
                    Percentage = totalMessages > 0 ? (double)x.Count * 100.0 / totalMessages : 0
                })
                .ToList();

            return new ChatAnalytics
            {
                TotalSessions = totalSessions,
                TotalMessages = totalMessages,
                LeadsGenerated = leadsGenerated,
                TopIntents = topIntents,
                AverageMessagesPerSession = totalSessions > 0 ? (double)totalMessages / totalSessions : 0,
                AgentTransfers = agentTransfers
            };
        }

        private static bool IsGreeting(string msg)
        {
            var m = msg.Trim().ToLowerInvariant();
            return m is "hi" or "hello" or "hey" or "hii" or "hlo";
        }

        private static string? NormalizePhone(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            var digits = new string(input.Where(char.IsDigit).ToArray());
            if (digits.Length > 10)
                digits = digits[^10..];
            return digits;
        }

        private static string? ExtractLocation(string message)
        {
            var lower = (message ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(lower))
                return null;

            if (lower.Contains("hyderab") || lower.Contains("hydrabad") || lower.Contains("hyderabd"))
                return "Hyderabad";

            var known = new[]
            {
                "hyderabad", "bangalore", "bengaluru", "mumbai", "delhi", "chennai", "pune", "kolkata", "ahmedabad"
            };

            foreach (var k in known)
            {
                if (lower.Contains(k))
                {
                    if (k == "bengaluru") return "Bangalore";
                    return char.ToUpperInvariant(k[0]) + k.Substring(1);
                }
            }

            return null;
        }

        private static string NormalizeLocation(string location)
        {
            var loc = (location ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(loc))
                return "Unknown";

            return string.Join(" ", loc.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
        }

        private static bool IsAwaitingBudgetIntent(string? lastIntent)
        {
            if (string.IsNullOrWhiteSpace(lastIntent))
                return false;
            if (string.Equals(lastIntent, AwaitingBudgetIntent, StringComparison.OrdinalIgnoreCase))
                return true;
            return lastIntent.StartsWith(AwaitingBudgetPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string> GenerateAIResponseAsync(string userMessage, bool staffContext = false, string? staffRole = null, ChatSessionModel? session = null)
        {
            try
            {
                var apiKey = _configuration["OpenRouter:ApiKey"];
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    return staffContext
                        ? "Ask how leads are assigned, how follow-ups work, attendance, leads today, or name a city to search inventory."
                        : "I can help with how the CRM works for buyers, channel partner login, or property search after you share your details.";
                }

                string systemPrompt;
                if (staffContext)
                {
                    systemPrompt = "You assist users already logged into a real-estate CRM. Give short, practical answers about lead assignment, follow-ups, dashboard, attendance, properties by city, subscriptions for partners, and roles.";
                }
                else
                {
                    systemPrompt = "You are a warm, human assistant for home buyers on a real-estate marketing chat. Sound natural and conversational. Cover how the CRM helps buyers, leads and follow-ups, channel partners use login.";
                }

                var messages = new List<object>
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userMessage }
                };

                var requestBody = new
                {
                    model = "openrouter/openai/gpt-3.5-turbo",
                    messages,
                    max_tokens = 400,
                    temperature = 0.6
                };

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                if (!_httpClient.DefaultRequestHeaders.Contains("HTTP-Referer"))
                    _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "https://uproptech.com");

                var resp = await _httpClient.PostAsJsonAsync("https://openrouter.ai/api/v1/chat/completions", requestBody);
                resp.EnsureSuccessStatusCode();

                var json = await resp.Content.ReadAsStringAsync();
                var parsed = JsonSerializer.Deserialize<OpenRouterResponse>(json);
                var content = parsed?.choices?.FirstOrDefault()?.message?.content;
                if (!string.IsNullOrWhiteSpace(content))
                    return content;
                return staffContext
                    ? "Say what you need leads, follow-ups, or a city to search inventory."
                    : "Happy to help tell me if you want more listings, another area, or to speak with our team.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating AI response");
                return staffContext
                    ? "Try asking about leads, follow-ups, or a city to search inventory."
                    : "I'm here to help ask about listings, locations, or getting in touch with our team.";
            }
        }

        private async Task<ChatbotResponse> ProcessLeadCollectionAsync(string message, ChatSessionModel session)
        {
            var msg = (message ?? string.Empty).Trim();
            var lower = msg.ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(msg) || IsGreeting(msg))
            {
                return new ChatbotResponse
                {
                    Response = "Hi! I'm here to help you with property information. You can ask me about properties in any city, pricing details, or general questions. If you'd like personalized assistance, I can connect you with an agent. What would you like to know?",
                    Intent = "greeting",
                    Confidence = 1.0
                };
            }

            if (string.IsNullOrWhiteSpace(session.UserName))
            {
                var extractedName = ExtractName(msg);
                if (string.IsNullOrWhiteSpace(extractedName))
                {
                    return new ChatbotResponse
                    {
                        Response = "I need your name to help you. Could you please share your name?",
                        Intent = "collect_name",
                        Confidence = 1.0
                    };
                }

                session.UserName = extractedName;
                await UpdateSessionAsync(session);

                return new ChatbotResponse
                {
                    Response = "Thanks! Please share your phone number.",
                    Intent = "collect_phone",
                    Confidence = 1.0
                };
            }

            if (string.IsNullOrWhiteSpace(session.UserPhone))
            {
                var phone = NormalizePhone(msg);
                if (string.IsNullOrWhiteSpace(phone) || phone.Length < 10)
                {
                    return new ChatbotResponse
                    {
                        Response = "I need a valid phone number. Please enter a 10-digit number (like 9876543210).",
                        Intent = "collect_phone",
                        Confidence = 1.0
                    };
                }

                session.UserPhone = phone;
                await UpdateSessionAsync(session);

                return new ChatbotResponse
                {
                    Response = "Great! Which location are you looking for?",
                    Intent = "collect_location",
                    Confidence = 1.0
                };
            }

            var extractedLocation = ExtractLocation(msg);
            if (string.IsNullOrWhiteSpace(extractedLocation))
            {
                return new ChatbotResponse
                {
                    Response = "I need a city or area name. For example: Hyderabad, Bangalore, Mumbai. What location are you interested in?",
                    Intent = "collect_location",
                    Confidence = 1.0
                };
            }
            
            var normalizedLocation = NormalizeLocation(extractedLocation);
            
            var onlineAgents = await _context.ChatAgents
                .Where(a => a.IsOnline && a.IsAvailable)
                .CountAsync();
            
            await CreateLeadAndShowProperties(session, normalizedLocation);

            var responseMessage = $"Thanks {session.UserName}! Here are properties in {normalizedLocation}:\n\n{await GetPropertiesByLocationTextAsync(normalizedLocation)}\n\n";
            
            if (onlineAgents > 0)
            {
                var assignedAgent = await _context.ChatAgents
                    .Where(a => a.IsOnline && a.IsAvailable)
                    .FirstOrDefaultAsync();
                
                if (assignedAgent != null)
                {
                    var agentUser = await _context.Users.FindAsync(assignedAgent.UserId);
                    var agentName = agentUser?.Username ?? agentUser?.Email ?? "Available Agent";
                    responseMessage += $"✅ **Your inquiry has been assigned to {agentName}.**\n\nThey will contact you shortly to help you with your property requirements.";
                }
                else
                {
                    responseMessage += "✅ **Your inquiry has been assigned to one of our available agents.**\n\nThey will contact you shortly to help you with your property requirements.";
                }
            }
            else
            {
                responseMessage += "⏰ **Our team is currently offline.**\n\nYour inquiry has been saved and an agent will contact you when available.";
            }

            return new ChatbotResponse
            {
                Response = responseMessage,
                Intent = ChatIntents.PROPERTY_SEARCH,
                Confidence = 1.0,
                ShouldCreateLead = true,
                GeneratedLeadId = session.GeneratedLeadId,
                PropertyQuery = normalizedLocation
            };
        }

        private async Task CreateLeadAndShowProperties(ChatSessionModel session, string preferredLocation)
        {
            var lead = new LeadModel
            {
                Name = session.UserName?.Trim(),
                Contact = NormalizePhone(session.UserPhone),
                Email = session.UserEmail,
                PreferredLocation = preferredLocation,
                Status = "New",
                Stage = "New",
                Source = "Chatbot"
            };

            _context.Leads.Add(lead);
            await _context.SaveChangesAsync();

            session.IsLeadGenerated = true;
            session.GeneratedLeadId = lead.LeadId;
            await UpdateSessionAsync(session);

            var onlineAgents = await _context.ChatAgents
                .Where(a => a.IsOnline && a.IsAvailable)
                .CountAsync();

            if (onlineAgents > 0)
            {
                await AssignRandomExecutiveToChatbotLeadAsync(lead);
            }
            else
            {
                _logger.LogInformation("No online agents available. Lead {LeadId} will be manually assigned by admin/channel partner.", lead.LeadId);
            }
        }

        private async Task<ChatbotResponse> ProcessBudgetCaptureAsync(string message, ChatSessionModel session)
        {
            var budgetText = (message ?? string.Empty).Trim();
            var normalizedBudget = NormalizeBudget(budgetText);

            var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == session.GeneratedLeadId!.Value);
            if (lead != null && !string.IsNullOrWhiteSpace(normalizedBudget))
            {
                lead.Requirement = UpsertBudgetInRequirement(lead.Requirement, normalizedBudget);
                lead.ModifiedOn = IndianTime.Now;
                _context.Leads.Update(lead);
                await _context.SaveChangesAsync();
            }

            var agentUserId = await AssignAgentAsync();
            var agentName = agentUserId.HasValue ? await GetUserDisplayNameAsync(agentUserId.Value) : null;
            session.AssignedAgentId = agentUserId;
            session.Status = "Transferred";
            await UpdateSessionAsync(session);

            return new ChatbotResponse
            {
                Response = "Thanks! " + FormatAgentTransferUserFacingMessage(agentName),
                Intent = ChatIntents.AGENT_REQUEST,
                Confidence = 1.0,
                ShouldTransferToAgent = agentUserId.HasValue,
                AssignedAgentId = agentUserId,
                AssignedAgentName = agentName,
                GeneratedLeadId = session.GeneratedLeadId
            };
        }

        private async Task<ChatbotResponse> ProcessNormalFlowAsync(string message, string intent, ChatSessionModel session)
        {
            var agentUserId = await AssignAgentAsync();
            var agentName = agentUserId.HasValue ? await GetUserDisplayNameAsync(agentUserId.Value) : null;
            
            return new ChatbotResponse
            {
                Response = FormatAgentTransferUserFacingMessage(agentName),
                Intent = ChatIntents.AGENT_REQUEST,
                Confidence = 1.0,
                ShouldTransferToAgent = agentUserId.HasValue,
                AssignedAgentId = agentUserId,
                AssignedAgentName = agentName
            };
        }

        private async Task<ChatbotResponse> ProcessCrmStaffFlowAsync(string message, string intent, int userId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return new ChatbotResponse
                    {
                        Response = "I'm having trouble identifying your account. Please try logging in again.",
                        Intent = "error",
                        Confidence = 0.0
                    };
                }

                var userRole = user.Role?.ToLower() ?? "public";
                
                switch (userRole)
                {
                    case "admin":
                        return await ProcessAdminFlowAsync(message, intent, userId);
                    
                    case "agent":
                        return await ProcessAgentFlowAsync(message, intent, userId);
                    
                    case "channelpartner":
                        return await ProcessChannelPartnerFlowAsync(message, intent, userId);
                    
                    default:
                        var publicSession = new ChatSessionModel();
                        return await ProcessPublicUserQueryAsync(message, publicSession);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing CRM staff flow for user {UserId}", userId);
                return new ChatbotResponse
                {
                    Response = "I encountered an error while processing your request. Please try again.",
                    Intent = "error",
                    Confidence = 0.0
                };
            }
        }

        private async Task<ChatbotResponse> ProcessAdminFlowAsync(string message, string intent, int userId)
        {
            var messageLower = message.ToLower();
            
            if (messageLower.Contains("guide") && messageLower.Contains("lead"))
            {
                return new ChatbotResponse
                {
                    Response = "**Step-by-Step Lead Creation Guide**\n\n" +
                              "**Step 1: Navigate to Lead Management**\n" +
                              "- Go to the main menu\n" +
                              "- Click on 'Lead Management'\n" +
                              "- Select 'Add New Lead'\n\n" +
                              "**Step 2: Fill Required Information**\n" +
                              "- Lead Name: Enter the full name\n" +
                              "- Contact Number: 10-digit mobile number\n" +
                              "- Email Address: Valid email format\n" +
                              "- Property Interest: What type of property they want\n\n" +
                              "**Step 3: Add Optional Details**\n" +
                              "- Preferred Location: City/Area preference\n" +
                              "- Budget Range: Minimum and maximum budget\n" +
                              "- Property Type: Flat, House, Villa, etc.\n" +
                              "- Source: How they found you (Website, Referral, etc.)\n\n" +
                              "**Step 4: Assign Agent**\n" +
                              "- Choose an available agent\n" +
                              "- Or leave unassigned for automatic assignment\n\n" +
                              "**Step 5: Save and Follow Up**\n" +
                              "- Click 'Save Lead'\n" +
                              "- Note the lead ID for future reference\n" +
                              "- Schedule first follow-up if needed\n\n" +
                              "**Pro Tips:**\n" +
                              "- Double-check phone numbers for accuracy\n" +
                              "- Add detailed property requirements\n" +
                              "- Set follow-up reminders for hot leads\n\n" +
                              "Ready to create a lead? Just provide the lead details and I'll help you format them correctly!",
                    Intent = "admin_lead_guidance",
                    Confidence = 0.9
                };
            }
            
            if (messageLower.Contains("add") && messageLower.Contains("lead"))
            {
                return new ChatbotResponse
                {
                    Response = "**Add New Lead**\n\n" +
                              "To add a new lead, I need the following information:\n\n" +
                              "**Required Information:**\n" +
                              "- Lead Name\n" +
                              "- Contact Number\n" +
                              "- Email Address\n" +
                              "- Property Interest\n\n" +
                              "**Optional Information:**\n" +
                              "- Preferred Location\n" +
                              "- Budget Range\n" +
                              "- Property Type\n" +
                              "- Source (Website, Referral, etc.)\n\n" +
                              "**How to Add:**\n" +
                              "1. Go to Lead Management section\n" +
                              "2. Click 'Add New Lead'\n" +
                              "3. Fill in the lead details\n" +
                              "4. Assign to an agent (optional)\n" +
                              "5. Save the lead\n\n" +
                              "Would you like me to guide you through the lead creation process step by step?",
                    Intent = "admin_add_lead",
                    Confidence = 0.9
                };
            }
            
            if (messageLower.Contains("lead"))
            {
                var todayLeads = await _context.Leads.CountAsync(l => l.CreatedOn.Date == DateTime.Today);
                var totalLeads = await _context.Leads.CountAsync();
                var pendingLeads = await _context.Leads.CountAsync(l => l.Status == "New");
                var convertedLeads = await _context.Leads.CountAsync(l => l.Status == "Converted");
                
                return new ChatbotResponse
                {
                    Response = $"**Lead Management Dashboard**\n\n" +
                              $"Today's New Leads: {todayLeads}\n" +
                              $"Total Leads: {totalLeads}\n" +
                              $"Pending Leads: {pendingLeads}\n" +
                              $"Converted Leads: {convertedLeads}\n\n" +
                              $"I can help you with:\n" +
                              $"- Add new leads\n" +
                              $"- Lead status updates\n" +
                              $"- Lead assignment to agents\n" +
                              $"- Lead follow-up tracking\n" +
                              $"- Lead conversion analytics\n\n" +
                              $"Would you like to add a new lead or check existing lead status?",
                    Intent = "admin_lead_management",
                    Confidence = 0.9
                };
            }
            
            if (messageLower.Contains("lead") && (messageLower.Contains("status") || messageLower.Contains("report")))
            {
                var pendingLeads = await _context.Leads.CountAsync(l => l.Status == "New");
                var convertedLeads = await _context.Leads.CountAsync(l => l.Status == "Converted");
                var activeDeals = await _context.Bookings.CountAsync(b => b.Status == "Active");
                
                return new ChatbotResponse
                {
                    Response = $"**Lead Status Report**\n\n" +
                              $"Pending Leads: {pendingLeads}\n" +
                              $"Converted Leads: {convertedLeads}\n" +
                              $"Active Deals: {activeDeals}\n\n" +
                              $"Would you like detailed analytics for any specific category?",
                    Intent = "admin_lead_report",
                    Confidence = 0.9
                };
            }
            
            if (messageLower.Contains("attendance") || messageLower.Contains("attendence"))
            {
                var todayDate = DateTime.Today;
                var totalAgents = await _context.Agents.CountAsync();
                
                var todayAttendance = await _context.AgentAttendance
                    .Where(aa => aa.Date.Date == todayDate)
                    .ToListAsync();
                
                var presentCount = todayAttendance.Count(aa => aa.Status?.ToLower() == "present");
                var absentCount = todayAttendance.Count(aa => aa.Status?.ToLower() == "absent");
                var leaveCount = todayAttendance.Count(aa => aa.Status?.ToLower() == "leave");
                var halfDayCount = todayAttendance.Count(aa => aa.Status?.ToLower() == "halfday");
                
                var attendanceRate = totalAgents > 0 ? (presentCount * 100.0 / totalAgents) : 0;
                
                return new ChatbotResponse
                {
                    Response = $"**Attendance Monitoring Report**\n\n" +
                              $"Date: {todayDate:dd MMM yyyy}\n" +
                              $"Total Agents: {totalAgents}\n" +
                              $"Present: {presentCount}\n" +
                              $"Absent: {absentCount}\n" +
                              $"On Leave: {leaveCount}\n" +
                              $"Half Day: {halfDayCount}\n" +
                              $"Attendance Rate: {attendanceRate:F1}%\n\n" +
                              $"I can help you with:\n" +
                              $"- Individual attendance details\n" +
                              $"- Weekly attendance reports\n" +
                              $"- Attendance correction requests\n" +
                              $"- Monthly attendance summaries\n\n" +
                              $"Would you like detailed attendance for any specific agent or time period?",
                    Intent = "admin_attendance_monitoring",
                    Confidence = 0.9
                };
            }
            
            if (messageLower.Contains("agent") && (messageLower.Contains("performance") || messageLower.Contains("report")))
            {
                var totalAgents = await _context.Agents.CountAsync();
                var activeAgents = await _context.Agents.CountAsync(a => a.Status == "Active");
                
                return new ChatbotResponse
                {
                    Response = $"**Agent Performance Report**\n\n" +
                              $"Total Agents: {totalAgents}\n" +
                              $"Active Agents: {activeAgents}\n\n" +
                              $"I can help you with:\n" +
                              $"- Individual agent performance metrics\n" +
                              $"- Agent commission tracking\n" +
                              $"- Agent attendance reports\n" +
                              $"- Agent lead conversion rates",
                    Intent = "admin_agent_management",
                    Confidence = 0.9
                };
            }
            
            if (messageLower.Contains("revenue") || messageLower.Contains("income") || messageLower.Contains("sales") || messageLower.Contains("payment"))
            {
                var todayRevenue = await _context.Payments
                    .Where(p => p.PaymentDate.Date == DateTime.Today)
                    .SumAsync(p => p.Amount);
                
                var monthlyRevenue = await _context.Payments
                    .Where(p => p.PaymentDate.Month == DateTime.Today.Month && p.PaymentDate.Year == DateTime.Today.Year)
                    .SumAsync(p => p.Amount);
                
                var totalRevenue = await _context.Payments
                    .SumAsync(p => p.Amount);
                
                return new ChatbotResponse
                {
                    Response = $"**Revenue Dashboard**\n\n" +
                              $"Today's Revenue: {todayRevenue:C}\n" +
                              $"Monthly Revenue: {monthlyRevenue:C}\n" +
                              $"Total Revenue: {totalRevenue:C}\n\n" +
                              $"I can help you with:\n" +
                              $"- Detailed financial reports\n" +
                              $"- Payment analytics\n" +
                              $"- Commission tracking\n" +
                              $"- Revenue by property type\n\n" +
                              $"Would you like detailed revenue analysis for any specific period?",
                    Intent = "admin_revenue",
                    Confidence = 0.9
                };
            }
            
            if (messageLower.Contains("property") && (messageLower.Contains("bangalore") || messageLower.Contains("hyderabad") || messageLower.Contains("mumbai") || messageLower.Contains("chennai") || messageLower.Contains("delhi")))
            {
                string location = null;
                if (messageLower.Contains("bangalore")) location = "Bangalore";
                else if (messageLower.Contains("hyderabad")) location = "Hyderabad";
                else if (messageLower.Contains("mumbai")) location = "Mumbai";
                else if (messageLower.Contains("chennai")) location = "Chennai";
                else if (messageLower.Contains("delhi")) location = "Delhi";
                
                var locationProperties = await _context.Properties
                    .Where(p => p.Location != null && p.Location.ToLower().Contains(location.ToLower()))
                    .ToListAsync();
                
                if (locationProperties.Any())
                {
                    var propertyInfo = string.Join("\n", locationProperties.Select((p, i) => 
                        $"{i + 1}. {p.PropertyName} - {p.Location}\n   Price: {p.Price:C}\n   Area: {p.AreaSqft} sqft\n   Builder: {p.Developer}\n   Status: {(p.IsActive ? "Available" : "Sold")}"));
                    
                    return new ChatbotResponse
                    {
                        Response = $"**Properties in {location}**\n\n{propertyInfo}\n\n" +
                                  $"Total Properties: {locationProperties.Count}\n" +
                                  $"Available: {locationProperties.Count(p => p.IsActive)}\n" +
                                  $"Sold: {locationProperties.Count(p => !p.IsActive)}\n\n" +
                                  $"I can help you with:\n" +
                                  $"- Detailed property information\n" +
                                  $"- Pricing analysis for {location}\n" +
                                  $"- Builder performance in {location}\n" +
                                  $"- Property availability updates\n\n" +
                                  $"Would you like more details about any specific property?",
                        Intent = "admin_property_location",
                        Confidence = 0.9
                    };
                }
                else
                {
                    return new ChatbotResponse
                    {
                        Response = $"**Properties in {location}**\n\n" +
                                  $"No properties found in {location}.\n\n" +
                                  $"I can help you with:\n" +
                                  $"- Check other locations\n" +
                                  $"- Add new properties in {location}\n" +
                                  $"- Property inventory overview\n\n" +
                                  $"Would you like to check properties in other locations?",
                        Intent = "admin_property_location_empty",
                        Confidence = 0.8
                    };
                }
            }
            
            if (messageLower.Contains("property") || messageLower.Contains("inventory"))
            {
                var totalProperties = await _context.Properties.CountAsync();
                var availableProperties = await _context.Properties.CountAsync(p => p.IsActive);
                var soldProperties = await _context.Properties.CountAsync(p => !p.IsActive);
                
                var propertiesByLocation = await _context.Properties
                    .Where(p => p.Location != null)
                    .GroupBy(p => p.Location)
                    .Select(g => new { Location = g.Key, Count = g.Count() })
                    .OrderByDescending(g => g.Count)
                    .Take(5)
                    .ToListAsync();
                
                var locationInfo = string.Join("\n", propertiesByLocation.Select(pl => $"- {pl.Location}: {pl.Count} properties"));
                
                return new ChatbotResponse
                {
                    Response = $"**Property Inventory Report**\n\n" +
                              $"Total Properties: {totalProperties}\n" +
                              $"Available: {availableProperties}\n" +
                              $"Sold: {soldProperties}\n\n" +
                              $"**Top Locations:**\n{locationInfo}\n\n" +
                              $"I can help you with:\n" +
                              $"- Property details by location\n" +
                              $"- Property pricing analysis\n" +
                              $"- Builder performance metrics\n" +
                              $"- Property availability status\n\n" +
                              $"Would you like detailed property information for any specific location?",
                    Intent = "admin_property_inventory",
                    Confidence = 0.9
                };
            }
            
            return new ChatbotResponse
            {
                Response = $"**Admin Assistant**\n\n" +
                          $"I can help you with:\n\n" +
                          $"**Lead Management:**\n" +
                          $"- Lead status reports\n" +
                          $"- Lead assignment\n" +
                          $"- Lead conversion analytics\n\n" +
                          $"**Agent Management:**\n" +
                          $"- Agent performance reports\n" +
                          $"- Commission tracking\n" +
                          $"- Attendance monitoring\n\n" +
                          $"**Financial Reports:**\n" +
                          $"- Revenue analytics\n" +
                          $"- Payment tracking\n" +
                          $"- Commission payouts\n\n" +
                          $"**Property Management:**\n" +
                          $"- Inventory reports\n" +
                          $"- Builder performance\n" +
                          $"- Pricing analysis\n\n" +
                          $"What would you like to manage today?",
                Intent = "admin_dashboard",
                Confidence = 0.8
            };
        }

        private async Task<ChatbotResponse> ProcessAgentFlowAsync(string message, string intent, int userId)
        {
            var messageLower = message.ToLower();
            
            if (messageLower.Contains("update") && messageLower.Contains("lead") && messageLower.Contains("status"))
            {
                return new ChatbotResponse
                {
                    Response = "**Update Lead Status - Step by Step Guide**\n\n" +
                              "**Step 1: Select the Lead**\n" +
                              "- Go to your lead list\n" +
                              "- Find the lead you want to update\n" +
                              "- Click on the lead name or ID\n\n" +
                              "**Step 2: Update Status**\n" +
                              "- Look for 'Status' field\n" +
                              "- Choose from available options:\n" +
                              "  * New - Initial contact made\n" +
                              "  * Active - In communication\n" +
                              "  * Hot - Ready to buy\n" +
                              "  * Cold - Not interested\n" +
                              "  * Converted - Deal closed\n" +
                              "  * Lost - Deal lost\n\n" +
                              "**Step 3: Add Reason (Optional but Recommended)**\n" +
                              "- Add a note explaining the status change\n" +
                              "- Include next action required\n" +
                              "- Mention any important conversation details\n\n" +
                              "**Step 4: Save Changes**\n" +
                              "- Click 'Update Status'\n" +
                              "- Verify the change was saved\n" +
                              "- Set follow-up reminder if needed\n\n" +
                              "**Pro Tips:**\n" +
                              "- Update status daily for active leads\n" +
                              "- Always add notes for status changes\n" +
                              "- Set reminders for follow-ups\n" +
                              "- Mark hot leads for priority attention\n\n" +
                              "Which lead would you like to update? I can help you identify the right status!",
                    Intent = "agent_update_status_guidance",
                    Confidence = 0.9
                };
            }
            
            if (messageLower.Contains("add") && messageLower.Contains("lead") && messageLower.Contains("notes"))
            {
                return new ChatbotResponse
                {
                    Response = "**Add Lead Notes - Step by Step Guide**\n\n" +
                              "**Step 1: Open the Lead**\n" +
                              "- Go to your lead list\n" +
                              "- Click on the specific lead\n" +
                              "- Find the 'Notes' section\n\n" +
                              "**Step 2: Add Your Note**\n" +
                              "- Click 'Add Note' or 'Add Comment'\n" +
                              "- Write clear, concise notes\n\n" +
                              "**What to Include in Notes:**\n" +
                              "- Date and time of conversation\n" +
                              "- Key discussion points\n" +
                              "- Client requirements/preferences\n" +
                              "- Next action items\n" +
                              "- Any promises made\n" +
                              "- Client concerns/objections\n\n" +
                              "**Example Note Format:**\n" +
                              "\"[Date] Spoke with client about 2BHK in Bangalore. Budget 50L-70L. Prefers ready-to-move. Next follow-up on [date] for property visit.\"\n\n" +
                              "**Step 3: Save and Set Reminder**\n" +
                              "- Save the note\n" +
                              "- Set follow-up reminder if needed\n" +
                              "- Tag team members if relevant\n\n" +
                              "**Best Practices:**\n" +
                              "- Be specific and factual\n" +
                              "- Include dates and times\n" +
                              "- Mention property requirements\n" +
                              "- Note client emotions/urgency\n" +
                              "- Update after every meaningful interaction\n\n" +
                              "Which lead would you like to add notes for? I can help you structure your notes!",
                    Intent = "agent_add_notes_guidance",
                    Confidence = 0.9
                };
            }
            
            if (messageLower.Contains("lead"))
            {
                var agentLeads = await _context.Leads
                    .Where(l => l.ExecutiveId == userId)
                    .OrderByDescending(l => l.CreatedOn)
                    .Take(5)
                    .ToListAsync();
                
                var thisMonthLeads = await _context.Leads
                    .CountAsync(l => l.ExecutiveId == userId && l.CreatedOn.Month == DateTime.Today.Month);
                
                var pendingLeads = agentLeads.Count(l => l.Status == "New");
                var convertedLeads = agentLeads.Count(l => l.Status == "Converted");
                
                if (agentLeads.Any())
                {
                    var leadsInfo = string.Join("\n", agentLeads.Take(5).Select((l, i) => 
                        $"{i + 1}. {l.Name} - {l.Contact} - Status: {l.Status} - Created: {l.CreatedOn:dd MMM yyyy}"));
                    
                    return new ChatbotResponse
                    {
                        Response = $"**Your Lead Management**\n\n" +
                                  $"This Month's Leads: {thisMonthLeads}\n" +
                                  $"Pending Leads: {pendingLeads}\n" +
                                  $"Converted Leads: {convertedLeads}\n\n" +
                                  $"**Recent Leads:**\n{leadsInfo}\n\n" +
                                  $"I can help you with:\n" +
                                  $"- Update lead status\n" +
                                  $"- Schedule follow-ups\n" +
                                  $"- Add lead notes\n" +
                                  $"- Convert leads to bookings\n\n" +
                                  $"What would you like to do with your leads?",
                        Intent = "agent_lead_management",
                        Confidence = 0.9
                    };
                }
                else
                {
                    return new ChatbotResponse
                    {
                        Response = "**Your Lead Management**\n\n" +
                                  $"This Month's Leads: {thisMonthLeads}\n" +
                                  $"Pending Leads: {pendingLeads}\n" +
                                  $"Converted Leads: {convertedLeads}\n\n" +
                                  $"You don't have any assigned leads yet. Contact your admin to get leads assigned.",
                        Intent = "agent_no_leads",
                        Confidence = 0.8
                    };
                }
            }
            
            if (messageLower.Contains("follow") || messageLower.Contains("followup") || messageLower.Contains("follow-up"))
            {
                var todayFollowUps = await _context.LeadFollowUps
                    .Where(f => f.ExecutiveId == userId && f.FollowUpDate.HasValue && f.FollowUpDate.Value.Date == DateTime.Today)
                    .ToListAsync();
                
                var pendingFollowUps = await _context.LeadFollowUps
                    .Where(f => f.ExecutiveId == userId && f.Status != "Completed")
                    .OrderBy(f => f.FollowUpDate)
                    .Take(10)
                    .ToListAsync();
                
                var todayInfo = todayFollowUps.Any() 
                    ? string.Join("\n", todayFollowUps.Select(f => $"- {f.FollowUpDate:HH:mm} - Lead ID: {f.LeadId} - {f.Comments}"))
                    : "No follow-ups scheduled for today";
                
                var pendingInfo = pendingFollowUps.Any()
                    ? string.Join("\n", pendingFollowUps.Select(f => $"- {f.FollowUpDate:dd MMM HH:mm} - Lead ID: {f.LeadId} - {f.Comments}"))
                    : "No pending follow-ups";
                
                return new ChatbotResponse
                {
                    Response = $"**Follow-Up Management**\n\n" +
                              $"**Today's Schedule:**\n{todayInfo}\n\n" +
                              $"**Pending Follow-ups:**\n{pendingInfo}\n\n" +
                              $"I can help you with:\n" +
                              $"- Schedule new follow-ups\n" +
                              $"- Mark follow-ups as completed\n" +
                              $"- Update follow-up notes\n" +
                              $"- Reschedule follow-ups\n\n" +
                              $"What would you like to do with your follow-ups?",
                    Intent = "agent_followup_management",
                    Confidence = 0.9
                };
            }
            
            if (messageLower.Contains("today") || messageLower.Contains("schedule") || messageLower.Contains("appointments"))
            {
                var todayFollowUps = await _context.LeadFollowUps
                    .Where(f => f.ExecutiveId == userId && f.FollowUpDate.HasValue && f.FollowUpDate.Value.Date == DateTime.Today)
                    .ToListAsync();
                
                if (todayFollowUps.Any())
                {
                    var scheduleInfo = string.Join("\n", todayFollowUps.Select(f => 
                        $"- {f.FollowUpDate:HH:mm} - Lead ID: {f.LeadId} - {f.Comments}"));
                    
                    return new ChatbotResponse
                    {
                        Response = $"**Today's Schedule**\n\n{scheduleInfo}\n\n" +
                                  $"You have {todayFollowUps.Count} follow-ups today. Would you like to mark any as completed?",
                        Intent = "agent_schedule",
                        Confidence = 0.9
                    };
                }
                else
                {
                    return new ChatbotResponse
                    {
                        Response = "You don't have any scheduled follow-ups for today. Would you like to see your pending leads?",
                        Intent = "agent_no_schedule",
                        Confidence = 0.8
                    };
                }
            }
            
            if (messageLower.Contains("performance") || messageLower.Contains("commission") || messageLower.Contains("earnings"))
            {
                var thisMonthLeads = await _context.Leads
                    .CountAsync(l => l.ExecutiveId == userId && l.CreatedOn.Month == DateTime.Today.Month);
                
                var convertedLeads = await _context.Leads
                    .CountAsync(l => l.ExecutiveId == userId && l.Status == "Converted");
                
                var thisMonthRevenue = await _context.Payments
                    .Where(p => p.PaymentDate.Month == DateTime.Today.Month && p.PaymentDate.Year == DateTime.Today.Year)
                    .SumAsync(p => p.Amount);
                
                return new ChatbotResponse
                {
                    Response = $"**Your Performance Dashboard**\n\n" +
                              $"This Month's Leads: {thisMonthLeads}\n" +
                              $"Total Converted: {convertedLeads}\n" +
                              $"Conversion Rate: {(thisMonthLeads > 0 ? (convertedLeads * 100 / thisMonthLeads) : 0)}%\n" +
                              $"This Month's Revenue: {thisMonthRevenue:C}\n\n" +
                              $"I can help you with:\n" +
                              $"- Detailed performance reports\n" +
                              $"- Commission breakdown\n" +
                              $"- Lead conversion strategies\n" +
                              $"- Performance comparison with team\n\n" +
                              $"What aspect of your performance would you like to explore?",
                    Intent = "agent_performance",
                    Confidence = 0.9
                };
            }
            
            if (messageLower.Contains("property") && (messageLower.Contains("bangalore") || messageLower.Contains("hyderabad") || messageLower.Contains("mumbai") || messageLower.Contains("chennai") || messageLower.Contains("delhi")))
            {
                string location = null;
                if (messageLower.Contains("bangalore")) location = "Bangalore";
                else if (messageLower.Contains("hyderabad")) location = "Hyderabad";
                else if (messageLower.Contains("mumbai")) location = "Mumbai";
                else if (messageLower.Contains("chennai")) location = "Chennai";
                else if (messageLower.Contains("delhi")) location = "Delhi";
                
                var locationProperties = await _context.Properties
                    .Where(p => p.Location != null && p.Location.ToLower().Contains(location.ToLower()) && p.IsActive)
                    .ToListAsync();
                
                if (locationProperties.Any())
                {
                    var propertyInfo = string.Join("\n", locationProperties.Select((p, i) => 
                        $"{i + 1}. {p.PropertyName} - {p.Location}\n   Price: {p.Price:C}\n   Area: {p.AreaSqft} sqft\n   Builder: {p.Developer}\n   ID: {p.PropertyId}"));
                    
                    return new ChatbotResponse
                    {
                        Response = $"**Properties in {location}**\n\n{propertyInfo}\n\n" +
                                  $"I can help you with:\n" +
                                  $"- Detailed property information\n" +
                                  $"- Client matching for these properties\n" +
                                  $"- Property visit scheduling\n" +
                                  $"- Pricing negotiation tips\n\n" +
                                  $"Would you like more details about any specific property?",
                        Intent = "agent_property_location",
                        Confidence = 0.9
                    };
                }
                else
                {
                    return new ChatbotResponse
                    {
                        Response = $"No available properties found in {location}.\n\n" +
                                  $"I can help you with:\n" +
                                  $"- Check other locations\n" +
                                  $"- Get property requirements from clients\n" +
                                  $"- Notify when properties become available\n\n" +
                                  $"Would you like to check properties in other locations?",
                        Intent = "agent_property_location_empty",
                        Confidence = 0.8
                    };
                }
            }
            
            if (messageLower.Contains("property") || messageLower.Contains("search"))
            {
                return new ChatbotResponse
                {
                    Response = "**Property Search Assistant**\n\n" +
                              "I can help you find properties for your clients. Please provide:\n\n" +
                              "**Client Requirements:**\n" +
                              "- Location preference\n" +
                              "- Budget range\n" +
                              "- Property type (flat, house, villa)\n" +
                              "- BHK preference\n" +
                              "- Specific amenities\n\n" +
                              "**I can help you with:**\n" +
                              "- Find matching properties\n" +
                              "- Check property availability\n" +
                              "- Schedule property visits\n" +
                              "- Get pricing information\n\n" +
                              "What are your client's requirements?",
                    Intent = "agent_property_search",
                    Confidence = 0.8
                };
            }
            
            return new ChatbotResponse
            {
                Response = "**Agent Assistant**\n\n" +
                          $"I can help you with:\n\n" +
                          $"**Lead Management:**\n" +
                          $"- View your assigned leads\n" +
                          $"- Update lead status\n" +
                          $"- Schedule follow-ups\n\n" +
                          $"**Performance Tracking:**\n" +
                          $"- View your conversion rates\n" +
                          $"- Commission details\n" +
                          $"- Lead analytics\n\n" +
                          $"**Property Search:**\n" +
                          $"- Find properties for clients\n" +
                          $"- Check availability\n" +
                          $"- Pricing information\n\n" +
                          $"What would you like to work on?",
                Intent = "agent_dashboard",
                Confidence = 0.8
            };
        }

        private async Task<ChatbotResponse> ProcessChannelPartnerFlowAsync(string message, string intent, int userId)
        {
            var messageLower = message.ToLower();
            
            if (messageLower.Contains("lead"))
            {
                var partnerLeads = await _context.PartnerLeads
                    .Where(pl => pl.PartnerId == userId)
                    .OrderByDescending(pl => pl.CreatedOn)
                    .Take(5)
                    .ToListAsync();
                
                var thisMonthLeads = partnerLeads.Count(pl => pl.CreatedOn.Month == DateTime.Today.Month);
                var convertedLeads = partnerLeads.Count(pl => pl.Status == "Converted");
                var pendingLeads = partnerLeads.Count(pl => pl.Status == "New");
                
                if (partnerLeads.Any())
                {
                    var leadsInfo = string.Join("\n", partnerLeads.Take(5).Select((pl, i) => 
                        $"{i + 1}. {pl.Lead?.Name} - {pl.Lead?.Contact} - Status: {pl.Status} - Commission: {pl.CommissionAmount:C}"));
                    
                    return new ChatbotResponse
                    {
                        Response = $"**Your Lead Management**\n\n" +
                                  $"This Month's Leads: {thisMonthLeads}\n" +
                                  $"Pending Leads: {pendingLeads}\n" +
                                  $"Converted Leads: {convertedLeads}\n\n" +
                                  $"**Recent Leads:**\n{leadsInfo}\n\n" +
                                  $"I can help you with:\n" +
                                  $"- Track lead status\n" +
                                  $"- Follow up on pending leads\n" +
                                  $"- View commission details\n" +
                                  $"- Get referral updates\n\n" +
                                  $"What would you like to do with your leads?",
                        Intent = "partner_lead_management",
                        Confidence = 0.9
                    };
                }
                else
                {
                    return new ChatbotResponse
                    {
                        Response = "**Your Lead Management**\n\n" +
                                  $"This Month's Leads: {thisMonthLeads}\n" +
                                  $"Pending Leads: {pendingLeads}\n" +
                                  $"Converted Leads: {convertedLeads}\n\n" +
                                  $"You don't have any partner leads yet. Start referring leads to earn commissions!\n\n" +
                                  $"**How to get started:**\n" +
                                  $"- Share your referral code\n" +
                                  $"- Submit leads through the portal\n" +
                                  $"- Track lead status in real-time",
                        Intent = "partner_no_leads",
                        Confidence = 0.8
                    };
                }
            }
            
            if (messageLower.Contains("commission") || messageLower.Contains("earnings") || messageLower.Contains("payout"))
            {
                var totalCommission = await _context.PartnerPayouts
                    .Where(pp => pp.PartnerId == userId)
                    .SumAsync(pp => pp.Amount);
                
                var pendingCommission = await _context.PartnerCommissions
                    .Where(pc => pc.PartnerId == userId && pc.Status == "Pending")
                    .SumAsync(pc => pc.CommissionAmount);
                
                var thisMonthCommission = await _context.PartnerCommissions
                    .Where(pc => pc.PartnerId == userId && pc.CreatedOn.Month == DateTime.Today.Month && pc.CreatedOn.Year == DateTime.Today.Year)
                    .SumAsync(pc => pc.CommissionAmount);
                
                var totalReferrals = await _context.PartnerLeads
                    .CountAsync(pl => pl.PartnerId == userId);
                
                return new ChatbotResponse
                {
                    Response = $"**Commission Dashboard**\n\n" +
                              $"Total Earned: {totalCommission:C}\n" +
                              $"This Month: {thisMonthCommission:C}\n" +
                              $"Pending: {pendingCommission:C}\n" +
                              $"Total Referrals: {totalReferrals}\n\n" +
                              $"**Commission Structure:**\n" +
                              $"- Residential: 2% of deal value\n" +
                              $"- Commercial: 3% of deal value\n\n" +
                              $"**Payment Schedule:**\n" +
                              $"- Processed monthly on 15th\n" +
                              $"- Minimum payout: {1000:C}\n\n" +
                              $"I can help you with:\n" +
                              $"- Detailed commission history\n" +
                              $"- Individual lead commissions\n" +
                              $"- Payout status tracking\n\n" +
                              $"Would you like detailed commission reports?",
                    Intent = "partner_commission",
                    Confidence = 0.9
                };
            }
            
            if (messageLower.Contains("refer") || messageLower.Contains("referral") || messageLower.Contains("submit") || messageLower.Contains("add lead"))
            {
                return new ChatbotResponse
                {
                    Response = "**Referral Program & Lead Submission**\n\n" +
                              $"**How to Submit Leads:**\n\n" +
                              "**Method 1: Partner Portal**\n" +
                              "1. Login to Partner Portal\n" +
                              "2. Click 'Submit New Lead'\n" +
                              "3. Fill lead details (name, phone, email, requirements)\n" +
                              "4. Submit for processing\n\n" +
                              "**Method 2: Direct Contact**\n" +
                              "1. Share your referral code: PARTNER{userId:D4}\n" +
                              "2. Lead mentions your code when contacting us\n" +
                              "3. We automatically credit the lead to you\n\n" +
                              "**Commission Structure:**\n" +
                              $"- Residential: 2% of deal value\n" +
                              $"- Commercial: 3% of deal value\n" +
                              $"- Minimum payout: {1000:C}\n\n" +
                              $"**Tracking:**\n" +
                              $"- Real-time lead status updates\n" +
                              $"- Commission calculation dashboard\n" +
                              $"- Monthly payout reports\n\n" +
                              $"Ready to submit a lead? I can guide you through the process!",
                    Intent = "partner_referral",
                    Confidence = 0.9
                };
            }
            
            if (messageLower.Contains("marketing") || messageLower.Contains("materials") || messageLower.Contains("brochure") || messageLower.Contains("resources"))
            {
                return new ChatbotResponse
                {
                    Response = "**Marketing Resources & Materials**\n\n" +
                              $"**Available Resources:**\n\n" +
                              "**Digital Materials:**\n" +
                              "- Property brochures (PDF)\n" +
                              "- Pricing catalogs (Excel)\n" +
                              "- Social media templates\n" +
                              "- Email templates for lead generation\n" +
                              "- WhatsApp marketing content\n\n" +
                              "**Print Materials:**\n" +
                              "- Referral cards (with your code)\n" +
                              "- Property flyers\n" +
                              "- Business cards\n" +
                              "- Banner designs\n\n" +
                              "**How to Access:**\n" +
                              "1. Login to Partner Portal\n" +
                              "2. Go to 'Marketing Resources'\n" +
                              "3. Download materials by category\n" +
                              "4. Customize with your referral code\n\n" +
                              $"**Your Referral Code:** PARTNER{userId:D4}\n\n" +
                              $"Need help with marketing strategy? I can provide tips!",
                    Intent = "partner_marketing",
                    Confidence = 0.8
                };
            }
            
            if (messageLower.Contains("payout") || messageLower.Contains("payment") || messageLower.Contains("withdraw"))
            {
                var lastPayout = await _context.PartnerPayouts
                    .Where(pp => pp.PartnerId == userId)
                    .OrderByDescending(pp => pp.CreatedOn)
                    .FirstOrDefaultAsync();
                
                var nextPayoutDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 15);
                if (nextPayoutDate <= DateTime.Today)
                {
                    nextPayoutDate = nextPayoutDate.AddMonths(1);
                }
                
                var lastPayoutInfo = lastPayout != null 
                    ? $"Last payout: {lastPayout.CreatedOn:dd MMM yyyy} - {lastPayout.Amount:C}"
                    : "No payouts received yet";
                
                return new ChatbotResponse
                {
                    Response = $"**Payout & Payment Information**\n\n" +
                              $"{lastPayoutInfo}\n\n" +
                              $"**Payment Schedule:**\n" +
                              $"- Payouts processed on 15th of every month\n" +
                              $"- Next payout date: {nextPayoutDate:dd MMM yyyy}\n" +
                              $"- Minimum payout: {1000:C}\n" +
                              $"- Payment method: Bank transfer\n\n" +
                              $"**Payment Process:**\n" +
                              $"1. Commissions calculated monthly\n" +
                              $"2. Payouts processed on 15th\n" +
                              $"3. Bank transfer within 3-5 working days\n" +
                              $"4. Email confirmation with details\n\n" +
                              $"**I can help you with:**\n" +
                              $"- Update bank details\n" +
                              $"- Check payout status\n" +
                              $"- Download payout statements\n\n" +
                              $"Need help with payments?",
                    Intent = "partner_payout",
                    Confidence = 0.9
                };
            }
            
            return new ChatbotResponse
            {
                Response = "**Channel Partner Assistant**\n\n" +
                          $"I can help you with:\n\n" +
                          $"**Lead Management:**\n" +
                          $"- Track your referred leads\n" +
                          $"- Monitor lead status\n" +
                          $"- Commission tracking\n\n" +
                          $"**Earnings:**\n" +
                          $"- View commission details\n" +
                          $"- Payout schedules\n" +
                          $"- Payment history\n\n" +
                          $"**Marketing Support:**\n" +
                          $"- Marketing materials\n" +
                          $"- Referral program details\n" +
                          $"- Property information\n\n" +
                          $"How can I help you grow your business?",
                Intent = "partner_dashboard",
                Confidence = 0.8
            };
        }

        private async Task<string> GetPropertiesByLocationTextAsync(string location)
        {
            var loc = (location ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(loc))
                loc = "your area";

            try
            {
                var query = _context.Properties.AsQueryable();
                query = query.Where(p => p.IsActive);

                var locLower = loc.ToLowerInvariant();
                query = query.Where(p => (p.Location ?? string.Empty).ToLower().Contains(locLower));

                var items = await query
                    .OrderByDescending(p => p.CreatedOn)
                    .Take(5)
                    .Select(p => new { p.PropertyId, p.PropertyName, p.Location, p.Price, p.AreaSqft })
                    .ToListAsync();

                if (!items.Any())
                    return "No active properties found right now for that location.";

                var lines = items.Select((p, idx) =>
                {
                    var price = p.Price.HasValue ? $"Rs{p.Price.Value:N0}" : "Price on request";
                    var area = p.AreaSqft.HasValue ? $"{p.AreaSqft.Value:N0} sqft" : "Area not specified";
                    var locText = string.IsNullOrWhiteSpace(p.Location) ? "" : $" ({p.Location})";
                    return $"{idx + 1}. {p.PropertyName}{locText}\n   {price}  {area}  ID: {p.PropertyId}";
                });

                return string.Join("\n\n", lines);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching properties for location {Location}", loc);
                return "I'm having trouble accessing property info right now. I can connect you to an agent.";
            }
        }

        private async Task AssignRandomExecutiveToChatbotLeadAsync(LeadModel lead)
        {
            var onlineAgents = await _context.ChatAgents
                .Where(a => a.IsOnline && a.IsAvailable)
                .CountAsync();

            if (onlineAgents == 0)
            {
                _logger.LogInformation("No online agents available for lead {LeadId}. Lead will be manually assigned by admin/channel partner.", lead.LeadId);
                return;
            }

            var execId = await AssignAgentAsync();
            if (!execId.HasValue)
                return;

            var who = await GetUserDisplayNameAsync(execId.Value) ?? "executive";
            lead.ExecutiveId = execId.Value;
            lead.ModifiedOn = IndianTime.Now;
            _context.Leads.Update(lead);
            _context.LeadHistory.Add(new LeadHistoryModel
            {
                LeadId = lead.LeadId,
                Activity = $"Lead auto-assigned from chatbot to {who}",
                ExecutiveId = execId.Value,
                ActivityDate = IndianTime.Now
            });
            await _context.SaveChangesAsync();

            var session = await _context.ChatSessions
                .FirstOrDefaultAsync(s => s.GeneratedLeadId == lead.LeadId);
            if (session != null)
            {
                session.AssignedAgentId = execId.Value;
                session.Status = "Assigned";
                await _context.SaveChangesAsync();
            }

            try
            {
                await _notificationService.NotifyLeadAssignedAsync(
                    leadId: lead.LeadId,
                    leadName: lead.Name ?? "New Lead",
                    assignedToUserId: execId.Value,
                    assignedByUserName: "Chatbot"
                );
                
                _logger.LogInformation("Created notification for executive {ExecutiveId} about new chatbot lead {LeadId}", execId.Value, lead.LeadId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending notification for lead {LeadId} assignment", lead.LeadId);
            }
        }

        private async Task<string?> GetUserDisplayNameAsync(int userId)
        {
            var u = await _context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId);
            if (u == null)
                return null;
            if (!string.IsNullOrWhiteSpace(u.Username))
                return u.Username.Trim();
            if (!string.IsNullOrWhiteSpace(u.Email))
                return u.Email.Trim();
            return null;
        }

        private static string? ExtractName(string msg)
        {
            var m = msg.Trim();
            if (string.IsNullOrWhiteSpace(m))
                return null;

            var lower = m.ToLowerInvariant();
            var patterns = new[]
            {
                @"\bmy name is\s+([a-zA-Z][a-zA-Z\s]{1,40})\b",
                @"\bi am\s+([a-zA-Z][a-zA-Z\s]{1,40})\b",
                @"\bthis is\s+([a-zA-Z][a-zA-Z\s]{1,40})\b"
            };

            foreach (var p in patterns)
            {
                var match = Regex.Match(lower, p);
                if (match.Success)
                {
                    var raw = match.Groups[1].Value.Trim();
                    if (IsValidName(raw))
                        return TitleCaseName(raw);
                }
            }

            if (Regex.IsMatch(m, @"^[a-zA-Z][a-zA-Z\s]{1,40}$"))
            {
                if (IsValidName(m))
                    return TitleCaseName(m);
            }

            return null;
        }

        private static bool IsValidName(string name)
        {
            var trimmed = name.Trim();
            
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length < 2 || trimmed.Length > 40)
                return false;

            if (!Regex.IsMatch(trimmed, @"^[a-zA-Z\s]+$"))
                return false;

            if (!char.IsLetter(trimmed[0]))
                return false;

            if (!Regex.IsMatch(trimmed.ToLower(), @"[aeiou]"))
                return false;

            return true;
        }

        private static string TitleCaseName(string name)
        {
            return string.Join(" ", name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));
        }

        private static string? NormalizeBudget(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            var s = input.Trim();
            if (Regex.IsMatch(s, @"\d"))
                return s;
            if (s.Length >= 2 && s.Length <= 60)
                return s;
            return null;
        }

        private static string UpsertBudgetInRequirement(string? requirement, string budget)
        {
            var existing = requirement ?? string.Empty;
            var cleaned = Regex.Replace(existing, @"\bBudget\s*:\s*.*(\r?\n|$)", "", RegexOptions.IgnoreCase).Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
                return $"Budget: {budget}";
            return $"{cleaned}\nBudget: {budget}".Trim();
        }

        private static string FormatAgentTransferUserFacingMessage(string? agentName)
        {
            if (!string.IsNullOrWhiteSpace(agentName))
                return $"{agentName} is your assigned executive. They can see this lead on their CRM dashboard and will contact you shortly.";
            return "A property expert is assigned to you. They will see this lead on their dashboard and contact you shortly.";
        }

        private readonly struct VisitorHelpResult
        {
            public string Text { get; init; }
            public bool InvitePropertyFlow { get; init; }
        }

        private static bool IsVisitorInformationalQuestionOnly(string? message, string lower)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;
            if (message.Contains('?', StringComparison.Ordinal))
                return true;
            if (Regex.IsMatch(lower, @"^(how|what|where|why|when|who|which|tell\s+me|explain|describe|can\s+you|could\s+you|is\s+there|are\s+there)\b"))
                return true;
            return false;
        }

        private static VisitorHelpResult? TryGetVisitorCrmHelp(string lower)
        {
            if (Regex.IsMatch(lower, @"\b(how|where|what)\s+.*\b(contact|reach|call|get|speak|talk)\b.*\b(agent|sales|executive|advisor|representative|your\s+team|someone)"))
            {
                return new VisitorHelpResult
                {
                    Text = "To speak with a property expert: use Talk to Agent in this chat when you're ready we'll ask for your name, phone, and preferred area, then assign someone from our team.",
                    InvitePropertyFlow = false
                };
            }

            return null;
        }

        private async Task<ChatbotResponse> ProcessPublicUserQueryAsync(string message, ChatSessionModel session)
        {
            var messageLower = message.ToLower();
            
            if (messageLower.Contains("property") && (messageLower.Contains("bangalore") || messageLower.Contains("hyderabad") || messageLower.Contains("mumbai") || messageLower.Contains("chennai") || messageLower.Contains("delhi")))
            {
                string location = null;
                if (messageLower.Contains("mumbai")) location = "Mumbai";
                else if (messageLower.Contains("bangalore")) location = "Bangalore";
                else if (messageLower.Contains("hyderabad")) location = "Hyderabad";
                else if (messageLower.Contains("chennai")) location = "Chennai";
                else if (messageLower.Contains("delhi")) location = "Delhi";
                
                var locationProperties = await _context.Properties
                    .Where(p => p.Location != null && p.Location.ToLower().Contains(location.ToLower()) && p.IsActive)
                    .ToListAsync();
                
                if (locationProperties.Any())
                {
                    var propertyInfo = string.Join("\n", locationProperties.Select((p, i) => 
                        $"{i + 1}. {p.PropertyName} ({p.Location})\n   Rs{p.Price:N0}  {p.AreaSqft} sqft  ID: {p.PropertyId}"));
                    
                    return new ChatbotResponse
                    {
                        Response = $"Thanks! Here are properties in {location}:\n\n{propertyInfo}\n\n" +
                                  $"If you have any specific questions about a property, I can connect you to an agent.",
                        Intent = "public_property_location",
                        Confidence = 0.9
                    };
                }
                else
                {
                    return new ChatbotResponse
                    {
                        Response = $"I don't have any available properties in {location} right now. Would you like me to check other locations?",
                        Intent = "public_property_location_empty",
                        Confidence = 0.8
                    };
                }
            }
            if (messageLower.Contains("property") || messageLower.Contains("real estate") || messageLower.Contains("flat") || messageLower.Contains("house"))
            {
                return new ChatbotResponse
                {
                    Response = "I can help you find properties! Please tell me:\n\n" +
                              "- Which location are you interested in?\n" +
                              "- What's your budget range?\n" +
                              "- What type of property (flat, house, villa)?\n" +
                              "- Any specific requirements?\n\n" +
                              "This will help me find the perfect property for you!",
                    Intent = "public_property_help",
                    Confidence = 0.9
                };
            }

            if (!string.IsNullOrWhiteSpace(message) && (message.Split(' ').Length >= 3 || message.Contains("?") || message.Contains("what") || message.Contains("how")))
            {
                var aiResponse = await GenerateAIResponseAsync(message, staffContext: false, staffRole: null, session: session);
                return new ChatbotResponse
                {
                    Response = aiResponse + "\n\nI hope that helps! If you need more personalized assistance, I can also connect you to our live agents. Would you like to speak to an agent?",
                    Intent = "smart_fallback",
                    Confidence = 0.7
                };
            }
            
            return await ProcessLeadCollectionAsync(message, session);
        }

        public async Task AddImageMessageAsync(string sessionId, string imageData, int? userId = null)
        {
            try
            {
                var session = await GetOrCreateSessionAsync(sessionId, userId);
                
                var log = new ChatLogModel
                {
                    SessionId = sessionId,
                    UserId = userId,
                    UserMessage = "[Image uploaded]",
                    AiResponse = "Image received. Analyzing...",
                    Intent = "image_upload",
                    Confidence = "1.0",
                    CreatedOn = DateTime.Now
                };

                await LogMessageAsync(log);
                _logger.LogInformation("Image message added to session {SessionId}", sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding image message to session {SessionId}", sessionId);
                throw;
            }
        }

        public async Task<string> AnalyzeImageAsync(string imageData, string message, string sessionId, int? userId = null)
        {
            try
            {
                var analysisPrompt = $@"
Analyze this image and the user's message: '{message}'

The user has uploaded an image in the chatbot. Please provide:
1. A description of what you see in the image
2. How this relates to their message or potential issue
3. Helpful suggestions or solutions based on the image content
4. If it's a technical issue, provide step-by-step guidance

Context: This is a Real Estate CRM system, so the image might be related to:
- Property listings or photos
- CRM interface screenshots
- Error messages or technical issues
- Property documents
- Dashboard or report issues

Please provide a helpful, detailed response that addresses their specific needs.";

                var simulatedResponse = await GenerateSimulatedImageAnalysis(imageData, message);

                var session = await GetOrCreateSessionAsync(sessionId, userId);
                var log = new ChatLogModel
                {
                    SessionId = sessionId,
                    UserId = userId,
                    UserMessage = "[Image uploaded] " + message,
                    AiResponse = simulatedResponse,
                    Intent = "image_analysis",
                    Confidence = "0.9",
                    CreatedOn = DateTime.Now
                };

                await LogMessageAsync(log);

                return simulatedResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing image for session {SessionId}", sessionId);
                
                var errorResponse = "I apologize, but I encountered an error while analyzing your image. However, I can still help you! Could you please describe what's shown in the image or what issue you're facing, and I'll provide assistance based on your description?";
                
                var session = await GetOrCreateSessionAsync(sessionId, userId);
                var log = new ChatLogModel
                {
                    SessionId = sessionId,
                    UserId = userId,
                    UserMessage = "[Image uploaded] " + message,
                    AiResponse = errorResponse,
                    Intent = "image_analysis_error",
                    Confidence = "0.5",
                    CreatedOn = DateTime.Now
                };

                await LogMessageAsync(log);
                
                return errorResponse;
            }
        }

        private async Task<string> GenerateSimulatedImageAnalysis(string imageData, string message)
        {
            var imageAnalysis = AnalyzeImageContent(imageData);
            return GenerateContextualResponse(imageAnalysis, message);
        }

        private ImageAnalysisResult AnalyzeImageContent(string imageData)
        {
            var analysis = new ImageAnalysisResult();
            
            if (imageData.Length > 100000)
            {
                analysis.ImageType = "screenshot";
                analysis.LikelyContent = "CRM Interface";
                analysis.HasText = true;
                analysis.HasUIElements = true;
            }
            else if (imageData.Contains("iVBORw0KGgo"))
            {
                analysis.ImageType = "png";
                analysis.LikelyContent = "Property Image or Document";
                analysis.HasText = false;
            }
            else if (imageData.Contains("/9j/"))
            {
                analysis.ImageType = "jpeg";
                analysis.LikelyContent = "Property Photo or Screenshot";
                analysis.HasText = imageData.Length > 50000;
            }
            
            if (imageData.Contains("error") || imageData.Contains("Error"))
            {
                analysis.ContainsError = true;
                analysis.IssueType = "Error Message";
            }
            
            if (imageData.Contains("dashboard") || imageData.Contains("Dashboard"))
            {
                analysis.IsDashboard = true;
                analysis.IssueType = "Dashboard Issue";
            }
            
            if (imageData.Contains("property") || imageData.Contains("Property"))
            {
                analysis.IsPropertyRelated = true;
                analysis.IssueType = "Property Information";
            }
            
            return analysis;
        }

        private string GenerateContextualResponse(ImageAnalysisResult analysis, string userMessage)
        {
            var response = new StringBuilder();
            
            response.AppendLine("I can see the image you've uploaded. ");
            
            if (analysis.ContainsError)
            {
                response.AppendLine("This appears to show an error message in our CRM system. ");
                response.AppendLine("Here's how to resolve this issue:");
                response.AppendLine("1. Take a screenshot of the full error message");
                response.AppendLine("2. Refresh your browser and try again");
                response.AppendLine("3. Clear your browser cache and cookies");
                response.AppendLine("4. Check if you have the correct permissions");
                response.AppendLine("5. If the error persists, note the exact error message and contact support");
                
                if (!string.IsNullOrEmpty(userMessage))
                {
                    response.AppendLine($"\nBased on your message: '{userMessage}', I can provide more specific assistance for this error.");
                }
            }
            else if (analysis.IsDashboard)
            {
                response.AppendLine("This appears to be a CRM dashboard screenshot. ");
                response.AppendLine("I can help you with:");
                response.AppendLine("° Understanding dashboard metrics and KPIs");
                response.AppendLine("° Navigating different dashboard sections");
                response.AppendLine("° Customizing your dashboard view");
                response.AppendLine("° Exporting dashboard reports");
                
                if (userMessage.ToLower().Contains("help") || userMessage.ToLower().Contains("issue"))
                {
                    response.AppendLine("\nWhat specific aspect of the dashboard would you like help with?");
                }
            }
            else if (analysis.IsPropertyRelated)
            {
                response.AppendLine("This appears to be related to property information. ");
                response.AppendLine("I can assist you with:");
                response.AppendLine("° Property details and specifications");
                response.AppendLine("° Pricing and availability information");
                response.AppendLine("° Property photos and virtual tours");
                response.AppendLine("° Location and neighborhood details");
                response.AppendLine("° Scheduling property visits");
                
                if (userMessage.ToLower().Contains("listing") || userMessage.ToLower().Contains("search"))
                {
                    response.AppendLine("\nWould you like me to help you find similar properties or provide more details about this listing?");
                }
            }
            else if (analysis.ImageType == "screenshot")
            {
                response.AppendLine("This looks like a screenshot of our CRM interface. ");
                response.AppendLine("I can help you with:");
                response.AppendLine("° Understanding any features you're seeing");
                response.AppendLine("° Navigating the CRM system");
                response.AppendLine("° Troubleshooting interface issues");
                response.AppendLine("° Finding specific information or tools");
                
                if (!string.IsNullOrEmpty(userMessage))
                {
                    response.AppendLine($"\nRegarding your question about '{userMessage}', I can provide specific guidance for what you're seeing in the screenshot.");
                }
            }
            else
            {
                response.AppendLine("I can see you've uploaded an image. ");
                response.AppendLine("Based on the image content, I can help you with:");
                response.AppendLine("° Understanding what's shown in the image");
                response.AppendLine("° Resolving any issues you're experiencing");
                response.AppendLine("° Finding related information or resources");
                response.AppendLine("° Connecting you with the right support if needed");
            }
            
            if (!string.IsNullOrEmpty(userMessage))
            {
                var lowerMessage = userMessage.ToLower();
                if (lowerMessage.Contains("help") || lowerMessage.Contains("assist"))
                {
                    response.AppendLine("\nI'm here to help! Please describe what specific assistance you need with this image.");
                }
                else if (lowerMessage.Contains("problem") || lowerMessage.Contains("issue"))
                {
                    response.AppendLine("\nI understand you're facing an issue. Let me help you resolve this step by step.");
                }
                else if (lowerMessage.Contains("question") || lowerMessage.Contains("what"))
                {
                    response.AppendLine($"\nRegarding your question: '{userMessage}', here's what I can tell you from the image...");
                }
            }
            
            response.AppendLine("\nIf you need more specific help, please describe exactly what you're trying to accomplish or what challenge you're facing.");
            
            return response.ToString();
        }

        public async Task<RealTimeChatMessage> CreateRealTimeMessageAsync(string sessionId, string conversationId, string message, string messageType = "User", int? senderId = null, string? senderName = null, int? parentMessageId = null)
        {
            try
            {
                var chatMessage = new RealTimeChatMessage
                {
                    SessionId = sessionId,
                    ConversationId = conversationId,
                    MessageType = messageType,
                    SenderId = senderId,
                    SenderName = senderName ?? "Anonymous",
                    MessageText = message,
                    SentAt = DateTime.UtcNow,
                    ParentMessageId = parentMessageId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.RealTimeChatMessages.Add(chatMessage);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Created real-time message {MessageId} in conversation {ConversationId}", chatMessage.Id, conversationId);
                return chatMessage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating real-time message in conversation {ConversationId}", conversationId);
                throw;
            }
        }

        public async Task<int?> AutoAssignAgentAsync(string conversationId, string priority = "Normal")
        {
            try
            {
                var availableAgents = await _context.AgentChatStatus
                    .Where(a => a.IsOnline && 
                               a.CurrentStatus != "Offline" && 
                               a.CurrentStatus != "Busy" &&
                               a.CurrentChatCount < a.MaxConcurrentChats)
                    .OrderBy(a => a.CurrentChatCount)
                    .ThenBy(a => a.AverageResponseTime ?? int.MaxValue)
                    .ToListAsync();

                if (!availableAgents.Any())
                {
                    _logger.LogWarning("No available agents found for conversation {ConversationId}", conversationId);
                    return null;
                }

                var selectedAgent = availableAgents.FirstOrDefault();
                
                if (selectedAgent == null)
                {
                    _logger.LogWarning("No available agents found for conversation {ConversationId}", conversationId);
                    return null;
                }
                
                await CreateConversationAssignmentAsync(conversationId, selectedAgent.AgentId);
                
                selectedAgent.CurrentChatCount += 1;
                selectedAgent.LastActivityAt = DateTime.UtcNow;
                selectedAgent.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Auto-assigned agent {AgentId} to conversation {ConversationId}", selectedAgent.AgentId, conversationId);
                return selectedAgent.AgentId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error auto-assigning agent to conversation {ConversationId}", conversationId);
                return null;
            }
        }

        public async Task<bool> AssignAgentToConversationAsync(string conversationId, int agentId, int? assignedByAgentId = null)
        {
            try
            {
                var success = await CreateConversationAssignmentAsync(conversationId, agentId, assignedByAgentId);
                
                if (success)
                {
                    var agentStatus = await _context.AgentChatStatus
                        .FirstOrDefaultAsync(a => a.AgentId == agentId);
                    
                    if (agentStatus != null)
                    {
                        agentStatus.CurrentChatCount += 1;
                        agentStatus.LastActivityAt = DateTime.UtcNow;
                        agentStatus.UpdatedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync();
                    }

                    // Firebase notification service not available in this CRM
                    // await _firebaseNotificationService.SendAgentAssignmentNotificationAsync(agentId, conversationId, 
                    //     assignedByAgentId?.ToString() ?? "System");

                    _logger.LogInformation("Manually assigned agent {AgentId} to conversation {ConversationId}", agentId, conversationId);
                }
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error manually assigning agent {AgentId} to conversation {ConversationId}", agentId, conversationId);
                return false;
            }
        }

        public async Task<List<RealTimeChatMessage>> GetConversationMessagesAsync(string conversationId)
        {
            try
            {
                return await _context.RealTimeChatMessages
                    
                    
                    .Where(m => m.ConversationId == conversationId)
                    .OrderBy(m => m.SentAt)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting messages for conversation {ConversationId}", conversationId);
                return new List<RealTimeChatMessage>();
            }
        }

        public async Task<List<AgentChatStatus>> GetOnlineAgentsAsync()
        {
            try
            {
                return await _context.AgentChatStatus
                    
                    .Where(a => a.IsOnline)
                    .OrderBy(a => a.CurrentChatCount)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting online agents");
                return new List<AgentChatStatus>();
            }
        }

        public async Task<bool> UpdateAgentChatStatusAsync(int agentId, bool isOnline, string status)
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
                _logger.LogInformation("Updated agent {AgentId} status to {Status} (Online: {IsOnline})", agentId, status, isOnline);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating agent {AgentId} status", agentId);
                return false;
            }
        }

        public async Task NotifyAgentsAboutNewMessageAsync(string conversationId, string message, int messageId)
        {
            try
            {
                var onlineAgents = await _context.AgentChatStatus
                    .Where(a => a.IsOnline && a.CurrentStatus != "Offline")
                    .ToListAsync();

                foreach (var agent in onlineAgents)
                {
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

                    // Firebase notification service not available in this CRM
                    // await _firebaseNotificationService.SendChatNotificationAsync(agent.AgentId, conversationId, message);
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("Notified {Count} agents about new message in conversation {ConversationId}", onlineAgents.Count, conversationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error notifying agents about new message in conversation {ConversationId}", conversationId);
            }
        }

        public async Task<bool> CreateConversationAssignmentAsync(string conversationId, int agentId, int? assignedByAgentId = null)
        {
            try
            {
                var assignment = await _context.ChatConversationAssignments
                    .FirstOrDefaultAsync(a => a.ConversationId == conversationId);

                if (assignment == null)
                {
                    assignment = new ChatConversationAssignment
                    {
                        ConversationId = conversationId,
                        AssignedAgentId = agentId,
                        AssignedByAgentId = assignedByAgentId,
                        AssignedAt = DateTime.UtcNow,
                        Status = "Assigned",
                        Priority = "Normal"
                    };
                    _context.ChatConversationAssignments.Add(assignment);
                }
                else
                {
                    assignment.AssignedAgentId = agentId;
                    assignment.AssignedByAgentId = assignedByAgentId;
                    assignment.AssignedAt = DateTime.UtcNow;
                    assignment.Status = "Assigned";
                    assignment.LastAgentActivityAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating conversation assignment for {ConversationId}", conversationId);
                return false;
            }
        }

        private class ImageAnalysisResult
        {
            public string ImageType { get; set; } = string.Empty;
            public string LikelyContent { get; set; } = string.Empty;
            public bool HasText { get; set; }
            public bool HasUIElements { get; set; }
            public bool ContainsError { get; set; }
            public bool IsDashboard { get; set; }
            public bool IsPropertyRelated { get; set; }
            public string IssueType { get; set; } = string.Empty;
        }
    }
}
