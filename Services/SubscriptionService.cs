using CRM.Helpers;
using CRM.Models;

namespace CRM.Services
{
    public class SubscriptionService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<SubscriptionService> _logger;

        public SubscriptionService(AppDbContext context, ILogger<SubscriptionService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<PartnerSubscriptionModel?> GetActiveSubscriptionAsync(int channelPartnerId)
        {
            // First, update subscription statuses
            await UpdateSubscriptionStatusesAsync(channelPartnerId);

            var subscription = await _context.PartnerSubscriptions
                .FirstOrDefaultAsync(s => s.ChannelPartnerId == channelPartnerId &&
                                         (s.Status == "Active" || s.Status == "Trial") &&
                                         s.EndDate > IndianTime.Now &&
                                         s.CancelledOn == null &&
                                         (s.CancellationReason == null || s.CancellationReason.Trim() == ""));

            if (subscription != null)
            {
                var plan = subscription.PlanId > 0 ? await _context.SubscriptionPlans.FindAsync(subscription.PlanId) : null;
                subscription.Plan = plan; // Set Plan so downstream methods (CanAddAgent, CanAddLead, etc.) can access plan properties
                _logger.LogInformation($"Found active subscription for partner {channelPartnerId}: ID={subscription.SubscriptionId}, Plan={plan?.PlanName}, EndDate={subscription.EndDate}");
            }
            else
            {
                _logger.LogWarning($"No active subscription found for partner {channelPartnerId}");
            }

            return subscription;
        }

        private async Task UpdateSubscriptionStatusesAsync(int channelPartnerId)
        {
            var now = IndianTime.Now;

            // Expire old subscriptions
            var expiredSubscriptions = await _context.PartnerSubscriptions
                .Where(s => s.ChannelPartnerId == channelPartnerId &&
                           (s.Status == "Active" || s.Status == "Trial") &&
                           s.EndDate <= now)
                .ToListAsync();

            foreach (var expired in expiredSubscriptions)
            {
                expired.Status = "Expired";
                expired.UpdatedOn = now;
                _logger.LogInformation($"Expired subscription {expired.SubscriptionId} for partner {channelPartnerId}");
            }

            // Activate scheduled subscriptions - but NEVER activate any cancelled/refunded subscriptions
            // First get all scheduled subscriptions that could be activated
            var candidateSubscriptions = await _context.PartnerSubscriptions
                .Where(s => s.ChannelPartnerId == channelPartnerId &&
                           s.Status == "Scheduled" &&
                           s.StartDate <= now &&
                           s.CancelledOn == null &&
                           (s.CancellationReason == null || s.CancellationReason.Trim() == ""))
                .ToListAsync();

            // Filter out any subscriptions that have refund/cancellation transactions
            var scheduledSubscriptions = new List<PartnerSubscriptionModel>();
            foreach (var candidate in candidateSubscriptions)
            {
                var hasRefundTransaction = await _context.PaymentTransactions
                    .AnyAsync(t => t.SubscriptionId == candidate.SubscriptionId &&
                                  (t.TransactionType == "Cancellation" || t.TransactionType == "Refund"));

                if (!hasRefundTransaction)
                {
                    scheduledSubscriptions.Add(candidate);
                }
                else
                {
                    _logger.LogWarning($"Skipping activation of subscription {candidate.SubscriptionId} - has refund/cancellation transactions");
                }
            }

            _logger.LogInformation($"Found {scheduledSubscriptions.Count} scheduled subscriptions to activate for partner {channelPartnerId}: [{string.Join(", ", scheduledSubscriptions.Select(s => s.SubscriptionId))}]");

            foreach (var scheduled in scheduledSubscriptions)
            {
                scheduled.Status = "Active";
                scheduled.UpdatedOn = now;
                _logger.LogInformation($"Activated scheduled subscription {scheduled.SubscriptionId} for partner {channelPartnerId}");
            }

            if (expiredSubscriptions.Any() || scheduledSubscriptions.Any())
            {
                await _context.SaveChangesAsync();
            }
        }

        public async Task<List<SubscriptionPlanModel>> GetAvailablePlansAsync()
        {
            return await _context.SubscriptionPlans
                .Where(p => p.IsActive)
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.MonthlyPrice)
                .ToListAsync();
        }

