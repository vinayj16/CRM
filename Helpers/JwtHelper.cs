using CRM.Models;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace CRM.Helpers
{
    /// <summary>
    /// Shared JWT token helper used by both ApiController and MobileApiController.
    /// Generates tokens with consistent claims and validates them uniformly.
    /// </summary>
    public static class JwtHelper
    {
        /// <summary>
        /// Generate a JWT token for the given user with standard claims.
        /// </summary>
        public static string GenerateToken(UserModel user, IConfiguration config, int expiryHours = 8)
        {
            var jwtKey = config["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key not configured");
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim("UserId", user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.Username ?? ""),
                new Claim(ClaimTypes.Email, user.Email ?? ""),
                new Claim(ClaimTypes.Role, user.Role ?? ""),
                new Claim("TenantId", user.TenantId.ToString()),
                new Claim("ChannelPartnerId", user.ChannelPartnerId?.ToString() ?? "")
            };

            var token = new JwtSecurityToken(
                issuer: config["Jwt:Issuer"],
                audience: config["Jwt:Audience"],
                claims: claims,
                expires: IndianTime.Now.AddHours(expiryHours),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>
        /// Validate a Bearer token and extract user identity info.
        /// Returns null if the token is invalid, expired, or - when a config is
        /// supplied - fails signature/issuer/audience validation.
        /// </summary>
        /// <param name="authHeader">The raw Authorization header value.</param>
        /// <param name="config">Optional IConfiguration. When provided, the token
        /// signature/issuer/audience are cryptographically validated with the
        /// configured Jwt:Key; when null, only the token is decoded and its
        /// lifetime checked (backward-compatible fallback).
        /// SECURITY NOTE: callers that use the returned claims to stamp tenant/
        /// user ownership MUST pass a config so forged tokens are rejected.</param>
        public static TokenUser? ValidateToken(string authHeader, IConfiguration? config = null)
        {
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                return null;

            var token = authHeader.Substring("Bearer ".Length).Trim();

            try
            {
                var handler = new JwtSecurityTokenHandler();
                JwtSecurityToken jwt;

                if (config != null)
                {
                    var jwtKey = config["Jwt:Key"];
                    if (string.IsNullOrEmpty(jwtKey))
                        return null;

                    var validationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidIssuer = config["Jwt:Issuer"],
                        ValidAudience = config["Jwt:Audience"],
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.Zero
                    };

                    handler.ValidateToken(token, validationParameters, out _);
                    jwt = handler.ReadJwtToken(token);
                }
                else
                {
                    jwt = handler.ReadJwtToken(token);
                    if (jwt.ValidTo < IndianTime.Now)
                        return null;
                }

                var userIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
                var role = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;
                var name = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
                var email = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;

                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                    return null;

                int? tenantId = null;
                var tenantClaim = jwt.Claims.FirstOrDefault(c => c.Type == "TenantId")?.Value;
                if (!string.IsNullOrEmpty(tenantClaim) && int.TryParse(tenantClaim, out int tid))
                    tenantId = tid;

                int? channelPartnerId = null;
                var cpClaim = jwt.Claims.FirstOrDefault(c => c.Type == "ChannelPartnerId")?.Value;
                if (!string.IsNullOrEmpty(cpClaim) && int.TryParse(cpClaim, out int cpid))
                    channelPartnerId = cpid;

                return new TokenUser
                {
                    UserId = userId,
                    Username = name ?? "",
                    Email = email ?? "",
                    Role = role ?? "",
                    TenantId = tenantId,
                    ChannelPartnerId = channelPartnerId
                };
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Lightweight user identity extracted from JWT token claims.
    /// </summary>
    public class TokenUser
    {
        public int UserId { get; set; }
        public string Username { get; set; } = "";
        public string Email { get; set; } = "";
        public string Role { get; set; } = "";
        public int? TenantId { get; set; }
        public int? ChannelPartnerId { get; set; }
    }
}
