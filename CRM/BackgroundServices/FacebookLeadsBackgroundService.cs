using CRM.Helpers;
using CRM.Services;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace CRM.BackgroundServices
{
    public class FacebookLeadsBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<FacebookLeadsBackgroundService> _logger;
        private readonly IServer _server;

        public FacebookLeadsBackgroundService(IServiceProvider serviceProvider, ILogger<FacebookLeadsBackgroundService> logger, IServer server)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _server = server;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine("=== FACEBOOK BACKGROUND SERVICE STARTING ===");
            _logger.LogInformation($"Facebook Background Service STARTED at {IndianTime.Now:yyyy-MM-dd HH:mm:ss}");
            
            // Create log file in project root
            var logPath = Path.Combine(Directory.GetCurrentDirectory(), "facebook_service_log.txt");
            File.WriteAllText(logPath, $"Facebook Service Started at {IndianTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            
            while (!stoppingToken.IsCancellationRequested)
            {
                var startTime = IndianTime.Now;
                Console.WriteLine($"\n=== FACEBOOK SERVICE CYCLE ===");
                Console.WriteLine($"Current Time: {startTime:yyyy-MM-dd HH:mm:ss}");
                _logger.LogInformation($"Facebook API call TRIGGERED at {startTime:yyyy-MM-dd HH:mm:ss}");
                
                // Write to file for easy verification
                File.AppendAllText(logPath, $"{startTime:yyyy-MM-dd HH:mm:ss} - Facebook service triggered\n");
                
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    
                    // Configure HttpClient to bypass SSL validation for localhost
                    var handler = new HttpClientHandler()
                    {
                        ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                    };
                    using var httpClient = new HttpClient(handler);
                    
                    var apiUrl = GetBaseUrl() + "/api/facebook/fetch-leads";
                    Console.WriteLine($"Calling API: {apiUrl}");
                    File.AppendAllText(logPath, $"{IndianTime.Now:yyyy-MM-dd HH:mm:ss} - Calling API: {apiUrl}\n");
                    
                    var response = await httpClient.GetAsync(apiUrl, stoppingToken);
                    
                    Console.WriteLine($"API Response Status: {response.StatusCode}");
                    File.AppendAllText(logPath, $"{IndianTime.Now:yyyy-MM-dd HH:mm:ss} - Status: {response.StatusCode}\n");
                    
                    var content = await response.Content.ReadAsStringAsync(stoppingToken);
                    Console.WriteLine($"API Response: {content}");
                    File.AppendAllText(logPath, $"{IndianTime.Now:yyyy-MM-dd HH:mm:ss} - Response: {content}\n");
                    
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation($"Facebook leads fetch COMPLETED at {IndianTime.Now:yyyy-MM-dd HH:mm:ss}: {content}");
                    }
                    else
                    {
                        _logger.LogWarning($"Facebook leads fetch FAILED at {IndianTime.Now:yyyy-MM-dd HH:mm:ss}: {response.StatusCode} - {content}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"API ERROR: {ex.Message}");
                    File.AppendAllText(logPath, $"{IndianTime.Now:yyyy-MM-dd HH:mm:ss} - ERROR: {ex.Message}\n");
                    _logger.LogError($"Facebook service ERROR at {IndianTime.Now:yyyy-MM-dd HH:mm:ss}: {ex.Message}");
                }

                var nextRun = IndianTime.Now.AddMinutes(2);
                Console.WriteLine($"Next run scheduled at: {nextRun:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"=== SLEEPING FOR 2 MINUTES ===");
                _logger.LogInformation($"Facebook service SLEEPING for 2 minutes. Next run at {nextRun:yyyy-MM-dd HH:mm:ss}");
                
                File.AppendAllText(logPath, $"{IndianTime.Now:yyyy-MM-dd HH:mm:ss} - Sleeping until {nextRun:yyyy-MM-dd HH:mm:ss}\n");
                
                // Wait 2 minutes
                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            }
        }

        private string GetBaseUrl()
        {
            var addresses = _server.Features.Get<IServerAddressesFeature>()?.Addresses;
            if (addresses != null && addresses.Count > 0)
            {
                return addresses.First().TrimEnd('/');
            }
            return "http://localhost:5000";
        }
    }
}