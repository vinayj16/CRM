using CRM.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CRM.Controllers
{
    [Authorize]
    public class BaseController : Controller
    {
        protected bool HasPermission(string controller, string action, string permission)
        {
            var role = User.FindFirst(ClaimTypes.Role)?.Value;
            if (string.IsNullOrEmpty(role) || role == "Admin") return true;

            var context = HttpContext.RequestServices.GetService<AppDbContext>();

            // Look up PageId(s) matching this controller+action
            var pageIds = context.Pages
                .Where(p => p.Controller == controller && p.Action == action && p.IsActive)
                .Select(p => p.PageId)
                .ToList();
            if (!pageIds.Any()) return false;

            // Look up PermissionId
            var permId = context.Permissions
                .Where(p => p.PermissionName == permission && p.IsActive)
                .Select(p => p.PermissionId)
                .FirstOrDefault();
            if (permId == 0) return false;

            // Check RolePagePermissions by ID — no .Include() needed
            return context.RolePagePermissions.Any(rpp =>
                rpp.RoleName == role &&
                pageIds.Contains(rpp.PageId) &&
                rpp.PermissionId == permId &&
                rpp.IsGranted);
        }
    }
}
