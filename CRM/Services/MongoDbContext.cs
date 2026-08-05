using CRM.Helpers;
using CRM.Models.MongoDb;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;
using System.Reflection;

namespace CRM.Services
{
    public class MongoDbContext
    {
        private static readonly object _serializerLock = new();
        private static bool _serializerRegistered;

        private readonly IMongoDatabase _database;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MongoDbContext> _logger;
        private bool _indexesCreated;

        public MongoDbContext(IConfiguration configuration, ILogger<MongoDbContext> logger)
        {
            _configuration = configuration;
            _logger = logger;


            // Register FlexibleStringSerializer globally for all string properties
            // This prevents "Cannot deserialize a 'String' from BsonType 'Decimal128'" errors
            // when MongoDB documents have numeric values stored in string-modeled fields (Budget, Sqft, etc.)
            RegisterFlexibleStringSerializerOnce();

            // Register global MongoDB conventions to ignore extra elements (like _id)
            var pack = new ConventionPack
            {
                new IgnoreExtraElementsConvention(true),
                new FlexibleStringSerializerConvention()
            };
            ConventionRegistry.Register("crm-ignore-extra", pack, _ => true);

            // Check environment variable first (for production), fall back to appsettings.json
            var connectionString = Environment.GetEnvironmentVariable("MONGODB_URI")
                ?? _configuration["MongoDb:ConnectionString"]
                ?? throw new InvalidOperationException("MongoDB connection string is not configured. Set MONGODB_URI env var or add 'MongoDb:ConnectionString' to appsettings.json.");

            var databaseName = Environment.GetEnvironmentVariable("MONGODB_DB")
                ?? _configuration["MongoDb:DatabaseName"]
                ?? "crm";

            var client = new MongoClient(connectionString);
            _database = client.GetDatabase(databaseName);
        }


        /// <summary>
        /// Registers the FlexibleStringSerializer globally for all string properties.
        /// This is done once per app lifetime to handle mixed-type fields like
        /// Budget (stored as Decimal128 in some documents, string in others).
        /// </summary>
        private static void RegisterFlexibleStringSerializerOnce()
        {
            if (_serializerRegistered) return;
            lock (_serializerLock)
            {
                if (_serializerRegistered) return;
                try
                {
                    BsonSerializer.TryRegisterSerializer(typeof(string), new FlexibleStringSerializer());
                    _serializerRegistered = true;
                }
                catch
                {
                    // If registration fails (e.g., already registered), ignore.
                    // Note: MongoDB.Driver does not allow overriding the built-in string
                    // serializer, so this is expected to fail. The FlexibleStringSerializerConvention
                    // (registered below) is the reliable mechanism that handles mixed-type fields.
                }
            }
        }

        /// <summary>
        /// MongoDB convention that applies <see cref="FlexibleStringSerializer"/> to every
        /// string member of every mapped class. This is the reliable fix for the
        /// "Cannot deserialize a 'String' from BsonType 'Decimal128'" error because it runs
        /// during class-map auto-creation (whereas a global string serializer registration is
        /// rejected by the driver for built-in types).
        /// </summary>
        private sealed class FlexibleStringSerializerConvention : ConventionBase, IMemberMapConvention
        {
            public void Apply(BsonMemberMap memberMap)
            {
                if (memberMap.MemberType == typeof(string))
                {
                    memberMap.SetSerializer(new FlexibleStringSerializer());
                }
            }
        }

        /// <summary>
        /// Gets the MongoDB database instance.
        /// </summary>
        public IMongoDatabase Database => _database;

        /// <summary>
        /// Chat messages collection - used for storing chatbot and real-time chat messages
        /// </summary>
        public IMongoCollection<ChatMessageDocument> ChatMessages =>
            _database.GetCollection<ChatMessageDocument>("chat_messages");

        /// <summary>
        /// Chat sessions collection - used for storing chatbot sessions
        /// </summary>
        public IMongoCollection<ChatSessionDocument> ChatSessions =>
            _database.GetCollection<ChatSessionDocument>("chat_sessions");

        /// <summary>
        /// Knowledge base collection - used for chatbot FAQ/knowledge articles
        /// </summary>
        public IMongoCollection<KnowledgeDocument> KnowledgeBase =>
            _database.GetCollection<KnowledgeDocument>("knowledge_base");

