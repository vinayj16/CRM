using CRM.Helpers;

namespace CRM.Services
{
    public class MonthlyPayoutBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MonthlyPayoutBackgroundService> _logger;

        public MonthlyPayoutBackgroundService(IServiceProvider serviceProvider, ILogger<MonthlyPayoutBackgroundService> logger)
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
                    var now = IndianTime.Now;

                    // Run on the 1st of every month at 2 AM
                    if (now.Day == 1 && now.Hour == 2 && now.Minute == 0)
                    {
                        _logger.LogInformation("Starting monthly payout processing...");

                        //using var scope = _serviceProvider.CreateScope();
                        //var payoutService = scope.ServiceProvider.GetRequiredService<PayoutService>();
                        //var payslipService = scope.ServiceProvider.GetRequiredService<PayslipService>();

                        // Process previous month
                        var previousMonth = now.AddMonths(-1);
                        var month = previousMonth.ToString("MMMM");
                        var year = previousMonth.Year;


                        await TenantBackgroundHelper.ForEachTenantAsync(_serviceProvider, _logger, async (tenantDb, tenant, scopeProvider) =>
                        {
                            _logger.LogInformation($"Processing monthly payouts for tenant: {tenant.CompanyName}");

                            var payoutService = scopeProvider.GetRequiredService<PayoutService>();
                            var payslipService = scopeProvider.GetRequiredService<PayslipService>();

                            await payoutService.ProcessMonthlyPayouts(month, year);
                            await payslipService.GenerateMonthlyPayslips(month, year);

                            _logger.LogInformation($"Monthly payout completed for tenant: {tenant.CompanyName}");
                        });

                        _logger.LogInformation($"Monthly payout processing completed for all tenants - {month} {year}");

                        // Wait for 1 hour to avoid running multiple times
                        await Task.Delay(TimeSpan.FromHours(1), stoppingToken);

                    }

                    // Check every minute
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in monthly payout background service");
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }
        }
    }
}