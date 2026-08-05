using CRM.Models.MongoDb;
using MongoDB.Driver;
using Microsoft.Extensions.Logging;

namespace CRM.Services
{
    public class MongoDbService : IMongoDbService
    {
        private readonly MongoDbContext _context;
        private readonly ILogger<MongoDbService> _logger;

        public MongoDbService(MongoDbContext context, ILogger<MongoDbService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<bool> PingAsync()
        {
            try
            {
                return await _context.PingAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MongoDB ping failed");
                return false;
            }
        }

        // ===== Chat Messages =====

        public async Task SaveChatMessageAsync(ChatMessageDocument message)
        {
            try
            {
                if (string.IsNullOrEmpty(message.Id))
                {
                    message.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
                }
                await _context.ChatMessages.InsertOneAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving chat message to MongoDB for conversation {ConversationId}", message.ConversationId);
                throw;
            }
        }

        public async Task<List<ChatMessageDocument>> GetConversationMessagesAsync(string conversationId)
        {
            try
            {
                var filter = Builders<ChatMessageDocument>.Filter.Eq(m => m.ConversationId, conversationId);
                var sort = Builders<ChatMessageDocument>.Sort.Ascending(m => m.SentAt);
                return await _context.ChatMessages.Find(filter).Sort(sort).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching conversation messages from MongoDB for {ConversationId}", conversationId);
                return new List<ChatMessageDocument>();
            }
        }

        public async Task<List<ChatMessageDocument>> GetSessionMessagesAsync(string sessionId)
        {
            try
            {
                var filter = Builders<ChatMessageDocument>.Filter.Eq(m => m.SessionId, sessionId);
                var sort = Builders<ChatMessageDocument>.Sort.Ascending(m => m.SentAt);
                return await _context.ChatMessages.Find(filter).Sort(sort).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching session messages from MongoDB for {SessionId}", sessionId);
                return new List<ChatMessageDocument>();
            }
        }

        public async Task<List<ChatMessageDocument>> GetRecentMessagesAsync(string conversationId, int limit = 50)
        {
            try
            {
                var filter = Builders<ChatMessageDocument>.Filter.Eq(m => m.ConversationId, conversationId);
                var sort = Builders<ChatMessageDocument>.Sort.Descending(m => m.SentAt);
                return await _context.ChatMessages.Find(filter).Sort(sort).Limit(limit).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching recent messages from MongoDB for {ConversationId}", conversationId);
                return new List<ChatMessageDocument>();
            }
        }

        public async Task MarkMessagesAsReadAsync(string conversationId, int? userId = null)
        {
            try
            {
                var filter = Builders<ChatMessageDocument>.Filter.And(
                    Builders<ChatMessageDocument>.Filter.Eq(m => m.ConversationId, conversationId),
                    Builders<ChatMessageDocument>.Filter.Eq(m => m.IsRead, false)
                );

                var update = Builders<ChatMessageDocument>.Update
                    .Set(m => m.IsRead, true)
                    .Set(m => m.ReadAt, DateTime.UtcNow);

                await _context.ChatMessages.UpdateManyAsync(filter, update);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking messages as read in MongoDB for {ConversationId}", conversationId);
            }
        }

        // ===== Chat Sessions =====

        public async Task SaveChatSessionAsync(ChatSessionDocument session)
        {
            try
            {
                // Check if session exists by GUID
                var existingFilter = Builders<ChatSessionDocument>.Filter.Eq(s => s.SessionGuid, session.SessionGuid);
                var existing = await _context.ChatSessions.Find(existingFilter).FirstOrDefaultAsync();

                if (existing != null)
                {
                    // Update existing session
                    session.Id = existing.Id;
                    var replaceFilter = Builders<ChatSessionDocument>.Filter.Eq(s => s.Id, existing.Id);
                    await _context.ChatSessions.ReplaceOneAsync(replaceFilter, session);
                }
                else
                {
                    // Insert new session
                    if (string.IsNullOrEmpty(session.Id))
                    {
                        session.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
                    }
                    await _context.ChatSessions.InsertOneAsync(session);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving chat session to MongoDB for {SessionGuid}", session.SessionGuid);
                throw;
            }
        }

        public async Task<ChatSessionDocument?> GetChatSessionByGuidAsync(string sessionGuid)
        {
            try
            {
                var filter = Builders<ChatSessionDocument>.Filter.Eq(s => s.SessionGuid, sessionGuid);
                return await _context.ChatSessions.Find(filter).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching chat session from MongoDB by GUID {SessionGuid}", sessionGuid);
                return null;
            }
        }

        public async Task<ChatSessionDocument?> GetChatSessionByIdAsync(string id)
        {
            try
            {
                var filter = Builders<ChatSessionDocument>.Filter.Eq(s => s.Id, id);
                return await _context.ChatSessions.Find(filter).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching chat session from MongoDB by ID {Id}", id);
                return null;
            }
        }

        public async Task<List<ChatSessionDocument>> GetActiveSessionsAsync(int tenantId, int limit = 20)
        {
            try
            {
                var filter = Builders<ChatSessionDocument>.Filter.And(
                    Builders<ChatSessionDocument>.Filter.Eq(s => s.TenantId, tenantId),
                    Builders<ChatSessionDocument>.Filter.Eq(s => s.Status, "Active")
                );
                var sort = Builders<ChatSessionDocument>.Sort.Descending(s => s.LastActivityAt);
                return await _context.ChatSessions.Find(filter).Sort(sort).Limit(limit).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching active sessions from MongoDB for tenant {TenantId}", tenantId);
                return new List<ChatSessionDocument>();
            }
        }

        public async Task<(List<ChatSessionDocument> Sessions, long Total)> GetSessionsPagedAsync(int tenantId, int page = 1, int pageSize = 20)
        {
            try
            {
                var filter = Builders<ChatSessionDocument>.Filter.Eq(s => s.TenantId, tenantId);
                var sort = Builders<ChatSessionDocument>.Sort.Descending(s => s.StartedAt);

                var total = await _context.ChatSessions.CountDocumentsAsync(filter);
                var sessions = await _context.ChatSessions
                    .Find(filter)
                    .Sort(sort)
                    .Skip((page - 1) * pageSize)
                    .Limit(pageSize)
                    .ToListAsync();

                return (sessions, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching paged sessions from MongoDB for tenant {TenantId}", tenantId);
                return (new List<ChatSessionDocument>(), 0);
            }
        }

        // ===== Knowledge Base =====

        public async Task SaveKnowledgeArticleAsync(KnowledgeDocument article)
        {
            try
            {
                if (string.IsNullOrEmpty(article.Id))
                {
                    article.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();
                }

                var existingFilter = Builders<KnowledgeDocument>.Filter.Eq(k => k.Id, article.Id);
                var existing = await _context.KnowledgeBase.Find(existingFilter).FirstOrDefaultAsync();

                if (existing != null)
                {
                    await _context.KnowledgeBase.ReplaceOneAsync(existingFilter, article);
                }
                else
                {
                    await _context.KnowledgeBase.InsertOneAsync(article);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving knowledge article to MongoDB");
                throw;
            }
        }

        public async Task<List<KnowledgeDocument>> SearchKnowledgeBaseAsync(string query, int tenantId, int limit = 5)
        {
            try
            {
                // Use MongoDB text index for efficient full-text search
                // Falls back to regex search if text search fails (e.g., no text index)
                try
                {
                    var filter = Builders<KnowledgeDocument>.Filter.And(
                        Builders<KnowledgeDocument>.Filter.Eq(k => k.IsActive, true),
                        Builders<KnowledgeDocument>.Filter.Eq(k => k.TenantId, tenantId),
                        Builders<KnowledgeDocument>.Filter.Text(query)
                    );

                    var sort = Builders<KnowledgeDocument>.Sort.Descending(k => k.Priority);
                    return await _context.KnowledgeBase.Find(filter).Sort(sort).Limit(limit).ToListAsync();
                }
                catch (MongoDB.Driver.MongoQueryException)
                {
                    // Fallback to regex search if text index is not available
                    var fallbackFilter = Builders<KnowledgeDocument>.Filter.And(
                        Builders<KnowledgeDocument>.Filter.Eq(k => k.IsActive, true),
                        Builders<KnowledgeDocument>.Filter.Eq(k => k.TenantId, tenantId),
                        Builders<KnowledgeDocument>.Filter.Or(
                            Builders<KnowledgeDocument>.Filter.Regex(k => k.Question, new MongoDB.Bson.BsonRegularExpression(query, "i")),
                            Builders<KnowledgeDocument>.Filter.Regex(k => k.Keywords, new MongoDB.Bson.BsonRegularExpression(query, "i")),
                            Builders<KnowledgeDocument>.Filter.Regex(k => k.Answer, new MongoDB.Bson.BsonRegularExpression(query, "i"))
                        )
                    );

                    var sort = Builders<KnowledgeDocument>.Sort.Descending(k => k.Priority);
                    return await _context.KnowledgeBase.Find(fallbackFilter).Sort(sort).Limit(limit).ToListAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching knowledge base in MongoDB");
                return new List<KnowledgeDocument>();
            }
        }

        public async Task<List<KnowledgeDocument>> GetActiveKnowledgeAsync(int tenantId)
        {
            try
            {
                var filter = Builders<KnowledgeDocument>.Filter.And(
                    Builders<KnowledgeDocument>.Filter.Eq(k => k.IsActive, true),
                    Builders<KnowledgeDocument>.Filter.Eq(k => k.TenantId, tenantId)
                );
                var sort = Builders<KnowledgeDocument>.Sort.Descending(k => k.Priority);
                return await _context.KnowledgeBase.Find(filter).Sort(sort).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching active knowledge from MongoDB for tenant {TenantId}", tenantId);
                return new List<KnowledgeDocument>();
            }
        }

        public async Task DeleteKnowledgeArticleAsync(string id)
        {
            try
            {
                var filter = Builders<KnowledgeDocument>.Filter.Eq(k => k.Id, id);
                await _context.KnowledgeBase.DeleteOneAsync(filter);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting knowledge article from MongoDB {Id}", id);
                throw;
            }
        }
    }
}
