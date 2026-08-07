using CRM.Models;

namespace CRM.Services
{
    public class PermissionService
    {
        private readonly AppDbContext _context;

        public PermissionService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<ModuleModel>> GetModulesWithPagesAsync()
        {
            var modules = await _context.Modules
                .Where(m => m.IsActive)
                .OrderBy(m => m.SortOrder)
                .ToListAsync();

            var pages = await _context.Pages
                .Where(p => p.IsActive)
                .OrderBy(p => p.SortOrder)
                .ToListAsync();

            foreach (var module in modules)
            {
                module.Pages = pages.Where(p => p.ModuleId == module.ModuleId).ToList();
            }

            return modules.Where(m => m.Pages.Any()).ToList();
        }

        public async Task<List<PermissionModel>> GetPermissionsAsync()
        {
            return await _context.Permissions
                .Where(p => p.IsActive)
                .OrderBy(p => p.SortOrder)
                .ToListAsync();
        }

        public async Task<Dictionary<string, bool>> GetRolePermissionsAsync(string roleName, int pageId, int? channelPartnerId = null)
        {
            var allPermissions = await _context.Permissions
                .Where(p => p.IsActive)
                .ToListAsync();

            var grantedPermissionIds = await _context.RolePagePermissions
                .Where(rpp => rpp.RoleName == roleName && rpp.PageId == pageId && rpp.IsGranted && rpp.ChannelPartnerId == channelPartnerId)
                .Select(rpp => rpp.PermissionId)
                .ToListAsync();

            var result = new Dictionary<string, bool>();
            foreach (var permission in allPermissions)
            {
                result[permission.PermissionName] = grantedPermissionIds.Contains(permission.PermissionId);
            }

            return result;
        }

        public async Task SaveRolePermissionsAsync(string roleName, Dictionary<int, Dictionary<int, bool>> permissions, string createdBy, int? channelPartnerId)
        {
            var existingPermissions = await _context.RolePagePermissions
                .Where(rpp => rpp.RoleName == roleName && rpp.ChannelPartnerId == channelPartnerId)
                .ToListAsync();

            _context.RolePagePermissions.RemoveRange(existingPermissions);

            foreach (var pagePermissions in permissions)
            {
                int pageId = pagePermissions.Key;
                foreach (var permission in pagePermissions.Value)
                {
                    int permissionId = permission.Key;
                    bool isGranted = permission.Value;
                    if (isGranted)
                    {
                        _context.RolePagePermissions.Add(new RolePagePermissionModel
                        {
                            RoleName = roleName,
                            PageId = pageId,
                            PermissionId = permissionId,
                            IsGranted = true,
                            CreatedBy = createdBy,
                            ChannelPartnerId = channelPartnerId
                        });
                    }
                }
            }
            await _context.SaveChangesAsync();
        }