        public async Task<(bool CanAdd, string Message)> CanAddAgentAsync(int channelPartnerId)
        {
            var subscription = await GetActiveSubscriptionAsync(channelPartnerId);
            if (subscription?.Plan == null)
                return (false, "No active subscription found");

            var currentAgentCount = await _context.Users
                .CountAsync(u => u.ChannelPartnerId == channelPartnerId &&
                               u.IsActive &&
                               (u.Role == "Sales" || u.Role == "Agent"));

            if (subscription.Plan.MaxAgents == -1)
                return (true, "");

            if (currentAgentCount >= subscription.Plan.MaxAgents)
                return (false, $"Agent limit reached. Your {subscription.Plan.PlanName} plan allows {subscription.Plan.MaxAgents} agents only.");

            return (true, "");
        }

        public async Task<(bool CanAdd, string Message)> CanAddLeadAsync(int channelPartnerId)
        {
            var subscription = await GetActiveSubscriptionAsync(channelPartnerId);
            if (subscription?.Plan == null)
            {
                _logger.LogWarning($"No active subscription found for partner {channelPartnerId}");
                return (false, "No active subscription found");
            }

            _logger.LogInformation($"Partner {channelPartnerId} has subscription {subscription.SubscriptionId} with plan {subscription.Plan.PlanName}, MaxLeadsPerMonth: {subscription.Plan.MaxLeadsPerMonth}");

            if (subscription.Plan.MaxLeadsPerMonth == -1)
                return (true, "");

            var currentMonth = IndianTime.Now.Month;
            var currentYear = IndianTime.Now.Year;
            var currentMonthLeads = await _context.Leads
                .CountAsync(l => l.ChannelPartnerId == channelPartnerId &&
                               l.CreatedOn.Month == currentMonth &&
                               l.CreatedOn.Year == currentYear);

            if (currentMonthLeads >= subscription.Plan.MaxLeadsPerMonth)
                return (false, $"Monthly lead limit reached.");

            return (true, "");
        }

        public async Task<(bool CanUpload, string Message, long CurrentUsageGB, long LimitGB)> CanUploadFileAsync(int channelPartnerId, long fileSizeBytes)
        {
            var subscription = await GetActiveSubscriptionAsync(channelPartnerId);
            if (subscription == null)
                return (false, "No active subscription found", 0, 0);

            // Plan is [BsonIgnore] on PartnerSubscriptionModel, look up separately
            var plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.PlanId == subscription.PlanId);
            if (plan == null)
                return (false, "No active plan found", 0, 0);

            if (plan.MaxStorageGB == -1)
                return (true, "", 0, -1);

            // Calculate current storage usage from AgentDocuments and ChannelPartnerDocuments using FileSize
            var agentDocsSize = await _context.AgentDocuments
                .Where(d => d.Agent != null && d.Agent.ChannelPartnerId == channelPartnerId)
                .SumAsync(d => (long?)d.FileSize) ?? 0;

            var partnerDocsSize = await _context.ChannelPartnerDocuments
                .Where(d => d.ChannelPartnerId == channelPartnerId)
                .SumAsync(d => (long?)d.FileSize) ?? 0;

            var totalUsageBytes = agentDocsSize + partnerDocsSize;
            var totalUsageGB = totalUsageBytes / (1024m * 1024m * 1024m);
            var fileSizeGB = fileSizeBytes / (1024m * 1024m * 1024m);
            var newTotalGB = totalUsageGB + fileSizeGB;

            if (newTotalGB > plan.MaxStorageGB)
            {
                return (false,
                    $"Storage limit exceeded. Your {subscription.Plan.PlanName} plan allows {subscription.Plan.MaxStorageGB}GB. Current usage: {totalUsageGB:F2}GB",
                    (long)Math.Ceiling(totalUsageGB),
                    subscription.Plan.MaxStorageGB);
            }

