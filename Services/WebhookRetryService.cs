using CRM.Helpers;
using CRM.Models;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace CRM.Services
{
    /// <summary>
    /// P0-I1: Background service for retrying failed webhooks with exponential backoff
    /// </summary>
    public class WebhookRetryService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<WebhookRetryService> _logger;
        private readonly HttpClient _httpClient;
        private const int CHECK_INTERVAL_MINUTES = 5;

        public WebhookRetryService(IServiceProvider serviceProvider, ILogger<WebhookRetryService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("WebhookRetryService started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessWebhookRetries(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing webhook retries");
                }

                // Wait before next check
                await Task.Delay(TimeSpan.FromMinutes(CHECK_INTERVAL_MINUTES), stoppingToken);
            }
        }

        private async Task ProcessWebhookRetries(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Get pending retries that are due for retry
            var now = IndianTime.Now;
            var retries = await context.WebhookRetryQueue
                .Where(w => (w.Status == "Pending" || w.Status == "Failed") && 
                           (w.NextRetryAt == null || w.NextRetryAt <= now) &&
                           w.RetryCount < w.MaxRetries)
                .Take(50) // Process 50 at a time
                .ToListAsync(stoppingToken);

            _logger.LogInformation($"Found {retries.Count} webhooks ready for retry");

            foreach (var retry in retries)
            {
                if (stoppingToken.IsCancellationRequested) break;

                await RetryWebhook(context, retry, stoppingToken);
            }

            // Clean up old successful records (older than 7 days)
            var cleanupDate = IndianTime.Now.AddDays(-7);
            var oldRecords = await context.WebhookRetryQueue
                .Where(w => w.Status == "Success" && w.ProcessedOn < cleanupDate)
                .ToListAsync(stoppingToken);

            if (oldRecords.Any())
            {
                context.WebhookRetryQueue.RemoveRange(oldRecords);
                await context.SaveChangesAsync(stoppingToken);
                _logger.LogInformation($"Cleaned up {oldRecords.Count} old webhook records");
            }
        }

        private async Task RetryWebhook(AppDbContext context, WebhookRetryQueueModel retry, CancellationToken stoppingToken)
        {
            try
            {
                retry.Status = "Processing";
                retry.RetryCount++;
                context.WebhookRetryQueue.Update(retry);
                await context.SaveChangesAsync(stoppingToken);

                _logger.LogInformation($"Retrying webhook {retry.WebhookEventId} (Attempt {retry.RetryCount}/{retry.MaxRetries})");

                // Send webhook
                var content = new StringContent(retry.PayloadJson, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(retry.Endpoint, content, stoppingToken);

                if (response.IsSuccessStatusCode)
                {
                    // Success!
                    retry.Status = "Success";
                    retry.ProcessedOn = IndianTime.Now;
                    retry.NextRetryAt = null;
                    retry.LastError = null;
                    
                    _logger.LogInformation($"Webhook {retry.WebhookEventId} delivered successfully");
                }
                else
                {
                    // Failed - schedule next retry with exponential backoff
                    var responseBody = await response.Content.ReadAsStringAsync(stoppingToken);
                    retry.LastError = $"HTTP {response.StatusCode}: {responseBody}";
                    
                    if (retry.RetryCount >= retry.MaxRetries)
                    {
                        retry.Status = "Failed";
                        retry.ProcessedOn = IndianTime.Now;
                        _logger.LogWarning($"Webhook {retry.WebhookEventId} failed permanently after {retry.MaxRetries} attempts");
                    }
                    else
                    {
                        retry.Status = "Pending";
                        // Exponential backoff: 1min, 2min, 4min, 8min, 16min, etc.
                        var delayMinutes = Math.Pow(2, retry.RetryCount - 1);
                        retry.NextRetryAt = IndianTime.Now.AddMinutes(delayMinutes);
                        
                        _logger.LogWarning($"Webhook {retry.WebhookEventId} failed, will retry in {delayMinutes} minutes");
                    }
                }

                // Persist the final processing result (ReplaceOne via Update)
                context.WebhookRetryQueue.Update(retry);
                await context.SaveChangesAsync(stoppingToken);
            }
            catch (HttpRequestException ex)
            {
                // Network error
                retry.LastError = $"Network error: {ex.Message}";
                
                if (retry.RetryCount >= retry.MaxRetries)
                {
                    retry.Status = "Failed";
                    retry.ProcessedOn = IndianTime.Now;
                }
                else
                {
                    retry.Status = "Pending";
                    var delayMinutes = Math.Pow(2, retry.RetryCount - 1);
                    retry.NextRetryAt = IndianTime.Now.AddMinutes(delayMinutes);
                }

                context.WebhookRetryQueue.Update(retry);
                await context.SaveChangesAsync(stoppingToken);
                _logger.LogError(ex, $"Network error retrying webhook {retry.WebhookEventId}");
            }
            catch (Exception ex)
            {
                // Unexpected error
                retry.Status = "Failed";
                retry.LastError = $"Unexpected error: {ex.Message}";
                retry.ProcessedOn = IndianTime.Now;
                
                context.WebhookRetryQueue.Update(retry);
                await context.SaveChangesAsync(stoppingToken);
                _logger.LogError(ex, $"Unexpected error retrying webhook {retry.WebhookEventId}");
            }
        }

        public override void Dispose()
        {
            _httpClient?.Dispose();
            base.Dispose();
        }
    }
}