        /// <summary>
        /// Seed data collections using MongoDB document types
        /// </summary>
        public IMongoCollection<RolePermissionDocument> RolePermissions =>
            _database.GetCollection<RolePermissionDocument>("role_permissions");
        public IMongoCollection<SaasPlanDocument> SaasPlans =>
            _database.GetCollection<SaasPlanDocument>("saas_plans");
        public IMongoCollection<SuperAdminDocument> SuperAdmins =>
            _database.GetCollection<SuperAdminDocument>("super_admins");
        public IMongoCollection<TenantDocument> Tenants =>
            _database.GetCollection<TenantDocument>("tenants");
        public IMongoCollection<UserDocument> Users =>
            _database.GetCollection<UserDocument>("users");

        /// <summary>
        /// Generic method to get any collection by name
        /// </summary>
        public IMongoCollection<T> GetCollection<T>(string name)
        {
            return _database.GetCollection<T>(name);
        }

        /// <summary>
        /// Checks the MongoDB connection health
        /// </summary>
        public async Task<bool> PingAsync()
        {
            try
            {
                await _database.RunCommandAsync<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument("ping", 1));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MongoDB ping failed - connection may be unavailable");
                return false;
            }
        }

        /// <summary>
        /// Ensures indexes are created for all collections for query performance.
        /// Safe to call multiple times - MongoDB ignores duplicate index creation.
        /// </summary>
        public async Task EnsureIndexesAsync()
        {
            if (_indexesCreated)
                return;

            try
            {
                // Chat Messages indexes
                var msgIndexBuilder = Builders<ChatMessageDocument>.IndexKeys;
                var msgIndexes = new List<CreateIndexModel<ChatMessageDocument>>
                {
                    new(msgIndexBuilder.Ascending(m => m.TenantId).Ascending(m => m.ConversationId).Ascending(m => m.SentAt)),
                    new(msgIndexBuilder.Ascending(m => m.TenantId).Ascending(m => m.SessionId).Ascending(m => m.SentAt)),
                    new(msgIndexBuilder.Ascending(m => m.ConversationId).Ascending(m => m.SentAt)),
                    new(msgIndexBuilder.Ascending(m => m.IsRead))
                };
                await ChatMessages.Indexes.CreateManyAsync(msgIndexes);

                // Chat Sessions indexes
                var sessIndexBuilder = Builders<ChatSessionDocument>.IndexKeys;
                var sessIndexes = new List<CreateIndexModel<ChatSessionDocument>>
                {
                    new(sessIndexBuilder.Ascending(s => s.TenantId).Descending(s => s.LastActivityAt)),
                    new(sessIndexBuilder.Ascending(s => s.SessionGuid), new CreateIndexOptions { Unique = true }),
                    new(sessIndexBuilder.Ascending(s => s.TenantId).Ascending(s => s.Status).Descending(s => s.LastActivityAt)),
                    new(sessIndexBuilder.Ascending(s => s.UserId))
                };
                await ChatSessions.Indexes.CreateManyAsync(sessIndexes);

                // Knowledge Base indexes
                var kbIndexBuilder = Builders<KnowledgeDocument>.IndexKeys;
                var kbIndexes = new List<CreateIndexModel<KnowledgeDocument>>
                {
                    new(kbIndexBuilder.Ascending(k => k.TenantId).Descending(k => k.Priority)),
                    new(kbIndexBuilder.Ascending(k => k.Category).Ascending(k => k.TenantId)),
                    new(kbIndexBuilder.Text(k => k.Question).Text(k => k.Keywords).Text(k => k.Answer))
                };
                await KnowledgeBase.Indexes.CreateManyAsync(kbIndexes);

                // Audit Log indexes
                var auditLogsCollection = _database.GetCollection<MongoDB.Bson.BsonDocument>("audit_logs");
                var auditIndexBuilder = Builders<MongoDB.Bson.BsonDocument>.IndexKeys;
                var auditIndexes = new List<CreateIndexModel<MongoDB.Bson.BsonDocument>>
                {
                    new(auditIndexBuilder.Ascending("Action")),
                    new(auditIndexBuilder.Ascending("EntityType")),
                    new(auditIndexBuilder.Descending("Timestamp")),
                    new(auditIndexBuilder.Ascending("Action").Descending("Timestamp")),
                    new(auditIndexBuilder.Ascending("EntityType").Ascending("Action")),
                    new(auditIndexBuilder.Ascending("UserId"))
                };
                await auditLogsCollection.Indexes.CreateManyAsync(auditIndexes);

                _indexesCreated = true;
                _logger.LogInformation("MongoDB indexes ensured successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create MongoDB indexes. Queries may be slower. This is non-fatal.");
                // Don't block startup - indexes can be created manually or on next restart
            }
        }
    }
}
