using MongoDB.Driver;

namespace CRM.Services
{
    public interface IMongoRepository<T> where T : class
    {
        /// <summary>Get all documents</summary>
        Task<List<T>> GetAllAsync();
        
        /// <summary>Get document by ObjectId string</summary>
        Task<T?> GetByIdAsync(string id);
        
        /// <summary>Get document by int ID field</summary>
        Task<T?> GetByIntIdAsync(int id, string fieldName = "Id");
        
        /// <summary>Find documents matching a filter</summary>
        Task<List<T>> FindAsync(FilterDefinition<T> filter);
        
        /// <summary>Find one document matching a filter</summary>
        Task<T?> FindOneAsync(FilterDefinition<T> filter);
        
        /// <summary>Insert a document</summary>
        Task InsertAsync(T document);
        
        /// <summary>Insert many documents</summary>
        Task InsertManyAsync(IEnumerable<T> documents);
        
        /// <summary>Replace a document</summary>
        Task ReplaceAsync(string id, T document);
        
        /// <summary>Update a document with partial fields</summary>
        Task UpdateAsync(string id, UpdateDefinition<T> update);
        
        /// <summary>Update many documents matching filter</summary>
        Task UpdateManyAsync(FilterDefinition<T> filter, UpdateDefinition<T> update);
        
        /// <summary>Delete a document by ObjectId</summary>
        Task DeleteAsync(string id);
        
        /// <summary>Delete a document by filter</summary>
        Task DeleteManyAsync(FilterDefinition<T> filter);
        
        /// <summary>Count documents matching filter</summary>
        Task<long> CountAsync(FilterDefinition<T>? filter = null);
        
        /// <summary>Get paginated results</summary>
        Task<(List<T> Items, long Total)> GetPagedAsync(FilterDefinition<T> filter, int page = 1, int pageSize = 20, SortDefinition<T>? sort = null);
        
        /// <summary>Get collection</summary>
        IMongoCollection<T> Collection { get; }
        
        /// <summary>Build a filter for tenant-scoped queries</summary>
        FilterDefinition<T> TenantFilter(int tenantId);
    }
}
