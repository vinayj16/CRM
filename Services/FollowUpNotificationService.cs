using CRM.Helpers;
using CRM.Models;

namespace CRM.Services
{
    public class FollowUpNotificationService : BackgroundService
    {
        private readonly ILogger<FollowUpNotificationService> _logger;
        private readonly IServiceProvider _serviceProvider;

        public FollowUpNotificationService(
            ILogger<FollowUpNotificationService> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Follow-Up Notification Service starting...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Calculate time until 9:00 AM
                    var now = IndianTime.Now;
                    var scheduledTime = new DateTime(now.Year, now.Month, now.Day, 9, 0, 0);
                    if (now > scheduledTime)
                        scheduledTime = scheduledTime.AddDays(1);

                    var delay = scheduledTime - now;
                    _logger.LogInformation($"Next daily notification scheduled for: {scheduledTime}");

                    await Task.Delay(delay, stoppingToken);
                    await SendDailyFollowUpNotifications();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError($"Error in Follow-Up Notification Service: {ex.Message}");
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }
        }

        private async Task SendDailyFollowUpNotifications()
        {
            _logger.LogInformation("sending daily followup notification for all tenenats");
            await TenantBackgroundHelper.ForEachTenantAsync(_serviceProvider, _logger, async (tenantDb, tenant, scopeProvider) =>
            {
                ////try
                ////{
                ////    using var scope = _serviceProvider.CreateScope();
                //    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var notificationService = scopeProvider.GetRequiredService<INotificationService>();

                var today = IndianTime.Today;

                var todayFollowUps = await tenantDb.LeadFollowUps
                    
                            .Join(tenantDb.Leads, (FollowUpModel f) => f.LeadId, (LeadModel l) => l.LeadId, (f, l) => new { f, l })
                    .Where(x => x.f.FollowUpDate.HasValue && x.f.FollowUpDate.Value.Date == today)
                    .ToListAsync();
                if (!todayFollowUps.Any()) return;

                var followUpsByExecutive = todayFollowUps.GroupBy(x => x.f.ExecutiveId).ToList();
                //var notificationsSent = 0;

                foreach (var group in followUpsByExecutive)
                {
                    var executiveId = group.Key;
                    var followUps = group.ToList();

                    if (followUps.Count == 1)
                    {
                        var fu = followUps.First();
                        await notificationService.CreateNotificationAsync(
                            "Follow-Up Scheduled for Today",
                            $"You have a follow-up today at {fu.f.FollowUpTime ?? "Not specified"} with {fu.l?.Name ?? "Lead"}. Stage: {fu.f.Stage}, Status: {fu.f.Status}",
                            "FollowUp", executiveId,
                            $"/Leads/Details/{fu.f.LeadId}", fu.f.LeadId, "Lead", "High");
                    }
                    else
                    {
                        var names = followUps.Select(f => f.l?.Name ?? "Lead").ToList();
                        var namesText = names.Count <= 3
                            ? string
                            .Join(", ", names)
                            : $"{string
                            .Join(", ", names.Take(3))} and {names.Count - 3} more";

                        await notificationService.CreateNotificationAsync(
                            "Multiple Follow-Ups Today",
                            $"You have {followUps.Count} follow-ups scheduled today: {namesText}",
                            "FollowUp", executiveId,
                            "/Leads/Index?followUpToday=true", null, "Lead", "High");
                    }
                }

                _logger.LogInformation($"sent follow up notifiaction for tenenat: {tenant.CompanyName}");
            });
        }
    }
}
