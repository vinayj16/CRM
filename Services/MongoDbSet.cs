using MongoDB.Driver;
using System.Linq.Expressions;
using System.Reflection;
using CRM.Services;

namespace CRM
{
    /// <summary>
    /// Wraps IMongoCollection{T} and provides EF Core-compatible instance methods
    /// (Where, FirstOrDefault, ToList, Add, Remove, FindAsync, etc.)
    /// Using instance methods avoids the extension method conflicts that plagued
    /// the earlier shims approach.
    ///
    /// TENANT ISOLATION:
    /// When a tenant is resolved (via ITenantService, which reads the "TenantId"
    /// auth claim or the request subdomain), every READ is automatically filtered
    /// so that only documents belonging to the current tenant (TenantId == current)
    /// OR documents that have no TenantId field (legacy/shared) are returned.
    /// Every WRITE is automatically stamped with the current TenantId.
    /// Types that do not declare a TenantId property (master/SaaS-global types such
    /// as Tenants, SaasPlans, Permissions, Modules, Pages, etc.) are never filtered.
    /// </summary>
    public class MongoDbSet<T> where T : class
    {
        private readonly IMongoCollection<T> _collection;
        private readonly ITenantService? _tenantService;

        public MongoDbSet(IMongoCollection<T> collection, ITenantService? tenantService = null)
        {
            _collection = collection;
            _tenantService = tenantService;
        }

        /// <summary>
        /// Returns the underlying IMongoCollection for advanced operations.
        /// </summary>
        public IMongoCollection<T> Collection => _collection;

        // =================================================================
        // Tenant isolation helpers
        // =================================================================

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, bool> _hasTenantIdCache = new();
        private static bool HasTenantId
        {
            get
            {
                if (!_hasTenantIdCache.TryGetValue(typeof(T), out bool hasId))
                {
                    hasId = typeof(T).GetProperty("TenantId", typeof(int)) != null
                            || typeof(T).GetProperty("TenantId", typeof(int?)) != null;
                    _hasTenantIdCache[typeof(T)] = hasId;
                }
                return hasId;
            }
        }

        private int CurrentTenantId => _tenantService?.GetTenantId() ?? 0;

        /// <summary>
        /// Combines a base filter with the tenant isolation filter.
        /// Returns the base filter unchanged when no tenant is resolved or the
        /// entity type is not tenant-scoped.
        /// </summary>
        private FilterDefinition<T> WithTenant(FilterDefinition<T> baseFilter)
        {
            var tid = CurrentTenantId;
            if (tid <= 0 || !HasTenantId)
                return baseFilter;

            // Match documents that belong to the current tenant OR that have no
            // TenantId field at all (legacy / shared documents remain visible).
            var tenantFilter = Builders<T>.Filter.Or(
                Builders<T>.Filter.Eq("TenantId", tid),
                Builders<T>.Filter.Exists("TenantId", false));

            return baseFilter & tenantFilter;
        }

        /// <summary>
        /// Stamps the current TenantId onto a document before it is written,
        /// but only when a tenant is resolved and the property is currently unset.
        /// </summary>
        private void StampTenant(T document)
        {
            if (document == null) return;
            var tid = CurrentTenantId;
            if (tid <= 0 || !HasTenantId) return;

            var prop = typeof(T).GetProperty("TenantId", typeof(int))
                       ?? typeof(T).GetProperty("TenantId", typeof(int?));
            if (prop == null) return;

            var current = prop.GetValue(document);
            var isUnset = current == null
                          || (current is int ci && ci == 0);
            if (isUnset)
                prop.SetValue(document, tid);
        }

        // =================================================================
        // LINQ / Query methods — return IQueryable<T> for further chaining
        // =================================================================

        private IQueryable<T> MaterializedQueryable()
            => _collection.Find(WithTenant(FilterDefinition<T>.Empty)).ToList().AsQueryable();

        public IQueryable<T> AsQueryable()
            => MaterializedQueryable();

        public IQueryable<T> Where(Expression<Func<T, bool>> predicate)
            => MaterializedQueryable().Where(predicate);

        public IOrderedQueryable<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
            => MaterializedQueryable().OrderBy(keySelector);

        public IOrderedQueryable<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
            => MaterializedQueryable().OrderByDescending(keySelector);

        public IQueryable<T> Take(int count)
            => MaterializedQueryable().Take(count);

        public IQueryable<T> Skip(int count)
            => MaterializedQueryable().Skip(count);

