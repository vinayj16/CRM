using CRM.Helpers;
using MongoDB.Driver;

namespace CRM.Services
{
    public class PendingApprovalReminderService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PendingApprovalReminderService> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(24);

        public PendingApprovalReminderService(
            IServiceProvider serviceProvider,
            ILogger<PendingApprovalReminderService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Cleanup: Remove duplicate Pending Approval notifications on startup
            await CleanupDuplicateNotificationsAsync();

            // Wait 2 minutes after startup before first check
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckPendingApprovals();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error in Pending Approval Reminder: {ex.Message}");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }
        }

        private async Task CleanupDuplicateNotificationsAsync()
        {
            try
            {
                await TenantBackgroundHelper.ForEachTenantAsync(_serviceProvider, _logger, async (tenantDb, tenant, scopeProvider) =>
                {
                    var cutoff = IndianTime.Now.AddHours(-1);
                    var filter = Builders<Models.NotificationModel>.Filter.Where(n =>
                        n.Title == "Pending Approvals Reminder" && n.CreatedOn < cutoff
                        && (n.TenantId == tenant.TenantId || n.TenantId == 0));
                    var result = await tenantDb.Notifications.Collection.DeleteManyAsync(filter);
                    if (result.DeletedCount > 0)
                    {
                        _logger.LogInformation($"Cleaned up {result.DeletedCount} duplicate pending approval notifications for {tenant.CompanyName}");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Startup notification cleanup failed (non-critical): {ex.Message}");
            }
        }

        private async Task CheckPendingApprovals()
        {
            await TenantBackgroundHelper.ForEachTenantAsync(_serviceProvider, _logger, async (tenantDb, tenant, scopeProvider) =>
        {
            var notificationService = scopeProvider.GetRequiredService<INotificationService>();
            var fcmService = scopeProvider.GetRequiredService<FcmService>();

            var pendingAgents = await tenantDb.Agents.CountAsync(a => a.Status == "Pending");
            var pendingPartners = await tenantDb.ChannelPartners.CountAsync(p => p.Status == "Pending");
            var totalPending = pendingAgents + pendingPartners;

            if (totalPending == 0)
            {
                _logger.LogInformation("No pending approvals found.");
                return;
            }

            _logger.LogInformation($"Tenant {tenant.CompanyName} : Found {totalPending} pending approvals ({pendingAgents} agents, {pendingPartners} partners)");

            // DEDUP CHECK: Skip if a similar notification was already sent in the last 23 hours
            var existingToday = await tenantDb.Notifications
                .Where(n => n.Title == "Pending Approvals Reminder" && n.CreatedOn >= IndianTime.Now.AddHours(-23))
                .AnyAsync();
            if (existingToday)
            {
                _logger.LogInformation("Pending approval reminder already sent recently, skipping.");
                return;
            }

            var adminUsers = await tenantDb.Users
                .Where(u => u.Role == "Admin" && u.IsActive)
                .ToListAsync();

            var parts = new List<string>();

            if (pendingAgents > 0)
                parts.Add($"{pendingAgents} agent(s)");

            if (pendingPartners > 0)
                parts.Add($"{pendingPartners} partner(s)");

            var message = $"You have {string.Join(" and ", parts)} pending approval.";

            foreach (var admin in adminUsers)
            {
                await notificationService.CreateNotificationAsync(
                    "Pending Approvals Reminder",
                    message,
                    "Approval",
                    admin.UserId,
                    pendingPartners > 0 ? "/ManageUsers/PartnerApproval" : "/List",
                    null,
                    "Reminder",
                    "High",
                    tenant.TenantId);

                try
                {
                    await fcmService.SendNotificationToUser(
                        admin.UserId,
                        "Pending Approvals Reminder",
                        message,
                        pendingPartners > 0 ? "/ManageUsers/PartnerApproval" : "/List",
                        "Approval");
                }
                catch
                {
                }
            }
        });
        }
    }
}