        public async Task<bool> HasPermissionAsync(string roleName, string controller, string action, string permissionName, int? userChannelPartnerId)
        {
            var standardRoles = new[] { "Admin", "Partner", "Agent", "Sales" };
            if (!standardRoles.Contains(roleName, StringComparer.OrdinalIgnoreCase))
            {
                var rolePermission = await _context.RolePermissions.OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(r => r.RoleName == roleName);
                if (rolePermission != null && !string.IsNullOrEmpty(rolePermission.AllowedModules))
                {
                    return true;
                }
            }

            var matchingPages = await _context.Pages
                .Where(p => p.Controller == controller && p.IsActive)
                .ToListAsync();

            var matchingPageIds = matchingPages.Select(p => p.PageId).ToList();
            if (!matchingPageIds.Any()) return false;

            // Standard roles that have a module matrix grant View access when the page's
            // module is in AllowedModules (mirrors sidebar visibility). Non-View permissions
            // still require explicit RolePagePermissions below.
            if (permissionName == "View")
            {
                var rolePermission = await _context.RolePermissions.OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(r => r.RoleName == roleName);
                if (rolePermission != null && !string.IsNullOrEmpty(rolePermission.AllowedModules)
                    && !rolePermission.AllowedModules.Equals("All", StringComparison.OrdinalIgnoreCase))
                {
                    var moduleIds = matchingPages.Select(p => p.ModuleId).Distinct().ToList();
                    if (moduleIds.Any())
                    {
                        var pageModules = await _context.Modules.Where(m => moduleIds.Contains(m.ModuleId) && m.IsActive).ToListAsync();
                        var matrix = rolePermission.AllowedModules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        bool inMatrix = pageModules.Any(m =>
                            matrix.Contains(m.ModuleName, StringComparer.OrdinalIgnoreCase) ||
                            matrix.Contains(m.DisplayName ?? string.Empty, StringComparer.OrdinalIgnoreCase));
                        if (inMatrix)
                        {
                            // Pages the sidebar hard-hides for these roles must stay restricted
                            bool sidebarHidden =
                                (controller == "WebhookLeads" && (roleName == "Sales" || roleName == "Agent")) ||
                                (controller == "Home" && action == "SalesOverview" && (roleName == "Sales" || roleName == "Agent")) ||
                                (controller == "Home" && action == "TeamDashboard") ||
                                (controller == "ManageUsers" && action == "PartnerApproval");
                            if (!sidebarHidden) return true;
                        }
                    }
                }
            }

            var permission = await _context.Permissions
                .FirstOrDefaultAsync(p => p.PermissionName == permissionName && p.IsActive);

            if (permission == null) return false;
            int permissionId = permission.PermissionId;

            if (controller == "Leads" && permissionName == "Create")
            {
                return await _context.RolePagePermissions.AnyAsync(rpp =>
                    rpp.RoleName == roleName && matchingPageIds.Contains(rpp.PageId) &&
                    rpp.PermissionId == permissionId && rpp.IsGranted &&
                    (rpp.ChannelPartnerId == userChannelPartnerId || rpp.ChannelPartnerId == null));
            }

            static bool RequiresControllerFallback(string name)
            {
                return name == "Create" || name == "Edit" || name == "Delete" || name == "Export" || name == "BulkUpload";
            }

            var exactPageId = matchingPages.FirstOrDefault(p => p.Action == action)?.PageId;

            if (userChannelPartnerId.HasValue)
            {
                if (exactPageId.HasValue)
                {
                    if (await _context.RolePagePermissions.AnyAsync(rpp =>
                        rpp.RoleName == roleName && rpp.PageId == exactPageId.Value &&
                        rpp.PermissionId == permissionId && rpp.IsGranted &&
                        rpp.ChannelPartnerId == userChannelPartnerId))
                        return true;
                }
                if (RequiresControllerFallback(permissionName))
                {
                    if (await _context.RolePagePermissions.AnyAsync(rpp =>
                        rpp.RoleName == roleName && matchingPageIds.Contains(rpp.PageId) &&
                        rpp.PermissionId == permissionId && rpp.IsGranted &&
                        rpp.ChannelPartnerId == userChannelPartnerId))
                        return true;
                }
            }

            if (exactPageId.HasValue)
            {
                if (await _context.RolePagePermissions.AnyAsync(rpp =>
                    rpp.RoleName == roleName && rpp.PageId == exactPageId.Value &&
                    rpp.PermissionId == permissionId && rpp.IsGranted &&
                    rpp.ChannelPartnerId == null))
                    return true;
            }

            if (RequiresControllerFallback(permissionName))
            {
                if (await _context.RolePagePermissions.AnyAsync(rpp =>
                    rpp.RoleName == roleName && matchingPageIds.Contains(rpp.PageId) &&
                    rpp.PermissionId == permissionId && rpp.IsGranted &&
                    rpp.ChannelPartnerId == null))
                    return true;
            }

            return false;
        }

        public bool HasSidebarPermission(string roleName, string controller, string action, string permissionName, int? userChannelPartnerId)
        {
            if (string.IsNullOrEmpty(roleName)) return false;
            if (roleName == "Admin" || roleName == "SuperAdmin") return true;

            var standardRoles = new[] { "Admin", "Partner", "Agent", "Sales" };
            if (!standardRoles.Contains(roleName, StringComparer.OrdinalIgnoreCase))
            {
                var rolePermission = _context.RolePermissions.OrderByDescending(r => r.CreatedAt).FirstOrDefault(r => r.RoleName == roleName);
                if (rolePermission != null && !string.IsNullOrEmpty(rolePermission.AllowedModules))
                {
                    return true;
                }
            }

            var matchingPages = _context.Pages.Where(p => p.Controller == controller && p.Action == action && p.IsActive).ToList();
            var matchingPageIds = matchingPages.Select(p => p.PageId).ToList();
            if (!matchingPageIds.Any()) return false;

            var permission = _context.Permissions.FirstOrDefault(p => p.PermissionName == permissionName && p.IsActive);
            if (permission == null) return false;
            int permissionId = permission.PermissionId;

            return _context.RolePagePermissions.Any(rpp =>
                rpp.RoleName == roleName &&
                matchingPageIds.Contains(rpp.PageId) &&
                rpp.PermissionId == permissionId &&
                rpp.IsGranted &&
                (rpp.ChannelPartnerId == userChannelPartnerId || rpp.ChannelPartnerId == null));
        }

        public async Task<bool> UpdateRoleNameAsync(string oldRoleName, string newRoleName)
        {
            try
            {
                var permissions = await _context.RolePagePermissions
                    .Where(rpp => rpp.RoleName == oldRoleName)
                    .ToListAsync();
                foreach (var permission in permissions)
                {
                    permission.RoleName = newRoleName;
                    _context.RolePagePermissions.Update(permission);
                }
                await _context.SaveChangesAsync();
                return true;
            }
            catch { return false; }
        }
    }
}
