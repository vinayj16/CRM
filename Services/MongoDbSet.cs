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
        /// <summary>
        /// Resolves the integer ID property for this entity type.
        /// Returns null when the int id maps to MongoDB _id (int "Id" or [BsonId] on int)
        /// or when no unambiguous int id property exists.
        /// </summary>
        private static PropertyInfo? ResolveIntIdProperty(out bool mapsToMongoId)
        {
            mapsToMongoId = false;
            var type = typeof(T);

            // 1) [BsonId] property IS the MongoDB _id.
            var bsonIdProp = type.GetProperties()
                .FirstOrDefault(p => p.GetCustomAttribute(typeof(MongoDB.Bson.Serialization.Attributes.BsonIdAttribute), false) != null);

            if (bsonIdProp != null)
            {
                // int [BsonId] maps to _id
                if (bsonIdProp.PropertyType == typeof(int) || bsonIdProp.PropertyType == typeof(int?))
                {
                    mapsToMongoId = true;
                    return null;
                }
                // ObjectId [BsonId] -> integer ids live in a separate named field (search below)
            }

            // Properties marked [BsonIgnore] are not persisted, so they can never be
            // the stored document id (e.g. UserProfile.Id is [Key] + [BsonIgnore]).
            bool IsIgnored(PropertyInfo p)
                => p.GetCustomAttribute(typeof(MongoDB.Bson.Serialization.Attributes.BsonIgnoreAttribute), false) != null;

            // 2) Plain int "Id" property (not [BsonIgnore]) - MongoDB convention maps "Id" to _id
            var idProp = type.GetProperties().FirstOrDefault(p => p.Name == "Id" &&
                (p.PropertyType == typeof(int) || p.PropertyType == typeof(int?)) &&
                !IsIgnored(p));
            if (idProp != null)
            {
                mapsToMongoId = true;
                return null;
            }

            // 3) [Key]-annotated int property (LeadModel.LeadId, FollowUpModel.FollowUpId)
            var keyProp = type.GetProperties().FirstOrDefault(p =>
                p.GetCustomAttribute(typeof(System.ComponentModel.DataAnnotations.KeyAttribute), false) != null &&
                (p.PropertyType == typeof(int) || p.PropertyType == typeof(int?)) &&
                !IsIgnored(p));
            if (keyProp != null)
                return keyProp;

            // 4) Named integer id field: {TypeName}Id or {TypeName minus Model}Id
            //    (LeadModel -> LeadId, PropertyModel -> PropertyId)
            var baseName = type.Name.EndsWith("Model", StringComparison.Ordinal)
                ? type.Name.Substring(0, type.Name.Length - 5)
                : type.Name;

            var namedId = type.GetProperty(baseName + "Id", typeof(int))
                       ?? type.GetProperty(baseName + "Id", typeof(int?))
                       ?? type.GetProperty(type.Name + "Id", typeof(int))
                       ?? type.GetProperty(type.Name + "Id", typeof(int?));
            if (namedId != null && !IsIgnored(namedId))
                return namedId;

            // 5) Unambiguous fallback: exactly one other int property ending in "Id" (skip TenantId).
            //    Guessing between several candidates risks updating/deleting the wrong document,
            //    so we only resolve when there is a single candidate.
            var candidates = type.GetProperties()
                .Where(p => p.Name != "TenantId" && p.Name.EndsWith("Id") &&
                            (p.PropertyType == typeof(int) || p.PropertyType == typeof(int?)) &&
                            !IsIgnored(p))
                .ToList();

            return candidates.Count == 1 ? candidates[0] : null;
        }

        private static string? GetIntIdPropertyName()
        {
            if (_intIdPropertyChecked)
                return _intIdPropertyName;

            _intIdPropertyChecked = true;
            var prop = ResolveIntIdProperty(out bool mapsToMongoId);
            _intIdPropertyName = mapsToMongoId ? "_id" : prop?.Name;
            return _intIdPropertyName;
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
            AutoAssignIntId(document);
            _collection.InsertOne(document);
        }

        public Task AddAsync(T document)
        {
            StampTenant(document);
            AutoAssignIntId(document);
            return _collection.InsertOneAsync(document);
        }

        public void AddRange(IEnumerable<T> documents)
        {
            var list = documents?.ToList() ?? new List<T>();
            foreach (var d in list) { StampTenant(d); AutoAssignIntId(d); }
            if (list.Any())
                _collection.InsertMany(list);
        }

        public Task AddRangeAsync(IEnumerable<T> documents)
        {
            var list = documents?.ToList() ?? new List<T>();
            foreach (var d in list) { StampTenant(d); AutoAssignIntId(d); }
            if (list.Any())
                return _collection.InsertManyAsync(list);
            return Task.CompletedTask;
        }

        /// <summary>
        /// When the document's integer ID (resolved via [Key] / naming convention) is
        /// still 0, assigns max+1 before insert. This prevents entity documents from
        /// being persisted with id 0, which would make Find/Update/Remove by int id
        /// target the wrong document (or no document at all).
        /// Entities whose int id maps to MongoDB _id (plain "Id" or int [BsonId]) are
        /// skipped — the driver assigns those.
        /// NOTE: For AddRange/AddRangeAsync callers must pass a pre-computed id or
        /// use single Add calls when more than one id-less document is inserted at
        /// once, because each AutoAssignIntId call queries the DB before the batch
        /// is flushed and would assign the same max+1 to every id-less document.
        /// </summary>
        private void AutoAssignIntId(T document)
        {
            if (document == null) return;

            var idProp = ResolveIntIdProperty(out bool mapsToMongoId);
            if (idProp == null || mapsToMongoId) return;
            if (idProp.Name == "TenantId") return;

            var current = idProp.GetValue(document);
            if (current is int i && i != 0) return;

            int max = 0;
            try
            {
                var filter = WithTenant(FilterDefinition<T>.Empty);
                var last = _collection.Find(filter)
                    .Sort(Builders<T>.Sort.Descending(idProp.Name))
                    .Limit(1)
                    .FirstOrDefault();
                if (last != null && idProp.GetValue(last) is int mi)
                    max = mi;
            }
            catch
            {
                // Best effort only: if the id field cannot be sorted we leave the
                // id as-is rather than scanning the whole collection on every Add.
            }

            idProp.SetValue(document, max + 1);
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
            // Resolve the integer id property (or int "Id" mapped to _id)
            var idProp = ResolveIntIdProperty(out _);
            if (idProp != null)
                return idProp.GetValue(document);
            // Legacy fallback: _id / Id convention property (skip [BsonIgnore] members)
            var legacy = type.GetProperties().FirstOrDefault(p =>
                (p.Name == "_id" || p.Name == "Id") &&
                p.GetCustomAttribute(typeof(MongoDB.Bson.Serialization.Attributes.BsonIgnoreAttribute), false) == null);
            return legacy?.GetValue(document);
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