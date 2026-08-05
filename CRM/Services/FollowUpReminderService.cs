using CRM.Helpers;
using CRM.Models;

namespace CRM.Services
{
    public class FollowUpReminderService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<FollowUpReminderService> _logger;

        public FollowUpReminderService(IServiceProvider serviceProvider, ILogger<FollowUpReminderService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckFollowUpReminders();
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // Check every 1 minute
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in FollowUpReminderService");
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); // Wait 5 minutes on error
                }
            }
        }

        private async Task CheckFollowUpReminders()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var today = IndianTime.Today;

            // Get leads with follow-ups due today
            var leadsWithFollowUps = await context.Leads
                .Where(l => l.FollowUpDate.HasValue &&
                           l.FollowUpDate.Value.Date == today &&
                           l.ExecutiveId.HasValue &&
                           l.Status != "Completed" &&
                           l.Stage != "Booked")
                .ToListAsync();

            // Get all admins
            var admins = await context.Users
                .Where(u => u.Role == "Admin" && u.ChannelPartnerId == null)
                .ToListAsync();

            foreach (var lead in leadsWithFollowUps)
            {
                // Collect all userIds who should be notified for this lead
                var userIdsToNotify = new HashSet<int>();

                // Add assigned executive
                if (lead.ExecutiveId.HasValue)
                    userIdsToNotify.Add(lead.ExecutiveId.Value);

                // Add all admins
                foreach (var admin in admins)
                    userIdsToNotify.Add(admin.UserId);

                foreach (var userId in userIdsToNotify)
                {
                    // Check if notification already exists for this user + lead + today
                    var alreadyExists = await context.Notifications
                        .AnyAsync(n => n.UserId == userId &&
                                      (n.Type == "FollowUpDue" || n.Type == "FollowUp") &&
                                      n.RelatedEntityId == lead.LeadId &&
                                      n.CreatedOn.Date == today);

                    if (!alreadyExists)
                    {
                        var encodedId = IdObfuscator.Encode(lead.LeadId);

                        // First time today — save to DB
                        var notification = new NotificationModel
                        {
                            Title = "Follow-up Due Today",
                            Message = $"Follow-up for lead '{lead.Name}' is due today ({lead.FollowUpDate.Value:dd/MM/yyyy})",
                            Type = "FollowUpDue",
                            UserId = userId,
                            Link = $"/leaddetails/{IdObfuscator.Encode(lead.LeadId)}#scrollspyFollowups",
                            RelatedEntityId = lead.LeadId,
                            RelatedEntityType = "FollowUp",
                            Priority = "High",
                            IsRead = false,
                            CreatedOn = IndianTime.Now
                        };

                        context.Notifications.Add(notification);
                        await context.SaveChangesAsync();
                        _logger.LogInformation($"Created follow-up reminder for lead {lead.LeadId} for user {userId}");
                    }

                    // Always send FCM push (even if already in DB)
                    try
                    {
                        var fcmService = scope.ServiceProvider.GetRequiredService<FcmService>();
                        await fcmService.SendNotificationToUser(
                            userId,
                            "Follow-up Due Today",
                            $"Follow-up for lead '{lead.Name}' is due today ({lead.FollowUpDate.Value:dd/MM/yyyy})",
                            $"/leaddetails/{IdObfuscator.Encode(lead.LeadId)}#scrollspyFollowups",
                            "FollowUpDue",
                            lead.LeadId
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"FCM send failed for user {userId}: {ex.Message}");
                    }
                }
            }
        }
    }
}
