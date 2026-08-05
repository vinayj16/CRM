// ====================================================================
// MongoDbEfCoreShims is now replaced by MongoDbSet<T> (in MongoDbSet.cs)
// which uses instance methods instead of extension methods to avoid
// .NET 10 ImmutableArrayExtensions and MongoDB.Driver method conflicts.
//
// The DbFacade and DbTx classes are defined here for backward compatibility.
// ====================================================================

namespace CRM
{
    /// <summary>
    /// No-op transaction facade for MongoDB.
    /// </summary>
    public class DbFacade
    {
        public Task<DbTx> BeginTransactionAsync(CancellationToken ct = default)
            => Task.FromResult(new DbTx());
    }

    /// <summary>
    /// No-op transaction handle for MongoDB.
    /// </summary>
    public class DbTx : IDisposable
    {
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    /// <summary>
    /// Entry wrapper for compatibility with EF Core's Entry() pattern.
    /// </summary>
    public class EntryWrapper<T>(T entity) where T : class
    {
        public T Entity { get; } = entity;
        public string State { get; set; } = "Unchanged";
    }
}