        public IQueryable<TResult> Select<TResult>(Expression<Func<T, TResult>> selector)
            => MaterializedQueryable().Select(selector);

        // =================================================================
        // Join support (delegates to Queryable.Join to avoid AsyncEnumerable conflict)
        // =================================================================

        public IQueryable<TResult> Join<TInner, TKey, TResult>(
            MongoDbSet<TInner> inner,
            Expression<Func<T, TKey>> outerKeySelector,
            Expression<Func<TInner, TKey>> innerKeySelector,
            Expression<Func<T, TInner, TResult>> resultSelector)
            where TInner : class
            => MaterializedQueryable().Join(inner.MaterializedQueryable(), outerKeySelector, innerKeySelector, resultSelector);

        public IQueryable<TResult> Join<TInner, TKey, TResult>(
            IQueryable<TInner> inner,
            Expression<Func<T, TKey>> outerKeySelector,
            Expression<Func<TInner, TKey>> innerKeySelector,
            Expression<Func<T, TInner, TResult>> resultSelector)
            where TInner : class
            => MaterializedQueryable().Join(inner, outerKeySelector, innerKeySelector, resultSelector);

        public IQueryable<TResult> Join<TInner, TKey, TResult>(
            IEnumerable<TInner> inner,
            Expression<Func<T, TKey>> outerKeySelector,
            Expression<Func<TInner, TKey>> innerKeySelector,
            Expression<Func<T, TInner, TResult>> resultSelector)
            where TInner : class
            => MaterializedQueryable().Join(inner.AsQueryable(), outerKeySelector, innerKeySelector, resultSelector);

        // =================================================================
        // Include / ThenInclude / AsNoTracking — no-ops in MongoDB
        // =================================================================

        public IQueryable<T> Include<TProperty>(Expression<Func<T, TProperty>> navigationPropertyPath)
            => MaterializedQueryable();

        public IQueryable<T> ThenInclude<TProperty>(Expression<Func<T, TProperty>> navigationPropertyPath)
            => MaterializedQueryable();

        public IQueryable<T> AsNoTracking()
            => MaterializedQueryable();

        // =================================================================
        // Sync terminal methods — execute immediately
        // =================================================================

        public T? FirstOrDefault()
            => _collection.Find(WithTenant(FilterDefinition<T>.Empty)).FirstOrDefault();

        public T? FirstOrDefault(Expression<Func<T, bool>> filter)
            => _collection.Find(WithTenant(filter)).FirstOrDefault();

        public List<T> ToList()
            => _collection.Find(WithTenant(FilterDefinition<T>.Empty)).ToList();

        public List<T> ToList(Expression<Func<T, bool>> filter)
            => _collection.Find(WithTenant(filter)).ToList();

        public int Count()
            => (int)_collection.CountDocuments(WithTenant(FilterDefinition<T>.Empty));

        public int Count(Expression<Func<T, bool>> filter)
            => (int)_collection.CountDocuments(WithTenant(filter));

        public long LongCount()
            => _collection.CountDocuments(WithTenant(FilterDefinition<T>.Empty));

        public long LongCount(Expression<Func<T, bool>> filter)
            => _collection.CountDocuments(WithTenant(filter));

        public bool Any()
            => _collection.Find(WithTenant(FilterDefinition<T>.Empty)).Any();

        public bool Any(Expression<Func<T, bool>> filter)
            => _collection.Find(WithTenant(filter)).Any();

        // =================================================================
        // Async terminal methods
        // =================================================================

        public async Task<List<T>> ToListAsync()
            => await _collection.Find(WithTenant(FilterDefinition<T>.Empty)).ToListAsync();

        public async Task<List<T>> ToListAsync(Expression<Func<T, bool>> filter)
            => await _collection.Find(WithTenant(filter)).ToListAsync();

        public async Task<List<T>> ToListAsync(CancellationToken cancellationToken)
            => await _collection.Find(WithTenant(FilterDefinition<T>.Empty)).ToListAsync(cancellationToken);

        public async Task<List<T>> ToListAsync(Expression<Func<T, bool>> filter, CancellationToken cancellationToken)
            => await _collection.Find(WithTenant(filter)).ToListAsync(cancellationToken);

        public async Task<T?> FirstOrDefaultAsync()
            => await _collection.Find(WithTenant(FilterDefinition<T>.Empty)).FirstOrDefaultAsync();

        public async Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> filter)
            => await _collection.Find(WithTenant(filter)).FirstOrDefaultAsync();

