using CRM.Helpers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net;
using CRM.Models;

namespace CRM.Services
{
    public class RazorpayService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly AppDbContext _context;
        private readonly ILogger<RazorpayService> _logger;
        private string _keyId;
        private string _keySecret;
        private string _webhookSecret;
        private string _credentialSource = "unknown";

        public RazorpayService(HttpClient httpClient, IConfiguration configuration, AppDbContext context, ILogger<RazorpayService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _context = context;
            _logger = logger;
            
            // Initialize credentials (will be loaded when needed)
            LoadCredentials();
        }

        private void LoadCredentials()
        {
            // Always clear previous header so we never reuse stale credentials.
            _httpClient.DefaultRequestHeaders.Authorization = null;
            _credentialSource = "unknown";

            try
            {
                // Try to get from database first
                var gateway = _context.PaymentGateways
                    .Where(g => g.GatewayName == "Razorpay" && g.IsActive)
                    .OrderByDescending(g => g.UpdatedOn ?? g.CreatedOn)
                    .FirstOrDefault();
                
                if (gateway != null)
                {
                    _keyId = (gateway.KeyId ?? "").Trim();
                    _keySecret = (gateway.KeySecret ?? "").Trim();
                    _webhookSecret = (gateway.WebhookSecret ?? "").Trim();
                    _credentialSource = "database";
                }

                // Fallback to appsettings if DB credentials are missing/empty.
                if (string.IsNullOrWhiteSpace(_keyId) || string.IsNullOrWhiteSpace(_keySecret))
                {
                    _keyId = (_configuration["Razorpay:KeyId"] ?? "").Trim();
                    _keySecret = (_configuration["Razorpay:KeySecret"] ?? "").Trim();
                    _webhookSecret = (_configuration["Razorpay:WebhookSecret"] ?? "").Trim();
                    _credentialSource = "appsettings";
                }

                // Set HTTP client authorization if credentials exist
                if (!string.IsNullOrEmpty(_keyId) && !string.IsNullOrEmpty(_keySecret))
                {
                    var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_keyId}:{_keySecret}"));
                    _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
                }
            }
            catch
            {
                // If database fails, use appsettings.json
                _keyId = (_configuration["Razorpay:KeyId"] ?? "").Trim();
                _keySecret = (_configuration["Razorpay:KeySecret"] ?? "").Trim();
                _webhookSecret = (_configuration["Razorpay:WebhookSecret"] ?? "").Trim();
                _credentialSource = "appsettings-fallback";

                if (!string.IsNullOrEmpty(_keyId) && !string.IsNullOrEmpty(_keySecret))
                {
                    var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_keyId}:{_keySecret}"));
                    _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
                }
            }
        }

        private static string MaskKeyId(string? keyId)
        {
            if (string.IsNullOrWhiteSpace(keyId))
            {
                return "(empty)";
            }

            var value = keyId.Trim();
            if (value.Length <= 8)
            {
                return value;
            }

            return $"{value.Substring(0, 8)}...";
        }

        public string GetKeyId()
        {
            // Reload credentials to get latest from database
            LoadCredentials();
            return _keyId;
        }

        public async Task<string> CreateOrderAsync(decimal amount, string currency = "INR", string receipt = null)
        {
            // Ensure we have latest credentials
            LoadCredentials();
            
            if (string.IsNullOrEmpty(_keyId) || string.IsNullOrEmpty(_keySecret))
            {
                throw new Exception("Razorpay credentials not configured. Please configure in Financial Settings.");
            }

            var orderData = new
            {
                amount = (int)(amount * 100), // Convert to paise
                currency = currency,
                receipt = receipt ?? $"order_{IndianTime.Now.Ticks}"
            };

            var json = JsonSerializer.Serialize(orderData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://api.razorpay.com/v1/orders", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var orderResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
                return orderResponse.GetProperty("id").GetString();
            }

            throw new Exception($"Failed to create Razorpay order: {responseContent}");
        }

        public bool VerifyPaymentSignature(string paymentId, string orderId, string signature)
        {
            // Ensure we have latest credentials
            LoadCredentials();
            
            // For test mode, always return true
            if (_keyId.StartsWith("rzp_test_"))
            {
                return true;
            }

            var payload = $"{orderId}|{paymentId}";
            var expectedSignature = ComputeHmacSha256(payload, _keySecret);
            return expectedSignature == signature;
        }

        public bool VerifyWebhookSignature(string payload, string signature)
        {
            // Ensure we have latest credentials
            LoadCredentials();
            
            if (string.IsNullOrEmpty(_webhookSecret))
            {
                _logger.LogWarning("Razorpay webhook secret is not configured. Webhook signature verification will fail. Go to SuperAdmin > Payment Config or add WebhookSecret to appsettings.json to enable webhook callbacks.");
                return false; // Cannot verify without webhook secret
            }
            
            var expectedSignature = ComputeHmacSha256(payload, _webhookSecret);
            return expectedSignature == signature;
        }

        private string ComputeHmacSha256(string data, string key)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Convert.ToHexString(hash).ToLower();
        }

        /// <summary>
        /// Create a refund for a payment
        /// </summary>
        /// <param name="paymentId">Razorpay payment ID (e.g., pay_xxxxx)</param>
        /// <param name="amount">Amount to refund in rupees (full or partial)</param>
        /// <param name="notes">Optional notes for the refund</param>
        /// <returns>Refund ID if successful</returns>
        public async Task<(bool success, string refundId, string message)> CreateRefundAsync(string paymentId, decimal? amount = null, string notes = null)
        {
            try
            {
                // Ensure we have latest credentials
                LoadCredentials();
                
                if (string.IsNullOrEmpty(_keyId) || string.IsNullOrEmpty(_keySecret))
                {
                    return (false, "", "Razorpay credentials not configured. Please configure in Financial Settings.");
                }

                var refundData = new Dictionary<string, object>();
                
                if (amount.HasValue)
                {
                    refundData["amount"] = (int)(amount.Value * 100); // Convert to paise
                }
                
                if (!string.IsNullOrEmpty(notes))
                {
                    refundData["notes"] = new { reason = notes };
                }

                var json = JsonSerializer.Serialize(refundData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"https://api.razorpay.com/v1/payments/{paymentId}/refund", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var refundResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    var refundId = refundResponse.GetProperty("id").GetString();
                    var status = refundResponse.GetProperty("status").GetString();
                    
                    return (true, refundId ?? "", $"Refund {status}. Refund will be processed to original payment method within 5-7 business days.");
                }

                return (false, "", $"Razorpay error: {responseContent}");
            }
            catch (Exception ex)
            {
                return (false, "", $"Failed to create refund: {ex.Message}");
            }
        }

        /// <summary>
        /// Fetch payment details from Razorpay
        /// </summary>
        public async Task<(bool success, JsonElement? payment)> FetchPaymentAsync(string paymentId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"https://api.razorpay.com/v1/payments/{paymentId}");
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var payment = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    return (true, payment);
                }

                return (false, null);
            }
            catch
            {
                return (false, null);
            }
        }

        /// <summary>
        /// Get all payments from Razorpay API
        /// </summary>
        public async Task<List<dynamic>> GetAllPaymentsAsync(int count = 100)
        {
            try
            {
                LoadCredentials();
                
                if (string.IsNullOrEmpty(_keyId) || string.IsNullOrEmpty(_keySecret))
                {
                    throw new Exception("Razorpay credentials not configured");
                }

                // Configure HttpClient with timeout and retry
                _httpClient.Timeout = TimeSpan.FromSeconds(30);
                
                // Use smaller count and add skip parameter for pagination
                var url = $"https://api.razorpay.com/v1/payments?count={Math.Min(count, 100)}&skip=0";
                
                var response = await _httpClient.GetAsync(url);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var paymentsResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    
                    if (!paymentsResponse.TryGetProperty("items", out var itemsProperty))
                    {
                        return new List<dynamic>();
                    }
                    
                    var payments = new List<dynamic>();
                    foreach (var item in itemsProperty.EnumerateArray())
                    {
                        var payment = new
                        {
                            id = item.TryGetProperty("id", out var id) ? id.GetString() : "",
                            amount = item.TryGetProperty("amount", out var amount) ? amount.GetInt64() : 0,
                            status = item.TryGetProperty("status", out var status) ? status.GetString() : "",
                            method = item.TryGetProperty("method", out var method) ? method.GetString() : "",
                            email = item.TryGetProperty("email", out var email) ? email.GetString() : "",
                            created_at = item.TryGetProperty("created_at", out var created) ? created.GetInt64() : 0
                        };
                        payments.Add(payment);
                    }
                    
                    return payments;
                }
                else
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        throw new Exception($"Razorpay authentication failed (401). Source={_credentialSource}, KeyId={MaskKeyId(_keyId)}. Verify Key ID/Key Secret pair in Financial > Payment Gateways, ensure both are from the same mode (test/live), and remove any extra spaces.");
                    }

                    throw new Exception($"Razorpay API error: {response.StatusCode} - {responseContent}");
                }
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Network error connecting to Razorpay API. Please check your internet connection. Details: {ex.Message}");
            }
            catch (TaskCanceledException ex)
            {
                throw new Exception($"Request timeout connecting to Razorpay API. Please try again. Details: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to fetch payments: {ex.Message}");
            }
        }

        /// <summary>
        /// Get payment details from Razorpay API
        /// </summary>
        public async Task<dynamic> GetPaymentDetailsAsync(string paymentId)
        {
            try
            {
                LoadCredentials();
                
                var response = await _httpClient.GetAsync($"https://api.razorpay.com/v1/payments/{paymentId}");
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var payment = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    return new
                    {
                        id = payment.TryGetProperty("id", out var id) ? id.GetString() : "",
                        amount = payment.TryGetProperty("amount", out var amount) ? amount.GetInt64() : 0,
                        status = payment.TryGetProperty("status", out var status) ? status.GetString() : "",
                        method = payment.TryGetProperty("method", out var method) ? method.GetString() : "",
                        currency = payment.TryGetProperty("currency", out var currency) ? currency.GetString() : "",
                        email = payment.TryGetProperty("email", out var email) ? email.GetString() : "",
                        contact = payment.TryGetProperty("contact", out var contact) ? contact.GetString() : "",
                        created_at = payment.TryGetProperty("created_at", out var created) ? created.GetInt64() : 0,
                        card = payment.TryGetProperty("card", out var card) ? new
                        {
                            network = card.TryGetProperty("network", out var network) ? network.GetString() : "",
                            last4 = card.TryGetProperty("last4", out var last4) ? last4.GetString() : "",
                            type = card.TryGetProperty("type", out var type) ? type.GetString() : ""
                        } : null
                    };
                }

                throw new Exception($"Failed to fetch payment details: {responseContent}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Error fetching payment details: {ex.Message}");
            }
        }

        /// <summary>
        /// Get refunds for a payment from Razorpay API
        /// </summary>
        public async Task<List<dynamic>> GetRefundsAsync(string paymentId)
        {
            try
            {
                LoadCredentials();
                
                var response = await _httpClient.GetAsync($"https://api.razorpay.com/v1/payments/{paymentId}/refunds");
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var refundsResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    var items = refundsResponse.GetProperty("items");
                    
                    var refunds = new List<dynamic>();
                    foreach (var item in items.EnumerateArray())
                    {
                        var refund = new
                        {
                            id = item.TryGetProperty("id", out var id) ? id.GetString() : "",
                            amount = item.TryGetProperty("amount", out var amount) ? amount.GetInt64() : 0,
                            status = item.TryGetProperty("status", out var status) ? status.GetString() : "",
                            created_at = item.TryGetProperty("created_at", out var created) ? created.GetInt64() : 0
                        };
                        refunds.Add(refund);
                    }
                    
                    return refunds;
                }

                return new List<dynamic>();
            }
            catch
            {
                return new List<dynamic>();
            }
        }

        /// <summary>
        /// Get all refunds from Razorpay API
        /// </summary>
        public async Task<List<dynamic>> GetAllRefundsAsync(int count = 100)
        {
            try
            {
                LoadCredentials();
                
                if (string.IsNullOrEmpty(_keyId) || string.IsNullOrEmpty(_keySecret))
                {
                    throw new Exception("Razorpay credentials not configured");
                }

                var response = await _httpClient.GetAsync($"https://api.razorpay.com/v1/refunds?count={count}");
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var refundsResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    var items = refundsResponse.GetProperty("items");
                    
                    var refunds = new List<dynamic>();
                    foreach (var item in items.EnumerateArray())
                    {
                        var refund = new
                        {
                            id = item.TryGetProperty("id", out var id) ? id.GetString() : "",
                            amount = item.TryGetProperty("amount", out var amount) ? amount.GetInt64() : 0,
                            status = item.TryGetProperty("status", out var status) ? status.GetString() : "",
                            payment_id = item.TryGetProperty("payment_id", out var paymentId) ? paymentId.GetString() : "",
                            created_at = item.TryGetProperty("created_at", out var created) ? created.GetInt64() : 0
                        };
                        refunds.Add(refund);
                    }
                    
                    return refunds;
                }

                return new List<dynamic>();
            }
            catch
            {
                return new List<dynamic>();
            }
        }
    }
}