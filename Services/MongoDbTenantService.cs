using CRM.MasterDb.Models;
using CRM.Models.MongoDb;
using MongoDB.Driver;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using CRM.Helpers;

namespace CRM.Services
{
    public interface ITenantService
    {
        int GetTenantId();
        string GetTenantName();
        bool IsResolved();
        Task<TenantDocument?> GetTenantAsync();
        Task<List<TenantDocument>> GetTenantsForEmailAsync(string email);
    }

    public class MongoDbTenantService : ITenantService
    {
        private readonly IHttpContextAccessor _accessor;
        private readonly MongoDbContext _context;
        private readonly ILogger<MongoDbTenantService> _logger;
        private TenantDocument? _tenant;
        private bool _resolved = false;

        public MongoDbTenantService(IHttpContextAccessor accessor, MongoDbContext context, ILogger<MongoDbTenantService> logger)
        {
            _accessor = accessor;
            _context = context;
            _logger = logger;
        }

        public int GetTenantId()
        {
            ResolveIfNeeded();
            return _tenant?.TenantId ?? 0;
        }

        public string GetTenantName()
        {
            ResolveIfNeeded();
            return _tenant?.CompanyName ?? string.Empty;
        }

        public bool IsResolved()
        {
            ResolveIfNeeded();
            return _resolved;
        }

        public async Task<TenantDocument?> GetTenantAsync()
        {
            ResolveIfNeeded();
            return await Task.FromResult(_tenant);
        }

        private void ResolveIfNeeded()
        {
            if (_resolved) return;

            // Priority 1: JWT claim (already logged in - cookie auth)
            var tenantIdClaim = _accessor.HttpContext?.User?.FindFirst("TenantId")?.Value;
            if (int.TryParse(tenantIdClaim, out int tenantId))
            {
                _tenant = _context.Tenants.Find(t => t.TenantId == tenantId && t.IsActive && !t.IsSuspended)
                    .FirstOrDefault();
                if (_tenant != null)
                {
                    _resolved = true;
                    return;
                }
            }

            // Priority 1b: Bearer token (mobile / API requests) - the API sends
            // the tenant in the JWT Authorization header, which is NOT loaded into
            // HttpContext.User (only cookie auth is registered). Without this, every
            // mobile-created resource would be stamped TenantId=0 and become orphaned.
            var authHeader = _accessor.HttpContext?.Request?.Headers["Authorization"].ToString();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var cfg = _accessor.HttpContext?.RequestServices?.GetService<IConfiguration>();
                var tokenUser = JwtHelper.ValidateToken(authHeader, cfg);
                if (tokenUser?.TenantId is int bearerTenantId && bearerTenantId > 0)
                {
                    _tenant = _context.Tenants.Find(t => t.TenantId == bearerTenantId && t.IsActive && !t.IsSuspended)
                        .FirstOrDefault();
                    if (_tenant != null)
                    {
                        _resolved = true;
                        return;
                    }
                }
            }

            // Priority 2: Subdomain from URL
            var host = _accessor.HttpContext?.Request.Host.Host.ToLower() ?? "";
            string? subdomain = null;

            if (host.Contains(".localhost"))
            {
                subdomain = host.Split('.')[0];
                if (subdomain == "localhost") subdomain = null;
            }
            else
            {
                var parts = host.Split('.');
                if (parts.Length >= 3)
                {
                    subdomain = parts[0];
                    if (subdomain == "www" || subdomain == "mail" || subdomain == "api")
                        subdomain = null;
                }
            }

            if (!string.IsNullOrEmpty(subdomain))
            {
                _tenant = _context.Tenants.Find(t => t.Subdomain == subdomain && t.IsActive && !t.IsSuspended)
                    .FirstOrDefault();
                if (_tenant != null)
                {
                    _resolved = true;
                }
            }
        }

        public async Task<List<TenantDocument>> GetTenantsForEmailAsync(string email)
        {
            var emailEntries = await _context.Database.GetCollection<EmailDirectoryModel>("email_directory")
                .Find(e => e.Email == email)
                .ToListAsync();

            var tenantIds = emailEntries.Select(e => e.TenantId).ToList();
            if (!tenantIds.Any()) return new List<TenantDocument>();

            return await _context.Tenants
                .Find(t => tenantIds.Contains(t.TenantId) && t.IsActive && !t.IsSuspended)
                .ToListAsync();
        }

        /// <summary>
        /// Sync method required by legacy code. Prefer async version.
        /// </summary>
        public TenantDocument? GetTenantByTenantId(int tenantId)
        {
            return _context.Tenants.Find(t => t.TenantId == tenantId).FirstOrDefault();
        }
    }

    /// <summary>
    /// Static tenant service for background services with no HTTP context.
    /// </summary>
    public class StaticTenantService : ITenantService
    {
        private readonly int _tenantId;
        private readonly string _tenantName;

        public StaticTenantService(int tenantId, string tenantName = "")
        {
            _tenantId = tenantId;
            _tenantName = tenantName;
        }

        public int GetTenantId() => _tenantId;
        public string GetTenantName() => _tenantName;
        public bool IsResolved() => true;
        public Task<TenantDocument?> GetTenantAsync() => Task.FromResult<TenantDocument?>(null);
        public Task<List<TenantDocument>> GetTenantsForEmailAsync(string email) => Task.FromResult(new List<TenantDocument>());
    }
}