            return (true, "", (long)Math.Ceiling(totalUsageGB), subscription.Plan.MaxStorageGB);
        }

    public async Task<bool> HasFeatureAccessAsync(int channelPartnerId, string featureName)
    {
        var subscription = await GetActiveSubscriptionAsync(channelPartnerId);
        if (subscription == null)
            return false;

        // Plan is [BsonIgnore] on PartnerSubscriptionModel, look up separately
        var plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.PlanId == subscription.PlanId);
        if (plan == null)
            return false;

        return featureName.ToLower() switch
        {
            "whatsapp" => plan.HasWhatsAppIntegration,
            "facebook" => plan.HasFacebookIntegration,
            "email" => plan.HasEmailIntegration,
            "customapi" => plan.HasCustomAPIAccess,
            "advancedreports" => plan.HasAdvancedReports,
            "customreports" => plan.HasCustomReports,
            "dataexport" => plan.HasDataExport,
            "prioritysupport" => plan.HasPrioritySupport,
            "phonesupport" => plan.HasPhoneSupport,
            "dedicatedmanagerr" => plan.HasDedicatedmanager,
            _ => false
        };
    }

        public async Task<PartnerSubscriptionModel> CreateSubscriptionAsync(int channelPartnerId, int planId, string billingCycle, string paymentTransactionId)
        {
            var plan = await _context.SubscriptionPlans.FindAsync(planId);
            if (plan == null)
                throw new ArgumentException("Invalid plan ID");

            // Set Plan on any existing active subscription being cancelled, then on the new one
            // so downstream code can access plan properties

            // Expire any existing active subscriptions
            var existingSubscriptions = await _context.PartnerSubscriptions
                .Where(s => s.ChannelPartnerId == channelPartnerId && (s.Status == "Active" || s.Status == "Trial"))
                .ToListAsync();

            foreach (var existing in existingSubscriptions)
            {
                existing.Status = "Cancelled";
                existing.UpdatedOn = IndianTime.Now;
            }

            var amount = billingCycle.ToLower() == "annual" ? plan.YearlyPrice : plan.MonthlyPrice;
            var endDate = billingCycle.ToLower() == "annual"
                ? IndianTime.Now.AddYears(1)
                : IndianTime.Now.AddMonths(1);

            var subscription = new PartnerSubscriptionModel
            {
                ChannelPartnerId = channelPartnerId,
                PlanId = planId,
                BillingCycle = billingCycle ?? "monthly",
                Amount = amount,
                StartDate = IndianTime.Now,
                EndDate = endDate,
                Status = "Active", // Immediately activate upon payment
                PaymentTransactionId = paymentTransactionId,
                CreatedOn = IndianTime.Now
            };

            _context.PartnerSubscriptions.Add(subscription);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"Created and activated subscription {subscription.SubscriptionId} for partner {channelPartnerId}");

            return subscription;
        }

        public async Task<PartnerSubscriptionModel> CreateScheduledSubscriptionAsync(int channelPartnerId, int planId, string billingCycle, DateTime startDate)
        {
            var plan = await _context.SubscriptionPlans.FindAsync(planId);
            if (plan == null)
                throw new ArgumentException("Invalid plan ID");

            var amount = billingCycle.ToLower() == "annual" ? plan.YearlyPrice : plan.MonthlyPrice;
            var endDate = billingCycle.ToLower() == "annual"
                ? startDate.AddYears(1)
                : startDate.AddMonths(1);

            var subscription = new PartnerSubscriptionModel
            {
                ChannelPartnerId = channelPartnerId,
                PlanId = planId,
                BillingCycle = billingCycle ?? "monthly",
                Amount = amount,
                StartDate = startDate,
                EndDate = endDate,
                Status = "Scheduled",
                CreatedOn = IndianTime.Now
            };

            _context.PartnerSubscriptions.Add(subscription);
            await _context.SaveChangesAsync();

            return subscription;
        }

        public async Task UpdateUsageStatsAsync(int channelPartnerId)
        {
            var subscription = await GetActiveSubscriptionAsync(channelPartnerId);
            if (subscription == null) return;

            var agentCount = (int)(await _context.Users
                .CountAsync(u => u.ChannelPartnerId == channelPartnerId &&
                               u.IsActive &&
                               (u.Role == "Sales" || u.Role == "Agent")));

            var currentMonth = IndianTime.Now.Month;
            var currentYear = IndianTime.Now.Year;
            var leadCount = (int)(await _context.Leads
                .CountAsync(l => l.ChannelPartnerId == channelPartnerId &&
                               l.CreatedOn.Month == currentMonth &&
                               l.CreatedOn.Year == currentYear));

            subscription.CurrentAgentCount = agentCount;
            subscription.CurrentMonthLeads = leadCount;
            subscription.UpdatedOn = IndianTime.Now;

            _context.PartnerSubscriptions.Update(subscription);
            await _context.SaveChangesAsync();
        }
    }
}