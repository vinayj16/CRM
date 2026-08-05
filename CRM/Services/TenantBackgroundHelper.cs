using CRM.MasterDb;
using CRM.MasterDb.Models;

namespace CRM.Services
{
    /// <summary>
    /// Helper for background services to execute work across all active tenants.
    /// With MongoDB, all tenants are in the same database.
    /// </summary>
    public static class TenantBackgroundHelper
    {
        /// <summary>
        /// Executes an action for each active tenant.
        /// In MongoDB mode, uses a single AppDbContext with tenant filtering.
        /// </summary>
        public static async Task ForEachTenantAsync(
            IServiceProvider serviceProvider,
            ILogger logger,
            Func<AppDbContext, TenantModel, IServiceProvider, Task> action)
        {
            using var masterScope = serviceProvider.CreateScope();
            var masterDb = masterScope.ServiceProvider.GetRequiredService<MasterDbContext>();

            var tenants = await masterDb.Tenants
                .Where(t => t.IsActive && !t.IsSuspended)
                .AsNoTracking()
                .ToListAsync();

            foreach (var tenant in tenants)
            {
                try
                {
                    // In MongoDB, all tenants share the same database.
                    // Create a new scope for each tenant.
                    using var tenantScope = serviceProvider.CreateScope();
                    var tenantDb = tenantScope.ServiceProvider.GetRequiredService<AppDbContext>();
                    await action(tenantDb, tenant, tenantScope.ServiceProvider);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"Error processing tenant {tenant.TenantId} ({tenant.CompanyName}): {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Simplified version.
        /// </summary>
        public static async Task ForEachTenantAsync(
            IServiceProvider serviceProvider,
            ILogger logger,
            Func<AppDbContext, TenantModel, Task> action)
        {
            await ForEachTenantAsync(serviceProvider, logger, async (db, tenant, _) =>
            {
                await action(db, tenant);
            });
        }
    }
}