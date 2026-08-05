using CRM.Helpers;
using CRM.Models;
using CRM.Services;

namespace CRM.Services
{
    public class SubscriptionMonitoringService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SubscriptionMonitoringService> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(6); // Check every 6 hours

        public SubscriptionMonitoringService(
            IServiceProvider serviceProvider,
            ILogger<SubscriptionMonitoringService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Subscription Monitoring Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessSubscriptionMonitoring();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in subscription monitoring service");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }
        }

        private async Task ProcessSubscriptionMonitoring()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var subscriptionService = scope.ServiceProvider.GetRequiredService<SubscriptionService>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            _logger.LogInformation("Starting subscription monitoring check");

            // 1. Handle expired subscriptions
            await HandleExpiredSubscriptions(context, notificationService);

            // 2. Activate scheduled subscriptions
            await ActivateScheduledSubscriptions(context, notificationService);

            // 3. Send expiry warnings
            await SendExpiryWarnings(context, notificationService);

            // 4. Update usage statistics
            await UpdateUsageStatistics(context, subscriptionService);

            // 5. Check usage limits and send warnings
            await CheckUsageLimits(context, notificationService);

            _logger.LogInformation("Subscription monitoring check completed");
        }

        private async Task HandleExpiredSubscriptions(AppDbContext context, INotificationService notificationService)
        {
            var expiredSubscriptions = await context.PartnerSubscriptions
                
                
                .Where(s => s.Status == "Active" && s.EndDate <= IndianTime.Now)
                .ToListAsync();

            foreach (var subscription in expiredSubscriptions)
            {
                _logger.LogInformation($"Processing expired subscription {subscription.SubscriptionId} for partner {subscription.ChannelPartnerId}");

                // Update subscription status
                subscription.Status = "Expired";
                subscription.UpdatedOn = IndianTime.Now;

                // Create notification for partner
                if (subscription.ChannelPartner != null)
                {
                    // Get the partner's user ID
                    var partnerUser = await context.Users
                        .FirstOrDefaultAsync(u => u.ChannelPartnerId == subscription.ChannelPartnerId);
                    
                    if (partnerUser != null)
                    {
                        await notificationService.CreateNotificationAsync(
                            title: "Subscription Expired",
                            message: $"Your {subscription.Plan?.PlanName} subscription has expired. Please renew to continue using all features.",
                            type: "SubscriptionExpired",
                            userId: partnerUser.UserId,
                            link: "/Subscription/MyPlan",
                            priority: "High"
                        );
                    }
                }

                // Optionally downgrade to basic plan or suspend features
                // This can be implemented based on business requirements
            }

            if (expiredSubscriptions.Any())
            {
                await context.SaveChangesAsync();
                _logger.LogInformation($"Processed {expiredSubscriptions.Count} expired subscriptions");
            }
        }

        private async Task ActivateScheduledSubscriptions(AppDbContext context, INotificationService notificationService)
        {
            // Find scheduled subscriptions that should be activated (start date is today or earlier)
            // Exclude cancelled scheduled subscriptions
            var scheduledToActivate = await context.PartnerSubscriptions
                
                
                .Where(s => s.Status == "Scheduled" && s.StartDate <= IndianTime.Now)
                .ToListAsync();

            foreach (var scheduledSubscription in scheduledToActivate)
            {
                _logger.LogInformation($"Activating scheduled subscription {scheduledSubscription.SubscriptionId} for partner {scheduledSubscription.ChannelPartnerId}");

                // Find and expire any active subscriptions for this partner
                var activeSubscriptions = await context.PartnerSubscriptions
                    .Where(s => s.ChannelPartnerId == scheduledSubscription.ChannelPartnerId && 
                               s.Status == "Active" && 
                               s.SubscriptionId != scheduledSubscription.SubscriptionId)
                    .ToListAsync();

                foreach (var activeSub in activeSubscriptions)
                {
                    activeSub.Status = "Expired";
                    activeSub.UpdatedOn = IndianTime.Now;
                    _logger.LogInformation($"Expired subscription {activeSub.SubscriptionId} to make way for scheduled subscription");
                }

                // Activate the scheduled subscription
                scheduledSubscription.Status = "Active";
                scheduledSubscription.UpdatedOn = IndianTime.Now;

                // Create notification for partner
                if (scheduledSubscription.ChannelPartner != null)
                {
                    // Get the partner's user ID
                    var partnerUser = await context.Users
                        .FirstOrDefaultAsync(u => u.ChannelPartnerId == scheduledSubscription.ChannelPartnerId);
                    
                    if (partnerUser != null)
                    {
                        await notificationService.CreateNotificationAsync(
                            title: "Subscription Activated",
                            message: $"Your {scheduledSubscription.Plan?.PlanName} subscription is now active! Enjoy your new plan features.",
                            type: "SubscriptionActivated",
                            userId: partnerUser.UserId,
                            link: "/Subscription/MyPlan",
                            priority: "High"
                        );
                    }
                }
            }

            if (scheduledToActivate.Any())
            {
                await context.SaveChangesAsync();
                _logger.LogInformation($"Activated {scheduledToActivate.Count} scheduled subscriptions");
            }
        }

        private async Task SendExpiryWarnings(AppDbContext context, INotificationService notificationService)
        {
            // Send warnings for subscriptions expiring in 7 days
            var warningDate7 = IndianTime.Now.AddDays(7);
            var subscriptionsExpiring7Days = await context.PartnerSubscriptions
                
                
                .Where(s => s.Status == "Active" && 
                           s.EndDate <= warningDate7 && 
                           s.EndDate > IndianTime.Now)
                .ToListAsync();

            foreach (var subscription in subscriptionsExpiring7Days)
            {
                var daysRemaining = (subscription.EndDate - IndianTime.Now).Days;
                
                // Get the partner's user ID
                var partnerUser = await context.Users
                    .FirstOrDefaultAsync(u => u.ChannelPartnerId == subscription.ChannelPartnerId);
                
                if (partnerUser != null)
                {
                    await notificationService.CreateNotificationAsync(
                        title: "Subscription Expiring Soon",
                        message: $"Your {subscription.Plan?.PlanName} subscription expires in {daysRemaining} days. Renew now to avoid service interruption.",
                        type: "SubscriptionExpiring",
                        userId: partnerUser.UserId,
                        link: "/Subscription/MyPlan",
                        priority: "High"
                    );
                }
            }

            // Send warnings for subscriptions expiring in 1 day
            var warningDate1 = IndianTime.Now.AddDays(1);
            var subscriptionsExpiring1Day = await context.PartnerSubscriptions
                
                
                .Where(s => s.Status == "Active" && 
                           s.EndDate <= warningDate1 && 
                           s.EndDate > IndianTime.Now)
                .ToListAsync();

            foreach (var subscription in subscriptionsExpiring1Day)
            {
                // Get the partner's user ID
                var partnerUser = await context.Users
                    .FirstOrDefaultAsync(u => u.ChannelPartnerId == subscription.ChannelPartnerId);
                
                if (partnerUser != null)
                {
                    await notificationService.CreateNotificationAsync(
                        title: "Subscription Expires Tomorrow",
                        message: $"URGENT: Your {subscription.Plan?.PlanName} subscription expires tomorrow! Renew immediately to avoid service disruption.",
                        type: "SubscriptionExpiring",
                        userId: partnerUser.UserId,
                        link: "/Subscription/MyPlan",
                        priority: "Urgent"
                    );
                }
            }

            if (subscriptionsExpiring7Days.Any() || subscriptionsExpiring1Day.Any())
            {
                _logger.LogInformation($"Sent expiry warnings for {subscriptionsExpiring7Days.Count + subscriptionsExpiring1Day.Count} subscriptions");
            }
        }

        private async Task UpdateUsageStatistics(AppDbContext context, SubscriptionService subscriptionService)
        {
            var activeSubscriptions = await context.PartnerSubscriptions
                .Where(s => s.Status == "Active")
                .ToListAsync();

            foreach (var subscription in activeSubscriptions)
            {
                try
                {
                    await subscriptionService.UpdateUsageStatsAsync(subscription.ChannelPartnerId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error updating usage stats for subscription {subscription.SubscriptionId}");
                }
            }

            _logger.LogInformation($"Updated usage statistics for {activeSubscriptions.Count} active subscriptions");
        }

        private async Task CheckUsageLimits(AppDbContext context, INotificationService notificationService)
        {
            var subscriptionsNearLimit = await context.PartnerSubscriptions
                
                
                .Where(s => s.Status == "Active")
                .ToListAsync();

            foreach (var subscription in subscriptionsNearLimit)
            {
                if (subscription.Plan == null) continue;

                // Check agent limit (90% threshold)
                if (subscription.Plan.MaxAgents > 0)
                {
                    var agentUsagePercent = (double)subscription.CurrentAgentCount / subscription.Plan.MaxAgents * 100;
                    if (agentUsagePercent >= 90)
                    {
                        // Get the partner's user ID
                        var partnerUser = await context.Users
                            .FirstOrDefaultAsync(u => u.ChannelPartnerId == subscription.ChannelPartnerId);
                        
                        if (partnerUser != null)
                        {
                            await notificationService.CreateNotificationAsync(
                                title: "Agent Limit Warning",
                                message: $"You're using {subscription.CurrentAgentCount} of {subscription.Plan.MaxAgents} agents ({agentUsagePercent:F0}%). Consider upgrading your plan.",
                                type: "UsageLimitWarning",
                                userId: partnerUser.UserId,
                                link: "/Subscription/MyPlan",
                                priority: "Normal"
                            );
                        }
                    }
                }

                // Check monthly leads limit (80% threshold)
                if (subscription.Plan.MaxLeadsPerMonth > 0)
                {
                    var leadUsagePercent = (double)subscription.CurrentMonthLeads / subscription.Plan.MaxLeadsPerMonth * 100;
                    if (leadUsagePercent >= 80)
                    {
                        // Get the partner's user ID
                        var partnerUser = await context.Users
                            .FirstOrDefaultAsync(u => u.ChannelPartnerId == subscription.ChannelPartnerId);
                        
                        if (partnerUser != null)
                        {
                            await notificationService.CreateNotificationAsync(
                                title: "Monthly Lead Limit Warning",
                                message: $"You've used {subscription.CurrentMonthLeads} of {subscription.Plan.MaxLeadsPerMonth} leads this month ({leadUsagePercent:F0}%). Consider upgrading your plan.",
                                type: "UsageLimitWarning",
                                userId: partnerUser.UserId,
                                link: "/Subscription/MyPlan",
                                priority: "Normal"
                            );
                        }
                    }
                }

                // Check storage limit (85% threshold)
                if (subscription.Plan.MaxStorageGB > 0)
                {
                    var storageUsagePercent = (double)subscription.CurrentStorageUsedGB / subscription.Plan.MaxStorageGB * 100;
                    if (storageUsagePercent >= 85)
                    {
                        // Get the partner's user ID
                        var partnerUser = await context.Users
                            .FirstOrDefaultAsync(u => u.ChannelPartnerId == subscription.ChannelPartnerId);
                        
                        if (partnerUser != null)
                        {
                            await notificationService.CreateNotificationAsync(
                                title: "Storage Limit Warning",
                                message: $"You're using {subscription.CurrentStorageUsedGB:F1} GB of {subscription.Plan.MaxStorageGB} GB storage ({storageUsagePercent:F0}%). Consider upgrading your plan.",
                                type: "UsageLimitWarning",
                                userId: partnerUser.UserId,
                                link: "/Subscription/MyPlan",
                                priority: "Normal"
                            );
                        }
                    }
                }
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Subscription Monitoring Service is stopping");
            await base.StopAsync(stoppingToken);
        }
    }
}