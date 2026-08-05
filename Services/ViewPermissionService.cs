////using System.Security.Claims;

//namespace CRM.Services
//{
//    public class ViewPermissionService
//    {
//        private readonly AppDbContext _context;
//        private readonly IHttpContextAccessor _httpContextAccessor;

//        public ViewPermissionService(AppDbContext context, IHttpContextAccessor httpContextAccessor)
//        {
//            _context = context;
//            _httpContextAccessor = httpContextAccessor;
//        }

//        public bool HasPermission(string controller, string action, string permission)
//        {
//            var user = _httpContextAccessor.HttpContext?.User;
//            if (user == null) return false;

//            var role = user.FindFirst(ClaimTypes.Role)?.Value;
//            if (string.IsNullOrEmpty(role)) return false;

//            // Admin has all permissions
//            if (role == "Admin") return true;

//            // Check if this is a custom role with AllowedModules
//            var standardRoles = new[] { "Admin", "Partner", "Agent", "Sales" };
//            if (!standardRoles.Contains(role))
//            {
//                var rolePermission = _context.RolePermissions.OrderByDescending(r => r.CreatedAt).FirstOrDefault(r => r.RoleName == role);
//                if (rolePermission != null && !string.IsNullOrEmpty(rolePermission.AllowedModules))
//                {
//                    // Custom role with AllowedModules gets full permissions (same as Admin/Partner who created it)
//                    return true;
//                }
//            }

//            // Get user's channel partner ID
//            var channelPartnerIdClaim = user.FindFirst("ChannelPartnerId")?.Value;
//            int? userChannelPartnerId = null;
//            if (!string.IsNullOrEmpty(channelPartnerIdClaim) && int.TryParse(channelPartnerIdClaim, out int partnerId))
//            {
//                userChannelPartnerId = partnerId;
//            }

//            // For partner agents, check both partner-specific AND global permissions
//            if (userChannelPartnerId.HasValue)
//            {
//                return _context.RolePagePermissions
//                    .Include(rpp => rpp.Page)
//                    .Include(rpp => rpp.Permission)
//                    .Any(rpp => rpp.RoleName == role &&
//                               rpp.Page.Controller == controller &&
//                               rpp.Page.Action == action &&
//                               rpp.Permission.PermissionName == permission &&
//                               rpp.IsGranted &&
//                               (rpp.ChannelPartnerId == userChannelPartnerId || rpp.ChannelPartnerId == null));
//            }

//            // For admin agents, check admin permissions (ChannelPartnerId = null)
//            return _context.RolePagePermissions
//                .Include(rpp => rpp.Page)
//                .Include(rpp => rpp.Permission)
//                .Any(rpp => rpp.RoleName == role &&
//                           rpp.Page.Controller == controller &&
//                           rpp.Page.Action == action &&
//                           rpp.Permission.PermissionName == permission &&
//                           rpp.IsGranted &&
//                           rpp.ChannelPartnerId == null);
//        }
//    }
//}

using System.Security.Claims;

namespace CRM.Services
{
    public class ViewPermissionService
    {
        private readonly AppDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ViewPermissionService(AppDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        public bool HasPermission(string controller, string action, string permission)
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null) return false;

            var role = user.FindFirst(ClaimTypes.Role)?.Value;
            if (string.IsNullOrEmpty(role)) return false;

            // Admin has all permissions
            if (role == "Admin") return true;

            // Check if this is a custom role with AllowedModules
            var standardRoles = new[] { "Admin", "Partner", "Agent", "Sales" };
            if (!standardRoles.Contains(role))
            {
                var rolePermission = _context.RolePermissions
                    .OrderByDescending(r => r.CreatedAt)
                    .FirstOrDefault(r => r.RoleName == role);
                if (rolePermission != null && !string.IsNullOrEmpty(rolePermission.AllowedModules))
                {
                    // Custom role with AllowedModules gets full permissions (same as Admin/Partner who created it)
                    return true;
                }
            }

            // Get user's channel partner ID
            var channelPartnerIdClaim = user.FindFirst("ChannelPartnerId")?.Value;
            int? userChannelPartnerId = null;
            if (!string.IsNullOrEmpty(channelPartnerIdClaim) && int.TryParse(channelPartnerIdClaim, out int partnerId))
            {
                userChannelPartnerId = partnerId;
            }

            // Look up PageIds matching this controller+action
            var pageIds = _context.Pages
                .Where(p => p.Controller == controller && p.Action == action && p.IsActive)
                .Select(p => p.PageId)
                .ToList();
            if (!pageIds.Any()) return false;

            // Look up PermissionId
            var permId = _context.Permissions
                .Where(p => p.PermissionName == permission && p.IsActive)
                .Select(p => p.PermissionId)
                .FirstOrDefault();
            if (permId == 0) return false;

            // For partner agents, check both partner-specific AND global permissions
            if (userChannelPartnerId.HasValue)
            {
                return _context.RolePagePermissions
                    .Any(rpp => rpp.RoleName == role &&
                                pageIds.Contains(rpp.PageId) &&
                                rpp.PermissionId == permId &&
                                rpp.IsGranted &&
                                (rpp.ChannelPartnerId == userChannelPartnerId || rpp.ChannelPartnerId == null));
            }

            // For admin agents, check admin permissions (ChannelPartnerId = null)
            return _context.RolePagePermissions
                .Any(rpp => rpp.RoleName == role &&
                            pageIds.Contains(rpp.PageId) &&
                            rpp.PermissionId == permId &&
                            rpp.IsGranted &&
                            rpp.ChannelPartnerId == null);
        }
    }
}