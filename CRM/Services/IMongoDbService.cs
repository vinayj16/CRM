using CRM.Models.MongoDb;

namespace CRM.Services
{
    public interface IMongoDbService
    {
        /// <summary>
        /// Checks MongoDB connection health
        /// </summary>
        Task<bool> PingAsync();

        // ===== Chat Messages =====

        /// <summary>
        /// Saves a chat message to MongoDB
        /// </summary>
        Task SaveChatMessageAsync(ChatMessageDocument message);

        /// <summary>
        /// Gets all messages for a conversation, ordered by sent time
        /// </summary>
        Task<List<ChatMessageDocument>> GetConversationMessagesAsync(string conversationId);

        /// <summary>
        /// Gets all messages for a session
        /// </summary>
        Task<List<ChatMessageDocument>> GetSessionMessagesAsync(string sessionId);

        /// <summary>
        /// Gets recent messages for a conversation with pagination
        /// </summary>
        Task<List<ChatMessageDocument>> GetRecentMessagesAsync(string conversationId, int limit = 50);

        /// <summary>
        /// Marks messages as read for a conversation
        /// </summary>
        Task MarkMessagesAsReadAsync(string conversationId, int? userId = null);

        // ===== Chat Sessions =====

        /// <summary>
        /// Saves or updates a chat session in MongoDB
        /// </summary>
        Task SaveChatSessionAsync(ChatSessionDocument session);

        /// <summary>
        /// Gets a chat session by session GUID
        /// </summary>
        Task<ChatSessionDocument?> GetChatSessionByGuidAsync(string sessionGuid);

        /// <summary>
        /// Gets a chat session by ID
        /// </summary>
        Task<ChatSessionDocument?> GetChatSessionByIdAsync(string id);

        /// <summary>
        /// Gets active chat sessions for a tenant
        /// </summary>
        Task<List<ChatSessionDocument>> GetActiveSessionsAsync(int tenantId, int limit = 20);

        /// <summary>
        /// Gets recent chat sessions for a tenant with pagination
        /// </summary>
        Task<(List<ChatSessionDocument> Sessions, long Total)> GetSessionsPagedAsync(int tenantId, int page = 1, int pageSize = 20);

        // ===== Knowledge Base =====

        /// <summary>
        /// Saves a knowledge base article
        /// </summary>
        Task SaveKnowledgeArticleAsync(KnowledgeDocument article);

        /// <summary>
        /// Searches knowledge base by question/keywords
        /// </summary>
        Task<List<KnowledgeDocument>> SearchKnowledgeBaseAsync(string query, int tenantId, int limit = 5);

        /// <summary>
        /// Gets all active knowledge articles for a tenant
        /// </summary>
        Task<List<KnowledgeDocument>> GetActiveKnowledgeAsync(int tenantId);

        /// <summary>
        /// Deletes a knowledge article by ID
        /// </summary>
        Task DeleteKnowledgeArticleAsync(string id);
    }
}
