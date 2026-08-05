using CRM.Helpers;
using CRM.Models;
using CRM.Services;

namespace CRM.BackgroundServices
{
    public class PaymentStatusSyncService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PaymentStatusSyncService> _logger;
        private readonly TimeSpan _syncInterval = TimeSpan.FromMinutes(5); // Sync every 5 minutes

        public PaymentStatusSyncService(IServiceProvider serviceProvider, ILogger<PaymentStatusSyncService> logger)
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
                    await SyncPaymentStatuses();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during payment status sync");
                }

                await Task.Delay(_syncInterval, stoppingToken);
            }
        }

        //private async Task SyncPaymentStatuses()
        //{
        //    await TenantBackgroundHelper.ForEachTenantAsync(_serviceProvider, _logger, async (tenantDb, tenant, scopeProvider) =>
        //    {
        //        //using var scope = _serviceProvider.CreateScope();
        //        //var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        //        var razorpayService = scopeProvider.GetRequiredService<RazorpayService>();

        //        try
        //        {
        //            // Get transactions that might need status updates (created in last 24 hours and not Success/Failed)
        //            var pendingTransactions = await tenantDb.PaymentTransactions
        //                .Include(t => t.Subscription)
        //                .Where(t => !string.IsNullOrEmpty(t.RazorpayPaymentId) &&
        //                           (t.Status == "Pending" || t.Status == "Authorized") &&
        //                           t.CreatedOn >= IndianTime.Now.AddHours(-24))
        //                .ToListAsync();

        //            if (!pendingTransactions.Any())
        //            {
        //                _logger.LogInformation("No pending transactions to sync");
        //                return;
        //            }

        //            int updated = 0;
        //            foreach (var transaction in pendingTransactions)
        //            {
        //                try
        //                {
        //                    var paymentDetails = await razorpayService.GetPaymentDetailsAsync(transaction.RazorpayPaymentId!);
        //                    var razorpayStatus = paymentDetails.status?.ToString();

        //                    if (!string.IsNullOrEmpty(razorpayStatus))
        //                    {
        //                        var oldStatus = transaction.Status;
        //                        var newStatus = razorpayStatus switch
        //                        {
        //                            "captured" => "Success",
        //                            "failed" => "Failed",
        //                            "created" => "Pending",
        //                            "authorized" => "Authorized",
        //                            "refunded" => "Refunded",
        //                            _ => transaction.Status
        //                        };

        //                        if (newStatus != transaction.Status)
        //                        {
        //                            transaction.Status = newStatus;
        //                            transaction.UpdatedOn = IndianTime.Now;

        //                            // Activate subscription only when payment is captured
        //                            if (razorpayStatus == "captured" && oldStatus != "Success" && transaction.Subscription != null)
        //                            {
        //                                await ActivateSubscriptionOnCapture(transaction.Subscription);
        //                            }

        //                            updated++;
        //                            _logger.LogInformation($"Tenant {tenant.CompanyName}:  Updated transaction {transaction.TransactionId} status from {oldStatus} to {newStatus}");
        //                        }
        //                    }
        //                }
        //                catch (Exception ex)
        //                {
        //                    _logger.LogWarning(ex, $"Failed to sync status for payment {transaction.RazorpayPaymentId} for Tenant {tenant.CompanyName}");
        //                }
        //            }

        //            if (updated > 0)
        //            {
        //                await tenantDb.SaveChangesAsync();
        //                _logger.LogInformation($"Payment status sync completed. Updated {updated} transactions.");
        //            }
        //        }});
        //}
        private async Task SyncPaymentStatuses()
        {
            await TenantBackgroundHelper.ForEachTenantAsync(
                _serviceProvider,
                _logger,
                async (tenantDb, tenant, scopeProvider) =>
                {
                    var razorpayService = scopeProvider.GetRequiredService<RazorpayService>();

                    try
                    {
                        var pendingTransactions = await tenantDb.PaymentTransactions
                            .Include(t => t.Subscription)
                            .Where(t =>
                                !string.IsNullOrEmpty(t.RazorpayPaymentId) &&
                                (t.Status == "Pending" || t.Status == "Authorized") &&
                                t.CreatedOn >= IndianTime.Now.AddHours(-24))
                            .ToListAsync();

                        if (!pendingTransactions.Any())
                        {
                            _logger.LogInformation($"Tenant {tenant.CompanyName}: No pending transactions");
                            return;
                        }

                        int updated = 0;

                        foreach (var transaction in pendingTransactions)
                        {
                            try
                            {
                                var paymentDetails = await razorpayService
                                    .GetPaymentDetailsAsync(transaction.RazorpayPaymentId!);

                                if (paymentDetails == null || paymentDetails.status == null)
                                    continue;

                                var razorpayStatus = paymentDetails.status.ToString();
                                var oldStatus = transaction.Status;

                                var newStatus = razorpayStatus switch
                                {
                                    "captured" => "Success",
                                    "failed" => "Failed",
                                    "created" => "Pending",
                                    "authorized" => "Authorized",
                                    "refunded" => "Refunded",
                                    _ => transaction.Status
                                };

                                if (newStatus != oldStatus)
                                {
                                    transaction.Status = newStatus;
                                    transaction.UpdatedOn = IndianTime.Now;

                                    // Activate subscription
                                    if (razorpayStatus == "captured" &&
                                        oldStatus != "Success" &&
                                        transaction.Subscription != null)
                                    {
                                        await ActivateSubscriptionOnCapture(transaction.Subscription);
                                    }

                                    updated++;

                                    _logger.LogInformation(
                                        $"Tenant {tenant.CompanyName}: Updated Tx {transaction.TransactionId} {oldStatus} ? {newStatus}");
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex,
                                    $"Tenant {tenant.CompanyName}: Failed for payment {transaction.RazorpayPaymentId}");
                            }
                        }

                        if (updated > 0)
                        {
                            await tenantDb.SaveChangesAsync();

                            _logger.LogInformation(
                                $"Tenant {tenant.CompanyName}: Sync completed. Updated {updated} transactions.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            $"Tenant {tenant.CompanyName}: Critical error in payment sync");
                    }
                });
        }
        private async Task ActivateSubscriptionOnCapture(PartnerSubscriptionModel subscription)
        {
            try
            {
                // Only activate if subscription is not already active
                if (subscription.Status != "Active")
                {
                    subscription.Status = "Active";
                    subscription.StartDate = IndianTime.Now;
                    subscription.UpdatedOn = IndianTime.Now;

                    _logger.LogInformation($"Activated subscription {subscription.SubscriptionId} on payment capture via background sync");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error activating subscription {subscription.SubscriptionId}");
            }
        }
    }
}