        public async Task<T?> FirstOrDefaultAsync(CancellationToken cancellationToken)
            => await _collection.Find(WithTenant(FilterDefinition<T>.Empty)).FirstOrDefaultAsync(cancellationToken);

        public async Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> filter, CancellationToken cancellationToken)
            => await _collection.Find(WithTenant(filter)).FirstOrDefaultAsync(cancellationToken);

        public async Task<T?> SingleOrDefaultAsync()
            => await MaterializedQueryable().SingleOrDefaultAsync();

        public async Task<T?> SingleOrDefaultAsync(Expression<Func<T, bool>> predicate)
            => await MaterializedQueryable().SingleOrDefaultAsync(predicate);

        public async Task<int> CountAsync()
            => (int)await _collection.CountDocumentsAsync(WithTenant(FilterDefinition<T>.Empty));

        public async Task<long> LongCountAsync()
            => await _collection.CountDocumentsAsync(WithTenant(FilterDefinition<T>.Empty));

        public async Task<int> CountAsync(Expression<Func<T, bool>> filter)
            => (int)await _collection.CountDocumentsAsync(WithTenant(filter));

        public async Task<long> LongCountAsync(Expression<Func<T, bool>> filter)
            => await _collection.CountDocumentsAsync(WithTenant(filter));

        public async Task<bool> AnyAsync()
            => await _collection.Find(WithTenant(FilterDefinition<T>.Empty)).AnyAsync();

        public async Task<bool> AnyAsync(Expression<Func<T, bool>> filter)
            => await _collection.Find(WithTenant(filter)).AnyAsync();

        // =================================================================
        // SumAsync helpers (execute synchronously via IQueryable bridge)
        // =================================================================

        public Task<decimal> SumAsync(Expression<Func<T, decimal>> selector)
            => Task.FromResult(MaterializedQueryable().Sum(selector));

        public Task<decimal?> SumAsync(Expression<Func<T, decimal?>> selector)
            => Task.FromResult(MaterializedQueryable().Sum(selector));

        public Task<int> SumAsync(Expression<Func<T, int>> selector)
            => Task.FromResult(MaterializedQueryable().Sum(selector));

        public Task<long> SumAsync(Expression<Func<T, long>> selector)
            => Task.FromResult(MaterializedQueryable().Sum(selector));

        public Task<double> SumAsync(Expression<Func<T, double>> selector)
            => Task.FromResult(MaterializedQueryable().Sum(selector));

        // =================================================================
        // Find by ID (mimics DbSet.Find / FindAsync(object id))
        // When id is an int, searches by {TypeName}Id property (e.g., PropertyId, LeadId)
        // instead of MongoDB _id (which is always ObjectId for entities that use [BsonId] on ObjectId).
        // When id is an ObjectId string or ObjectId, searches by _id field.
        // =================================================================

        private static string? _intIdPropertyName;
        private static bool _intIdPropertyChecked;

        /// <summary>
        /// Gets the int ID property name for this entity type.
        /// For PropertyModel, returns "PropertyId". For LeadModel, returns "LeadId".
        /// Returns null if no int ID property is found or if [BsonId] is on an int property (in which case _id search works).
        /// </summary>
        private static string? GetIntIdPropertyName()
        {
            if (_intIdPropertyChecked)
                return _intIdPropertyName;

            _intIdPropertyChecked = true;
            var type = typeof(T);

            // Check if [BsonId] is on an ObjectId field - if so, _id won't match int IDs
            bool bsonIdOnObjectId = false;
            foreach (var prop in type.GetProperties())
            {
                if (prop.GetCustomAttribute(typeof(MongoDB.Bson.Serialization.Attributes.BsonIdAttribute), false) != null)
                {
                    if (prop.PropertyType == typeof(MongoDB.Bson.ObjectId) ||
                        prop.PropertyType == typeof(MongoDB.Bson.ObjectId?))
                    {
                        bsonIdOnObjectId = true;
                    }
                    break;
                }
            }

            if (!bsonIdOnObjectId)
            {
                // Check if the type has an int Id property that is the primary key
                // (MongoDB convention maps Id to _id)
                var primaryIdProp = type.GetProperty("Id", typeof(int))
                            ?? type.GetProperty("Id", typeof(int?))
                            ?? type.GetProperty(type.Name + "Id", typeof(int))
                            ?? type.GetProperty(type.Name + "Id", typeof(int?));
                
                if (primaryIdProp != null)
                {
                    _intIdPropertyName = "_id";
                    return _intIdPropertyName;
                }
                
                _intIdPropertyName = null;
                return null;
            }

            // Look for {TypeName}Id property (e.g., PropertyId for PropertyModel)
            var typeName = type.Name;
            var idProp = type.GetProperty(typeName + "Id", typeof(int))
                       ?? type.GetProperty(typeName + "Id", typeof(int?));

            if (idProp != null)
            {
                _intIdPropertyName = idProp.Name;
                return _intIdPropertyName;
            }

            // Fallback: look for any int property ending with "Id"
            foreach (var prop in type.GetProperties())
            {
                if (prop.Name.EndsWith("Id") &&
                    (prop.PropertyType == typeof(int) || prop.PropertyType == typeof(int?)))
                {
                    _intIdPropertyName = prop.Name;
                    return _intIdPropertyName;
                }
            }

            _intIdPropertyName = null;
            return null;
        }

        public T? Find(object id)
        {
            // If id is an int and the entity has an int ID property, search by that property
            if (id is int intId)
            {
                var propName = GetIntIdPropertyName();
                if (propName != null)
                    return _collection.Find(WithTenant(Builders<T>.Filter.Eq(propName, intId))).FirstOrDefault();
            }

            // Default: search by MongoDB _id field
            return _collection.Find(WithTenant(Builders<T>.Filter.Eq("_id", id))).FirstOrDefault();
        }

        public async Task<T?> FindAsync(object id)
        {
            // If id is an int and the entity has an int ID property, search by that property
            if (id is int intId)
            {
                var propName = GetIntIdPropertyName();
                if (propName != null)
                    return await _collection.Find(WithTenant(Builders<T>.Filter.Eq(propName, intId))).FirstOrDefaultAsync();
            }

            // Default: search by MongoDB _id field
            return await _collection.Find(WithTenant(Builders<T>.Filter.Eq("_id", id))).FirstOrDefaultAsync();
        }

        // =================================================================
        // GroupBy support (returns IQueryable)
        // =================================================================

        public IQueryable<IGrouping<TKey, T>> GroupBy<TKey>(Expression<Func<T, TKey>> keySelector)
            => MaterializedQueryable().GroupBy(keySelector);

        // =================================================================
        // Min / Max / Average
        // =================================================================

        public Task<TResult> MinAsync<TResult>(Expression<Func<T, TResult>> selector)
            => Task.FromResult(MaterializedQueryable().Min(selector));

        public Task<TResult> MaxAsync<TResult>(Expression<Func<T, TResult>> selector)
            => Task.FromResult(MaterializedQueryable().Max(selector));

        // =================================================================
        // CRUD operations
        // =================================================================

        public void Add(T document)
        {
            StampTenant(document);
            _collection.InsertOne(document);
        }

        public Task AddAsync(T document)
        {
            StampTenant(document);
            return _collection.InsertOneAsync(document);
        }

        public void AddRange(IEnumerable<T> documents)
        {
            var list = documents?.ToList() ?? new List<T>();
            foreach (var d in list) StampTenant(d);
            if (list.Any())
                _collection.InsertMany(list);
        }

        public Task AddRangeAsync(IEnumerable<T> documents)
        {
            var list = documents?.ToList() ?? new List<T>();
            foreach (var d in list) StampTenant(d);
            if (list.Any())
                return _collection.InsertManyAsync(list);
            return Task.CompletedTask;
        }

        public void Remove(T document)
        {
            var id = GetDocumentId(document);
            if (id == null) return;

            if (id is int intId)
            {
                var propName = GetIntIdPropertyName();
                if (propName != null)
                {
                    _collection.DeleteOne(WithTenant(Builders<T>.Filter.Eq(propName, intId)));
                    return;
                }
            }

            if (id is MongoDB.Bson.ObjectId oid && oid != MongoDB.Bson.ObjectId.Empty)
                _collection.DeleteOne(WithTenant(Builders<T>.Filter.Eq("_id", oid)));
        }

        public void RemoveRange(IEnumerable<T> documents)
        {
            if (documents == null || !documents.Any()) return;
            var ids = documents.Select(d => GetDocumentId(d))
                .Where(id => id != null)
                .ToList();

            var intIds = ids.Where(id => id is int).Cast<int>().ToList();
            var objectIds = ids.Where(id => id is MongoDB.Bson.ObjectId oid && oid != MongoDB.Bson.ObjectId.Empty)
                .Cast<MongoDB.Bson.ObjectId>().ToList();

            if (intIds.Any())
            {
                var propName = GetIntIdPropertyName();
                if (propName != null)
                    _collection.DeleteMany(WithTenant(Builders<T>.Filter.In(propName, intIds.Cast<object>())));
            }

            if (objectIds.Any())
                _collection.DeleteMany(WithTenant(Builders<T>.Filter.In("_id", objectIds)));
        }

        public void Update(T document)
        {
            var id = GetDocumentId(document);
            if (id == null) return;

            if (id is int intId)
            {
                var propName = GetIntIdPropertyName();
                if (propName != null)
                {
                    _collection.ReplaceOne(WithTenant(Builders<T>.Filter.Eq(propName, intId)), document);
                    return;
                }
            }

            if (id is MongoDB.Bson.ObjectId oid && oid != MongoDB.Bson.ObjectId.Empty)
                _collection.ReplaceOne(WithTenant(Builders<T>.Filter.Eq("_id", oid)), document);
        }

        // =================================================================
        // Private helpers
        // =================================================================

        private static object? GetDocumentId(T document)
        {
            var type = typeof(T);
            // First check for property with [BsonId] attribute (proper MongoDB mapping)
            foreach (var prop in type.GetProperties())
            {
                if (prop.GetCustomAttribute(typeof(MongoDB.Bson.Serialization.Attributes.BsonIdAttribute), false) != null)
                    return prop.GetValue(document);
            }
            // Fallback: look for Id, _id, or {TypeName}Id
            var idProp = type.GetProperty("Id") ?? type.GetProperty("_id") ??
                         type.GetProperty(type.Name + "Id");
            return idProp?.GetValue(document);
        }
    }

    /// <summary>
    /// IQueryable extension methods to add Include/ThenInclude/ToListAsync etc.
    /// These DO NOT conflict because they target IQueryable<T>, not IMongoCollection<T>.
    /// </summary>
    public static class MongoDbQueryableExtensions
    {
        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) where T : class => source;

        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source)
            => Task.FromResult(source.ToList());

        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken)
            => Task.FromResult(source.ToList());

        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate)
            => Task.FromResult(source.Where(predicate).ToList());

        public static Task<T?> FirstOrDefaultAsync<T>(this IQueryable<T> source) where T : class
            => Task.FromResult(source.FirstOrDefault());

        public static Task<T?> FirstOrDefaultAsync<T>(this IQueryable<T> source,
            Expression<Func<T, bool>> predicate) where T : class
            => Task.FromResult(source.FirstOrDefault(predicate));

        public static Task<T?> SingleOrDefaultAsync<T>(this IQueryable<T> source) where T : class
            => Task.FromResult(source.SingleOrDefault());

        public static Task<T?> SingleOrDefaultAsync<T>(this IQueryable<T> source,
            Expression<Func<T, bool>> predicate) where T : class
            => Task.FromResult(source.SingleOrDefault(predicate));

        public static Task<int> CountAsync<T>(this IQueryable<T> source)
            => Task.FromResult(source.Count());

        public static Task<int> CountAsync<T>(this IQueryable<T> source,
            Expression<Func<T, bool>> predicate) => Task.FromResult(source.Count(predicate));

        public static Task<long> LongCountAsync<T>(this IQueryable<T> source)
            => Task.FromResult(source.LongCount());

        public static Task<bool> AnyAsync<T>(this IQueryable<T> source)
            => Task.FromResult(source.Any());

        public static Task<bool> AnyAsync<T>(this IQueryable<T> source,
            Expression<Func<T, bool>> predicate) => Task.FromResult(source.Any(predicate));

        public static Task<decimal> SumAsync<T>(this IQueryable<T> source,
            Expression<Func<T, decimal>> selector) => Task.FromResult(source.Sum(selector));

        public static Task<decimal?> SumAsync<T>(this IQueryable<T> source,
            Expression<Func<T, decimal?>> selector) => Task.FromResult(source.Sum(selector));

        public static Task<int> SumAsync<T>(this IQueryable<T> source,
            Expression<Func<T, int>> selector) => Task.FromResult(source.Sum(selector));

        public static Task<long> SumAsync<T>(this IQueryable<T> source,
            Expression<Func<T, long>> selector) => Task.FromResult(source.Sum(selector));

        public static Task<double> SumAsync<T>(this IQueryable<T> source,
            Expression<Func<T, double>> selector) => Task.FromResult(source.Sum(selector));
    }
}