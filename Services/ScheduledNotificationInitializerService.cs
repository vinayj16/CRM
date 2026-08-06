using CRM.Helpers;
using System.Collections.Concurrent;

namespace CRM.Services
{
    public class ScheduledNotificationInitializerService : BackgroundService
    {
        private readonly ILogger<ScheduledNotificationInitializerService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private static readonly ConcurrentDictionary<int, Timer> _activeTimers = new();

        public ScheduledNotificationInitializerService(
            ILogger<ScheduledNotificationInitializerService> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Wait for app to fully start
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ScheduleTodayFollowUps();
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // Changed to 1 minute for testing
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error in scheduled notification initializer: {ex.Message}");
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }
        }

        public static void ClearAllActiveTimers()
        {
            Console.WriteLine($"TIMER-DEBUG: Clearing {_activeTimers.Count} active timers");
            foreach (var kvp in _activeTimers)
            {
                Console.WriteLine($"TIMER-DEBUG: Disposing timer for FollowUpId {kvp.Key}");
                kvp.Value.Dispose();
            }
            _activeTimers.Clear();
            Console.WriteLine($"TIMER-DEBUG: All timers cleared. Active count: {_activeTimers.Count}");
        }

        private async Task ScheduleTodayFollowUps()
        {
            await TenantBackgroundHelper.ForEachTenantAsync(_serviceProvider, _logger, async (tenantDb, tenant, scopeProvider) =>
            {
                //using var scope = _serviceProvider.CreateScope();
                //var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var today = IndianTime.Today;
                var now = IndianTime.Now;

                Console.WriteLine($"TIMER-DEBUG: Checking follow-ups for {today:yyyy-MM-dd} at {now:HH:mm:ss}");
                Console.WriteLine($"TIMER-DEBUG: Currently active timers: {_activeTimers.Count}");

                var todayFollowUps = await tenantDb.LeadFollowUps
                    .Where(f => f.FollowUpDate.HasValue &&
                               f.FollowUpDate.Value.Date == today &&
                               !string.IsNullOrEmpty(f.FollowUpTime) &&
                               (f.IsNotificationRead == false || f.IsNotificationRead == null))
                    .ToListAsync();

                if (!todayFollowUps.Any()) return;
                Console.WriteLine($"TIMER-DEBUG: Found {todayFollowUps.Count} unread follow-ups for today");

                foreach (var f in todayFollowUps)
                {
                    Console.WriteLine($"TIMER-DEBUG: FollowUpId {f.FollowUpId}, Time: {f.FollowUpTime}, IsNotificationRead: {f.IsNotificationRead}");
                }

                var scheduledCount = 0;

                foreach (var followUp in todayFollowUps)
                {
                    try
                    {
                        var followUpDateTime = ParseFollowUpDateTime(followUp.FollowUpDate.Value, followUp.FollowUpTime);

                        if (followUpDateTime <= now)
                            continue; // Already passed

                        // Skip if already scheduled
                        if (_activeTimers.ContainsKey(followUp.FollowUpId))
                            continue;

                        var delay = followUpDateTime - now;

                        var timer = new Timer(async _ =>
                        {
                            await OnFollowUpTimeReached(followUp.FollowUpId, followUp.LeadId, followUp.ExecutiveId, followUp.Stage, followUp.Status, followUp.FollowUpTime);
                            _activeTimers.TryRemove(followUp.FollowUpId, out Timer _);
                        }, null, delay, Timeout.InfiniteTimeSpan);

                        _activeTimers[followUp.FollowUpId] = timer;
                        //scheduledCount++;

                        _logger.LogInformation($"tenant:{tenant.CompanyName} Scheduled notification for FollowUpId {followUp.FollowUpId} at {followUpDateTime:HH:mm}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error scheduling FollowUpId {followUp.FollowUpId}: {ex.Message}");
                    }
                }

                //_logger.LogInformation($"Scheduled {scheduledCount} follow-up notifications for today");
            });
        }

        private async Task OnFollowUpTimeReached(int followUpId, int leadId, int executiveId, string stage, string status, string time)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // Check if notification is already read before firing
                var followUp = await context.LeadFollowUps.AsNoTracking()
                    .FirstOrDefaultAsync(f => f.FollowUpId == followUpId);

                //if (followUp == null || followUp.IsNotificationRead == true) return; 

                Console.WriteLine($"TIMER-DEBUG: Timer fired for FollowUpId {followUpId}. Current IsNotificationRead: {followUp?.IsNotificationRead}");

                if (followUp == null || followUp.IsNotificationRead == true)
                {
                    Console.WriteLine($"SCHEDULED-NOTIF: Skipped FollowUpId {followUpId} - already read or not found");
                    return;
                }

                var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
                var fcmService = scope.ServiceProvider.GetRequiredService<FcmService>(); // Add FCM service

                var lead = await context.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.LeadId == leadId);
                var leadName = lead?.Name ?? "Lead";

                // Notify assigned executive
                await notificationService.CreateNotificationAsync(
                    "Follow-Up Reminder",
                    $"It's time for your follow-up with {leadName} at {time}! Stage: {stage}, Status: {status}",
                    "FollowUpReminder",
                    executiveId,
                    $"/leaddetails/{IdObfuscator.Encode(leadId)}",
                    leadId,
                    "Lead",
                    "Urgent"
                );

                // Send FCM browser push notification to executive
                await fcmService.SendNotificationToUser(
                    executiveId,
                    "Follow-Up Reminder",
                    $"It's time for your follow-up with {leadName} at {time}!",
                    $"/leaddetails/{IdObfuscator.Encode(leadId)}",
                    "FollowUpReminder",
                    leadId
                );

                // Notify all admins
                var admins = await context.Users
                    .Where(u => u.Role == "Admin" && u.ChannelPartnerId == null && u.UserId != executiveId)
                    .ToListAsync();

                foreach (var admin in admins)
                {
                    await notificationService.CreateNotificationAsync(
                        "Follow-Up Reminder",
                        $"Follow-up time for {leadName} at {time}! Stage: {stage}, Status: {status}",
                        "FollowUpReminder",
                        admin.UserId,
                        $"/leaddetails/{IdObfuscator.Encode(leadId)}",
                        leadId,
                        "Lead",
                        "High"
                    );

                    // Send FCM browser push notification to admin
                    await fcmService.SendNotificationToUser(
                        admin.UserId,
                        "Follow-Up Reminder",
                        $"Follow-up time for {leadName} at {time}!",
                        $"/leaddetails/{IdObfuscator.Encode(leadId)}",
                        "FollowUpReminder",
                        leadId
                    );
                }

                Console.WriteLine($"SCHEDULED-NOTIF: Fired for FollowUpId {followUpId}, Lead {leadName} at {IndianTime.Now:HH:mm:ss}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SCHEDULED-NOTIF ERROR: {ex.Message}");
            }
        }

        private DateTime ParseFollowUpDateTime(DateTime date, string time)
        {
            // Handle formats: "00:15", "12:58", "14:30", "12:58 AM", "1:30 PM"
            if (DateTime.TryParse($"{date:yyyy-MM-dd} {time}", out DateTime result))
                return result;

            if (TimeSpan.TryParse(time, out TimeSpan timeSpan))
                return date.Date.Add(timeSpan);

            throw new ArgumentException($"Invalid time format: {time}");
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            // Dispose all timers
            foreach (var timer in _activeTimers.Values)
                timer.Dispose();
            _activeTimers.Clear();

            _logger.LogInformation("Scheduled Notification Initializer Service stopped");
            await base.StopAsync(stoppingToken);
        }
    }
}
