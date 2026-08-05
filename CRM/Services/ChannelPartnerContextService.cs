using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CRM.Services
{
    public interface IChannelPartnerContextService
    {
        (int? UserId, string? Role, int? ChannelPartnerId) GetCurrentUserContext(HttpContext httpContext);
    }

    public class ChannelPartnerContextService : IChannelPartnerContextService
    {
        private readonly AppDbContext _context;

        public ChannelPartnerContextService(AppDbContext context)
        {
            _context = context;
        }

        public (int? UserId, string? Role, int? ChannelPartnerId) GetCurrentUserContext(HttpContext httpContext)
        {
            var token = httpContext?.Request.Cookies["jwtToken"];
            if (string.IsNullOrEmpty(token))
                return (null, null, null);

            try
            {
                var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
                var userIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
                var role = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;

                if (!int.TryParse(userIdClaim, out int userId))
                    return (null, role, null);

                // Get ChannelPartnerId from Users table
                var user = _context.Users.FirstOrDefault(u => u.UserId == userId);
                var channelPartnerId = user?.ChannelPartnerId;

                return (userId, role, channelPartnerId);
            }
            catch
            {
                return (null, null, null);
            }
        }
    }
}
