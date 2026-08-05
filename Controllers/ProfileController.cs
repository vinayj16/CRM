using CRM.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CRM.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<ProfileController> _logger;

        public ProfileController(AppDbContext context, IHttpContextAccessor httpContextAccessor, ILogger<ProfileController> logger)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        private string GetUsernameFromToken()
        {
            string token = _httpContextAccessor.HttpContext?.Request.Cookies["jwtToken"];
            if (string.IsNullOrEmpty(token)) return null;

            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(token);
                return jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name || c.Type == "name")?.Value;
            }
            catch
            {
                return null;
            }
        }

        // GET: Get profile image by path (for uploads folder images)
        [HttpGet]
        public IActionResult ProfileImage(int userId)
        {
            var profile = _context.UserProfiles.FirstOrDefault(u => u.UserId == userId);
            if (profile == null || string.IsNullOrEmpty(profile.ProfileImagePath))
            {
                return NotFound();
            }
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", profile.ProfileImagePath.TrimStart('/'));
            if (!System.IO.File.Exists(filePath))
            {
                // Fall back to binary stored in DB
                if (profile.ProfileImage != null)
                    return File(profile.ProfileImage, $"image/png");
                return NotFound();
            }
            var ext = Path.GetExtension(filePath).ToLower();
            var mime = ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                _ => "application/octet-stream"
            };
            return PhysicalFile(filePath, mime);
        }
        [Route("Profile")]
        [Route("Profile/Index")]
        public async Task<IActionResult> Index()
        {
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value.ToString();

            if (userRole == "SuperAdmin")
            {
                return RedirectToAction("Dashboard", "SuperAdmin");
            }



            // Get UserId from claims instead of username
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if ((string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId)))
            {
                return RedirectToAction("Login", "Account");
            }
            var connectionstring = ""; // MongoDB mode: no connection string needed

            var userProfile = await _context.UserProfiles.FirstOrDefaultAsync(u => u.UserId == userId);

            if (userProfile == null)
            {
                // Create a new profile if it doesn't exist
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
                if (user != null)
                {
                    userProfile = new UserProfile
                    {
                        UserId = user.UserId,
                        Username = user.Username,
                        Email = user.Email,
                        PhoneNumber = user.Phone
                    };
                    _context.UserProfiles.Add(userProfile);
                    await _context.SaveChangesAsync();
                }
            }

            // Get user's ChannelPartnerId and Role for the view
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            ViewBag.ChannelPartnerId = currentUser?.ChannelPartnerId;
            ViewBag.UserRole = User.FindFirst(ClaimTypes.Role)?.Value;

            // Get AgentId - match by email AND ChannelPartnerId to handle duplicates
            AgentModel agent;
            if (currentUser.ChannelPartnerId == null)
            {
                // Admin agents: match by email + NULL
                agent = await _context.Agents
                    .Where(a => a.Email == currentUser.Email && a.ChannelPartnerId == null)
                    .OrderByDescending(a => a.CreatedOn)
                    .FirstOrDefaultAsync();
            }
            else
            {
                // Partner agents: match by email + ChannelPartnerId
                agent = await _context.Agents
                    .Where(a => a.Email == currentUser.Email && a.ChannelPartnerId == currentUser.ChannelPartnerId)
                    .OrderByDescending(a => a.CreatedOn)
                    .FirstOrDefaultAsync();
            }
            ViewBag.AgentId = agent?.AgentId;

            return View(userProfile);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateProfile(UserProfile model, IFormFile? profileImage, string removeImage)
        {
            // Get UserId from claims instead of username
            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                return RedirectToAction("Login", "Account");
            }

            var userProfile = await _context.UserProfiles.FirstOrDefaultAsync(u => u.UserId == userId);

            if (userProfile == null)
            {
                TempData["Error"] = "Profile not found.";
                return RedirectToAction("Index");
            }

            // Update profile fields
            userProfile.FirstName = model.FirstName;
            userProfile.LastName = model.LastName;
            userProfile.Email = model.Email;
            userProfile.PhoneNumber = model.PhoneNumber;
            userProfile.Address = model.Address;
            userProfile.City = model.City;
            userProfile.State = model.State;
            userProfile.Country = model.Country;
            userProfile.PostalCode = model.PostalCode;
            userProfile.Location = model.Location;
            userProfile.Age = model.Age;
            userProfile.DOB = model.DOB;
            userProfile.Gender = model.Gender;
            userProfile.Designation = model.Designation;
            userProfile.EmployeeId = model.EmployeeId;

            // Handle profile image removal
            if (removeImage == "true")
            {
                // Delete physical file if it exists
                if (!string.IsNullOrEmpty(userProfile.ProfileImagePath))
                {
                    var oldPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", userProfile.ProfileImagePath.TrimStart('/'));
                    if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
                }
                userProfile.ProfileImage = null;
                userProfile.ProfileImagePath = null;
            }
            // Handle profile image upload - save to wwwroot/uploads/profiles/
            else if (profileImage != null && profileImage.Length > 0)
            {
                var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "profiles");
                Directory.CreateDirectory(uploadsDir);

                var ext = Path.GetExtension(profileImage.FileName).ToLower();
                var fileName = $"profile_{userId}_{DateTime.Now:yyyyMMddHHmmss}{ext}";
                var filePath = Path.Combine(uploadsDir, fileName);

                // Read bytes once - use for both file storage and backward compat binary
                byte[] imageBytes;
                using (var ms = new MemoryStream())
                {
                    await profileImage.CopyToAsync(ms);
                    imageBytes = ms.ToArray();
                }

                // Save to physical file
                await System.IO.File.WriteAllBytesAsync(filePath, imageBytes);

                // Delete old physical file if exists
                if (!string.IsNullOrEmpty(userProfile.ProfileImagePath))
                {
                    var oldPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", userProfile.ProfileImagePath.TrimStart('/'));
                    if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
                }

                userProfile.ProfileImagePath = $"/uploads/profiles/{fileName}";
                // Also store as byte array for backward compatibility with existing views/layout
                userProfile.ProfileImage = imageBytes;
            }

            try
            {
                _context.UserProfiles.Update(userProfile);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Profile updated successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Failed to update profile: " + ex.Message;
            }

            return RedirectToAction("Index");
        }
    }
}
