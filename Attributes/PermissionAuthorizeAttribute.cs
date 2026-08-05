using CRM.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CRM.Attributes
{
    public class PermissionAuthorizeAttribute : ActionFilterAttribute
    {
        private readonly string _permissionName;

        public PermissionAuthorizeAttribute(string permissionName = "View")
        {
            _permissionName = permissionName;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var httpContext = context.HttpContext;
            var token = httpContext.Request.Cookies["jwtToken"];

            if (string.IsNullOrEmpty(token))
            {
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }

            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var jwt = tokenHandler.ReadJwtToken(token);
                var roleClaim = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
                var userIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;

                if (string.IsNullOrEmpty(roleClaim))
                {
                    context.Result = new RedirectToActionResult("Error", "Home", null);
                    return;
                }
                // Admin always has full access to everything
                if (roleClaim.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }


                // Admin always has full access to everything
                if (roleClaim.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // Get controller and action names
                var controllerName = context.RouteData.Values["controller"]?.ToString();
                var actionName = context.RouteData.Values["action"]?.ToString();

                // Get user's channel partner ID for permission checking
                int? userChannelPartnerId = null;
                if (int.TryParse(userIdClaim, out int userId))
                {
                    var dbContext = httpContext.RequestServices.GetService<AppDbContext>();
                    if (dbContext != null)
                    {
                        var user = dbContext.Users.FirstOrDefault(u => u.UserId == userId);
                        userChannelPartnerId = user?.ChannelPartnerId;

                        // Check if this is a custom role with AllowedModules (module-based access)
                        var standardRoles = new[] { "Admin", "Partner", "Agent", "Sales" };
                        if (!standardRoles.Contains(roleClaim, StringComparer.OrdinalIgnoreCase))
                        {
                            var rolePermission = dbContext.RolePermissions
                                .OrderByDescending(r => r.CreatedAt)
                                .FirstOrDefault(r => r.RoleName == roleClaim);
                            if (rolePermission != null && !string.IsNullOrEmpty(rolePermission.AllowedModules))
                            {
                                // Custom role with AllowedModules - grant full access
                                // Module visibility is controlled by the sidebar
                                return;
                            }
                        }
                    }
                }

                // For standard roles, check permissions from database
                var permissionService = httpContext.RequestServices.GetService<PermissionService>();
                if (permissionService != null)
                {
                    var hasPermission = permissionService.HasPermissionAsync(roleClaim, controllerName, actionName, _permissionName, userChannelPartnerId).Result;
                    if (!hasPermission)
                    {
                        context.Result = new RedirectToActionResult("AccessDenied", "Home", null);
                        return;
                    }
                }
                else
                {
                    context.Result = new RedirectToActionResult("AccessDenied", "Home", null);
                }
            }
            catch
            {
                context.Result = new RedirectToActionResult("Login", "Account", null);
            }
        }
    }
}