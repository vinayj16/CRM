using MongoDB.Driver;
using MongoDB.Bson;
using Microsoft.Extensions.Logging;

namespace CRM.Services
{
    public class MongoRepository<T> : IMongoRepository<T> where T : class
    {
        protected readonly IMongoCollection<T> _collection;
        protected readonly ILogger<MongoRepository<T>> _logger;

        public MongoRepository(IMongoCollection<T> collection, ILogger<MongoRepository<T>> logger)
        {
            _collection = collection;
            _logger = logger;
        }

        public IMongoCollection<T> Collection => _collection;

        public virtual FilterDefinition<T> TenantFilter(int tenantId)
        {
            return Builders<T>.Filter.Eq("TenantId", tenantId);
        }

        public async Task<List<T>> GetAllAsync()
        {
            return await _collection.Find(_ => true).ToListAsync();
        }

        public async Task<T?> GetByIdAsync(string id)
        {
            if (!ObjectId.TryParse(id, out _)) return null;
            var filter = Builders<T>.Filter.Eq("_id", ObjectId.Parse(id));
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<T?> GetByIntIdAsync(int id, string fieldName = "Id")
        {
            var filter = Builders<T>.Filter.Eq(fieldName, id);
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<List<T>> FindAsync(FilterDefinition<T> filter)
        {
            return await _collection.Find(filter).ToListAsync();
        }

        public async Task<T?> FindOneAsync(FilterDefinition<T> filter)
        {
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task InsertAsync(T document)
        {
            await _collection.InsertOneAsync(document);
        }

        public async Task InsertManyAsync(IEnumerable<T> documents)
        {
            await _collection.InsertManyAsync(documents);
        }

        public async Task ReplaceAsync(string id, T document)
        {
            if (!ObjectId.TryParse(id, out _)) return;
            var filter = Builders<T>.Filter.Eq("_id", ObjectId.Parse(id));
            await _collection.ReplaceOneAsync(filter, document);
        }

        public async Task UpdateAsync(string id, UpdateDefinition<T> update)
        {
            if (!ObjectId.TryParse(id, out _)) return;
            var filter = Builders<T>.Filter.Eq("_id", ObjectId.Parse(id));
            await _collection.UpdateOneAsync(filter, update);
        }

        public async Task UpdateManyAsync(FilterDefinition<T> filter, UpdateDefinition<T> update)
        {
            await _collection.UpdateManyAsync(filter, update);
        }

        public async Task DeleteAsync(string id)
        {
            if (!ObjectId.TryParse(id, out _)) return;
            var filter = Builders<T>.Filter.Eq("_id", ObjectId.Parse(id));
            await _collection.DeleteOneAsync(filter);
        }

        public async Task DeleteManyAsync(FilterDefinition<T> filter)
        {
            await _collection.DeleteManyAsync(filter);
        }

        public async Task<long> CountAsync(FilterDefinition<T>? filter = null)
        {
            filter ??= Builders<T>.Filter.Empty;
            return await _collection.CountDocumentsAsync(filter);
        }

        public async Task<(List<T> Items, long Total)> GetPagedAsync(
            FilterDefinition<T> filter, int page = 1, int pageSize = 20, SortDefinition<T>? sort = null)
        {
            var total = await _collection.CountDocumentsAsync(filter);
            sort ??= Builders<T>.Sort.Descending("CreatedOn");

            var items = await _collection.Find(filter)
                .Sort(sort)
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync();

            return (items, total);
        }
    }

    /// <summary>
    /// Registry for creating typed repository instances.
    /// </summary>
    public class MongoRepositoryRegistry
    {
        private readonly MongoDbContext _context;
        private readonly IServiceProvider _serviceProvider;

        public MongoRepositoryRegistry(MongoDbContext context, IServiceProvider serviceProvider)
        {
            _context = context;
            _serviceProvider = serviceProvider;
        }

        public IMongoRepository<T> GetRepository<T>(string collectionName) where T : class
        {
            var collection = _context.Database.GetCollection<T>(collectionName);
            var logger = _serviceProvider.GetRequiredService<ILogger<MongoRepository<T>>>();
            return new MongoRepository<T>(collection, logger);
        }
    }
}
