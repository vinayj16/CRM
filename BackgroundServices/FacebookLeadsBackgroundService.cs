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
            _logger.LogInformation($"Facebook Background Service STARTED at {IndianTime.Now:yyyy-MM-dd HH:mm:ss}");
            
            while (!stoppingToken.IsCancellationRequested)
            {
                var startTime = IndianTime.Now;
                _logger.LogInformation($"Facebook API call TRIGGERED at {startTime:yyyy-MM-dd HH:mm:ss}");
                
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
                    _logger.LogInformation($"Calling API: {apiUrl}");
                    
                    var response = await httpClient.GetAsync(apiUrl, stoppingToken);
                    _logger.LogInformation($"API Response Status: {response.StatusCode}");
                    
                    var content = await response.Content.ReadAsStringAsync(stoppingToken);
                    
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
                    _logger.LogError($"Facebook service ERROR at {IndianTime.Now:yyyy-MM-dd HH:mm:ss}: {ex.Message}");
                }

                var nextRun = IndianTime.Now.AddMinutes(2);
                _logger.LogInformation($"Facebook service SLEEPING for 2 minutes. Next run at {nextRun:yyyy-MM-dd HH:mm:ss}");
                
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