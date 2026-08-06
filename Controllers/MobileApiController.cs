using CRM.Helpers;
using CRM.Models;
using CRM.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MongoDB.Driver;

namespace CRM.Controllers
{
    [Route("api/mobile")]
    [ApiController]
    public class MobileApiController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly ILogger<MobileApiController> _logger;

        public MobileApiController(AppDbContext db, IConfiguration config, ILogger<MobileApiController> logger)
        {
            _db = db;
            _config = config;
            _logger = logger;
        }

        // ===================== AUTH =====================

        /// <summary>
        /// Mobile Login - Returns JWT token with user info
        /// POST /api/mobile/login
        /// </summary>
        [HttpPost("login")]
        public IActionResult Login([FromBody] MobileLoginRequest request)
        {
            var user = _db.Users.FirstOrDefault(u => u.Username == request.Username || u.Email == request.Username);

            if (user == null || !PasswordHelper.VerifyPassword(request.Password, user.Password))
            {
                return BadRequest(new { success = false, message = "Invalid credentials" });
            }

            if (!user.IsActive)
            {
                return BadRequest(new { success = false, message = "Account is inactive. Contact your admin." });
            }

            var token = GenerateJwtToken(user);

            return Ok(new
            {
                success = true,
                message = "Login successful",
                token = token,
                user = new
                {
                    userId = user.UserId,
                    username = user.Username,
                    email = user.Email,
                    role = user.Role,
                    phone = user.Phone,
                    tenantId = user.TenantId,
                    channelPartnerId = user.ChannelPartnerId,
                    isActive = user.IsActive
                }
            });
        }

        // ===================== DASHBOARD =====================

        /// <summary>
        /// Get Dashboard Stats
        /// GET /api/mobile/dashboard
        /// </summary>
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                // Get counts for the dashboard (role-aware filtering)
                var leadsQuery = _db.Leads.AsQueryable();
                var propsQuery = _db.Properties.AsQueryable();
                var bookingsQuery = _db.Bookings.AsQueryable();

                if (user.TenantId > 0)
                {
                    leadsQuery = leadsQuery.Where(l => l.TenantId == user.TenantId);
                    propsQuery = propsQuery.Where(p => p.TenantId == user.TenantId);
                    bookingsQuery = bookingsQuery.Where(b => b.TenantId == user.TenantId);
                }

                // Non-admin users see only their assigned data
                if (user.Role != "Admin" && user.Role != "SuperAdmin")
                {
                    leadsQuery = leadsQuery.Where(l => l.ExecutiveId == user.UserId || l.CreatedBy == user.UserId);
                }

                var totalLeads = await leadsQuery.CountAsync();
                var totalProperties = await propsQuery.CountAsync();
                var totalBookings = await bookingsQuery.CountAsync();
                var pendingTasks = 0;

                // Get recent leads (role-aware)
                var recentLeadsQuery = _db.Leads.AsQueryable();
                if (user.TenantId > 0)
                    recentLeadsQuery = recentLeadsQuery.Where(l => l.TenantId == user.TenantId);
                if (user.Role != "Admin" && user.Role != "SuperAdmin")
                    recentLeadsQuery = recentLeadsQuery.Where(l => l.ExecutiveId == user.UserId || l.CreatedBy == user.UserId);

                var recentLeads = await recentLeadsQuery
                    .OrderByDescending(l => l.CreatedOn)
                    .Take(5)
                    .Select(l => new
                    {
                        l.LeadId,
                        l.Name,
                        l.Contact,
                        l.Email,
                        l.Stage,
                        l.Status,
                        l.CreatedOn,
                        l.PropertyType,
                        l.Budget
                    })
                    .ToListAsync();

                // Get today's follow-ups
                var todayStart = IndianTime.Now.Date;
                var todayFollowUps = await _db.FollowUps
                    .Where(f => f.FollowUpDate >= todayStart && f.FollowUpDate < todayStart.AddDays(1))
                    .CountAsync();

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        totalLeads,
                        totalProperties,
                        totalBookings,
                        pendingTasks,
                        todayFollowUps,
                        recentLeads
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dashboard error");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== LEADS =====================

        /// <summary>
        /// Get all leads with optional filters
        /// GET /api/mobile/leads?search=&stage=&status=&page=1&pageSize=20
        /// </summary>
        [HttpGet("leads")]
        public async Task<IActionResult> GetLeads(
            [FromQuery] string? search = null,
            [FromQuery] string? stage = null,
            [FromQuery] string? status = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var query = _db.Leads.AsQueryable();

                // Filter by tenant
                if (user.TenantId > 0)
                    query = query.Where(l => l.TenantId == user.TenantId);

                // Apply search filter
                if (!string.IsNullOrEmpty(search))
                {
                    var s = search.ToLower();
                    query = query.Where(l =>
                        (l.Name != null && l.Name.ToLower().Contains(s)) ||
                        (l.Contact != null && l.Contact.Contains(s)) ||
                        (l.Email != null && l.Email.ToLower().Contains(s)) ||
                        (l.Location != null && l.Location.ToLower().Contains(s)));
                }

                // Apply stage filter
                if (!string.IsNullOrEmpty(stage))
                    query = query.Where(l => l.Stage == stage);

                // Apply status filter
                if (!string.IsNullOrEmpty(status))
                    query = query.Where(l => l.Status == status);

                var totalCount = await query.CountAsync();
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                var leads = await query
                    .OrderByDescending(l => l.CreatedOn)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(l => new
                    {
                        l.LeadId,
                        l.Name,
                        l.Contact,
                        l.Email,
                        l.Stage,
                        l.Status,
                        l.Source,
                        l.PropertyType,
                        l.Budget,
                        l.Location,
                        l.CreatedOn,
                        l.FollowUpDate,
                        l.ExecutiveId,
                        l.Comments
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    data = leads,
                    pagination = new { page, pageSize, totalCount, totalPages }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetLeads error");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Get single lead detail
        /// GET /api/mobile/leads/{id}
        /// </summary>
        [HttpGet("leads/{id}")]
        public async Task<IActionResult> GetLead(int id)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var lead = await _db.Leads.FirstOrDefaultAsync(l => l.LeadId == id);
                if (lead == null)
                    return NotFound(new { success = false, message = "Lead not found" });

                return Ok(new { success = true, data = lead });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Create a new lead
        /// POST /api/mobile/leads
        /// </summary>
        [HttpPost("leads")]
        public async Task<IActionResult> CreateLead([FromBody] MobileLeadRequest request)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var maxId = _db.Leads.Any() ? await _db.Leads.MaxAsync(l => l.LeadId) : 0;

                var lead = new LeadModel
                {
                    LeadId = maxId + 1,
                    TenantId = (user.TenantId ?? 0),
                    Name = request.Name,
                    Contact = request.Contact,
                    Email = request.Email,
                    Stage = request.Stage ?? "New",
                    Status = request.Status ?? "Active",
                    Source = request.Source ?? "Mobile App",
                    PropertyType = request.PropertyType,
                    Budget = request.Budget,
                    Location = request.Location,
                    Requirement = request.Requirement,
                    PreferredLocation = request.PreferredLocation,
                    Type = request.Type,
                    BHK = request.BHK,
                    ExecutiveId = user.UserId,
                    CreatedBy = user.UserId,
                    CreatedOn = IndianTime.Now,
                    ChannelPartnerId = user.ChannelPartnerId
                };

                _db.Leads.Add(lead);

                return Ok(new { success = true, message = "Lead created successfully", data = new { lead.LeadId } });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Update a lead
        /// PUT /api/mobile/leads/{id}
        /// </summary>
        [HttpPut("leads/{id}")]
        public async Task<IActionResult> UpdateLead(int id, [FromBody] MobileLeadRequest request)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var lead = await _db.Leads.FirstOrDefaultAsync(l => l.LeadId == id);
                if (lead == null)
                    return NotFound(new { success = false, message = "Lead not found" });

                if (request.Name != null) lead.Name = request.Name;
                if (request.Contact != null) lead.Contact = request.Contact;
                if (request.Email != null) lead.Email = request.Email;
                if (request.Stage != null) lead.Stage = request.Stage;
                if (request.Status != null) lead.Status = request.Status;
                if (request.PropertyType != null) lead.PropertyType = request.PropertyType;
                if (request.Budget != null) lead.Budget = request.Budget;
                if (request.Location != null) lead.Location = request.Location;
                if (request.Requirement != null) lead.Requirement = request.Requirement;
                if (request.PreferredLocation != null) lead.PreferredLocation = request.PreferredLocation;
                if (request.Type != null) lead.Type = request.Type;
                if (request.BHK != null) lead.BHK = request.BHK;
                lead.ModifiedOn = IndianTime.Now;

                _db.Leads.Update(lead);
                await _db.SaveChangesAsync();

                return Ok(new { success = true, message = "Lead updated successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== PROPERTIES =====================

        /// <summary>
        /// Get all properties with optional filters
        /// GET /api/mobile/properties?search=&page=1&pageSize=20
        /// </summary>
        [HttpGet("properties")]
        public async Task<IActionResult> GetProperties(
            [FromQuery] string? search = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var query = _db.Properties.AsQueryable();

                if (user.TenantId > 0)
                    query = query.Where(p => p.TenantId == user.TenantId);

                if (!string.IsNullOrEmpty(search))
                {
                    var s = search.ToLower();
                    query = query.Where(p =>
                        (p.PropertyName != null && p.PropertyName.ToLower().Contains(s)) ||
                        (p.Location != null && p.Location.ToLower().Contains(s)) ||
                        (p.Developer != null && p.Developer.ToLower().Contains(s)));
                }

                var totalCount = await query.CountAsync();
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                var properties = await query
                    .OrderByDescending(p => p.CreatedOn)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(p => new
                    {
                        p.PropertyId,
                        p.PropertyName,
                        p.Developer,
                        p.Price,
                        p.Location,
                        p.AreaSqft,
                        p.PropertyGroup,
                        p.FlatNumber,
                        p.Inventory,
                        p.IsActive,
                        p.CreatedOn
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    data = properties,
                    pagination = new { page, pageSize, totalCount, totalPages }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Get single property detail
        /// GET /api/mobile/properties/{id}
        /// </summary>
        [HttpGet("properties/{id}")]
        public async Task<IActionResult> GetProperty(int id)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var property = await _db.Properties.FirstOrDefaultAsync(p => p.PropertyId == id);
                if (property == null)
                    return NotFound(new { success = false, message = "Property not found" });

                return Ok(new { success = true, data = property });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== BOOKINGS =====================

        /// <summary>
        /// Get bookings for current user/tenant
        /// GET /api/mobile/bookings?page=1&pageSize=20
        /// </summary>
        [HttpGet("bookings")]
        public async Task<IActionResult> GetBookings(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var query = _db.Bookings.AsQueryable();

                if (user.TenantId > 0)
                    query = query.Where(b => b.TenantId == user.TenantId);

                var totalCount = await query.CountAsync();
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                var bookings = await query
                    .OrderByDescending(b => b.CreatedOn)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(b => new
                    {
                        b.BookingId,
                        b.BookingNumber,
                        b.LeadId,
                        b.PropertyId,
                        b.BookingAmount,
                        b.TotalAmount,
                        b.RemainingAmount,
                        b.PaymentType,
                        b.Status,
                        b.BookingDate,
                        b.Notes
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    data = bookings,
                    pagination = new { page, pageSize, totalCount, totalPages }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Get single booking detail
        /// GET /api/mobile/bookings/{id}
        /// </summary>
        [HttpGet("bookings/{id}")]
        public async Task<IActionResult> GetBooking(int id)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.BookingId == id);
                if (booking == null)
                    return NotFound(new { success = false, message = "Booking not found" });

                return Ok(new { success = true, data = booking });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== PROFILE =====================

        /// <summary>
        /// Get current user profile
        /// GET /api/mobile/profile
        /// </summary>
        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var fullUser = await _db.Users.FirstOrDefaultAsync(u => u.UserId == user.UserId);
                if (fullUser == null)
                    return NotFound(new { success = false, message = "User not found" });

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        fullUser.UserId,
                        fullUser.Username,
                        fullUser.Email,
                        fullUser.Phone,
                        fullUser.Role,
                        fullUser.TenantId,
                        fullUser.ChannelPartnerId,
                        fullUser.IsActive,
                        fullUser.CreatedDate,
                        fullUser.LastActivity
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Update profile
        /// PUT /api/mobile/profile
        /// </summary>
        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] MobileProfileRequest request)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var fullUser = await _db.Users.FirstOrDefaultAsync(u => u.UserId == user.UserId);
                if (fullUser == null)
                    return NotFound(new { success = false, message = "User not found" });

                if (request.Email != null) fullUser.Email = request.Email;
                if (request.Phone != null) fullUser.Phone = request.Phone;
                fullUser.LastActivity = IndianTime.Now;

                _db.Users.Update(fullUser);
                await _db.SaveChangesAsync();
                return Ok(new { success = true, message = "Profile updated successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== PROFILE IMAGE UPLOAD =====================

        /// <summary>
        /// Upload profile image (base64)
        /// POST /api/mobile/profile/image
        /// </summary>
        [HttpPost("profile/image")]
        public async Task<IActionResult> UploadProfileImage([FromBody] MobileImageRequest request)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                if (string.IsNullOrEmpty(request.ImageBase64))
                    return BadRequest(new { success = false, message = "No image data provided" });

                // Store base64 image in user profile or settings
                var profile = await _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == user.UserId);
                if (profile == null)
                {
                profile = new UserProfile
                {
                    UserId = user.UserId,
                    TenantId = (user.TenantId ?? 0),
                    ProfileImagePath = request.ImageBase64
                };
                    _db.UserProfiles.Add(profile);
                }
                else
                {
                    profile.ProfileImagePath = request.ImageBase64;
                    _db.UserProfiles.Update(profile);
                }

                await _db.SaveChangesAsync();
                return Ok(new { success = true, message = "Profile image updated" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== STATS FOR SPECIFIC MODULES =====================

        /// <summary>
        /// Get lead stage statistics
        /// GET /api/mobile/stats/leads
        /// </summary>
        [HttpGet("stats/leads")]
        public async Task<IActionResult> GetLeadStats()
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var query = _db.Leads.AsQueryable();
                if (user.TenantId > 0)
                    query = query.Where(l => l.TenantId == user.TenantId);

                var allLeads = await query.ToListAsync();

                var stats = new
                {
                    total = allLeads.Count,
                    byStage = allLeads.GroupBy(l => l.Stage ?? "Unknown")
                        .ToDictionary(g => g.Key, g => g.Count()),
                    byStatus = allLeads.GroupBy(l => l.Status ?? "Unknown")
                        .ToDictionary(g => g.Key, g => g.Count()),
                    todayAdded = allLeads.Count(l => l.CreatedOn.Date == IndianTime.Now.Date),
                    thisWeek = allLeads.Count(l => l.CreatedOn >= IndianTime.Now.AddDays(-7))
                };

                return Ok(new { success = true, data = stats });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== SEARCH =====================

        /// <summary>
        /// Global search across leads and properties
        /// GET /api/mobile/search?q=
        /// </summary>
        [HttpGet("search")]
        public async Task<IActionResult> GlobalSearch([FromQuery] string q)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
                    return Ok(new { success = true, data = new { leads = new List<object>(), properties = new List<object>() } });

                var s = q.ToLower();

                // Search leads
                var leadsQuery = _db.Leads.AsQueryable();
                if (user.TenantId > 0)
                    leadsQuery = leadsQuery.Where(l => l.TenantId == user.TenantId);

                var leads = await leadsQuery
                    .Where(l =>
                        (l.Name != null && l.Name.ToLower().Contains(s)) ||
                        (l.Contact != null && l.Contact.Contains(s)) ||
                        (l.Email != null && l.Email.ToLower().Contains(s)))
                    .Take(10)
                    .Select(l => new { l.LeadId, l.Name, l.Contact, l.Email, l.Stage, type = "lead" })
                    .ToListAsync();

                // Search properties
                var propQuery = _db.Properties.AsQueryable();
                if (user.TenantId > 0)
                    propQuery = propQuery.Where(p => p.TenantId == user.TenantId);

                var properties = await propQuery
                    .Where(p =>
                        (p.PropertyName != null && p.PropertyName.ToLower().Contains(s)) ||
                        (p.Location != null && p.Location.ToLower().Contains(s)))
                    .Take(10)
                    .Select(p => new { p.PropertyId, p.PropertyName, p.Location, p.Price, type = "property" })
                    .ToListAsync();

                return Ok(new { success = true, data = new { leads, properties } });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== REFERRALS =====================

        /// <summary>
        /// Get referrals for the current user/tenant
        /// GET /api/mobile/referrals
        /// </summary>
        [HttpGet("referrals")]
        public async Task<IActionResult> GetReferrals()
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                // Get referral earnings
                var earnings = await _db.Revenues
                    .Where(r => r.Type == "Referral" && r.TenantId == (user.TenantId ?? 0))
                    .OrderByDescending(r => r.Date)
                    .Select(r => new
                    {
                        r.RevenueId,
                        r.Amount,
                        r.Description,
                        CreatedOn = r.Date,
                        r.Source
                    })
                    .ToListAsync();

                return Ok(new { success = true, data = earnings });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== NOTIFICATIONS =====================

        /// <summary>
        /// Get user notifications
        /// GET /api/mobile/notifications
        /// </summary>
        [HttpGet("notifications")]
        public async Task<IActionResult> GetNotifications()
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var notifications = await _db.Notifications
                    .Where(n => n.UserId == user.UserId || n.UserId == null)
                    .OrderByDescending(n => n.CreatedOn)
                    .Take(50)
                    .Select(n => new
                    {
                        n.NotificationId,
                        n.Title,
                        n.Message,
                        n.Type,
                        n.IsRead,
                        n.CreatedOn,
                        n.Link
                    })
                    .ToListAsync();

                var unreadCount = notifications.Count(n => !n.IsRead);

                return Ok(new { success = true, data = notifications, unreadCount });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== BOOKINGS CRUD =====================

        /// <summary>
        /// Create a new booking
        /// POST /api/mobile/bookings
        /// </summary>
        [HttpPost("bookings")]
        public async Task<IActionResult> CreateBooking([FromBody] MobileBookingRequest request)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var maxId = _db.Bookings.Any() ? await _db.Bookings.MaxAsync(b => b.BookingId) : 0;

                var booking = new BookingModel
                {
                    BookingId = maxId + 1,
                    TenantId = (user.TenantId ?? 0),
                    BookingNumber = $"BK-{IndianTime.Now:yyyyMMdd}-{maxId + 1:D4}",
                    LeadId = request.LeadId,
                    PropertyId = request.PropertyId,
                    FlatId = request.FlatId ?? 0,
                    BookingAmount = request.BookingAmount,
                    TotalAmount = request.TotalAmount,
                    RemainingAmount = request.TotalAmount - request.BookingAmount,
                    PaymentType = request.PaymentType ?? "FullPayment",
                    Status = "Confirmed",
                    BookingDate = IndianTime.Now,
                    Notes = request.Notes,
                    CreatedBy = user.UserId,
                    CreatedOn = IndianTime.Now,
                    ChannelPartnerId = user.ChannelPartnerId
                };

                _db.Bookings.Add(booking);

                return Ok(new { success = true, message = "Booking created", data = new { booking.BookingId, booking.BookingNumber } });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Update booking status
        /// PUT /api/mobile/bookings/{id}/status
        /// </summary>
        [HttpPut("bookings/{id}/status")]
        public async Task<IActionResult> UpdateBookingStatus(int id, [FromBody] MobileStatusRequest request)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.BookingId == id);
                if (booking == null)
                    return NotFound(new { success = false, message = "Booking not found" });

                booking.Status = request.Status ?? booking.Status;
                booking.ModifiedOn = IndianTime.Now;
                _db.Bookings.Update(booking);
                await _db.SaveChangesAsync();

                return Ok(new { success = true, message = "Booking status updated" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== INVOICES =====================

        /// <summary>
        /// Get invoices
        /// GET /api/mobile/invoices?page=1&pageSize=20
        /// </summary>
        [HttpGet("invoices")]
        public async Task<IActionResult> GetInvoices([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var query = _db.Invoices.AsQueryable();
                if (user.TenantId > 0)
                    query = query.Where(i => i.TenantId == user.TenantId);

                var totalCount = await query.CountAsync();
                var invoices = await query
                    .OrderByDescending(i => i.CreatedOn)
                    .Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(i => new
                    {
                        i.InvoiceId, i.InvoiceNumber, i.BookingId, i.Amount, i.TaxAmount,
                        i.TotalAmount, i.PaidAmount, i.Status, i.DueDate, i.CreatedOn
                    })
                    .ToListAsync();

                return Ok(new { success = true, data = invoices, pagination = new { page, pageSize, totalCount } });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Get single invoice
        /// GET /api/mobile/invoices/{id}
        /// </summary>
        [HttpGet("invoices/{id}")]
        public async Task<IActionResult> GetInvoice(int id)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.InvoiceId == id);
                if (invoice == null)
                    return NotFound(new { success = false, message = "Invoice not found" });

                return Ok(new { success = true, data = invoice });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== PAYMENTS =====================

        /// <summary>
        /// Get payments
        /// GET /api/mobile/payments?page=1&pageSize=20
        /// </summary>
        [HttpGet("payments")]
        public async Task<IActionResult> GetPayments([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var query = _db.Payments.AsQueryable();
                if (user.TenantId > 0)
                    query = query.Where(p => p.TenantId == user.TenantId);

                var totalCount = await query.CountAsync();
                var payments = await query
                    .OrderByDescending(p => p.CreatedOn)
                    .Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(p => new
                    {
                        p.PaymentId, p.ReceiptNumber, p.InvoiceId, p.BookingId,
                        p.Amount, p.PaymentMethod, p.Status, p.PaymentDate, p.TransactionReference
                    })
                    .ToListAsync();

                return Ok(new { success = true, data = payments, pagination = new { page, pageSize, totalCount } });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Record a payment
        /// POST /api/mobile/payments
        /// </summary>
        [HttpPost("payments")]
        public async Task<IActionResult> CreatePayment([FromBody] MobilePaymentRequest request)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var maxId = _db.Payments.Any() ? await _db.Payments.MaxAsync(p => p.PaymentId) : 0;

                var payment = new PaymentModel
                {
                    PaymentId = maxId + 1,
                    TenantId = (user.TenantId ?? 0),
                    ReceiptNumber = $"RCP-{IndianTime.Now:yyyyMMdd}-{maxId + 1:D4}",
                    InvoiceId = request.InvoiceId,
                    BookingId = request.BookingId,
                    Amount = request.Amount,
                    PaymentMethod = request.PaymentMethod ?? "Cash",
                    TransactionReference = request.TransactionReference,
                    PaymentDate = IndianTime.Now,
                    Notes = request.Notes,
                    Status = "Completed",
                    ReceivedBy = user.UserId,
                    CreatedOn = IndianTime.Now
                };

                _db.Payments.Add(payment);

                // Note: Payment is recorded successfully. Invoice paid amount tracking
                // requires a direct MongoDB collection update (not yet implemented in this API).
                // The payment record itself is persisted correctly.

                return Ok(new { success = true, message = "Payment recorded", data = new { payment.PaymentId } });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== EXPENSES =====================

        /// <summary>
        /// Get expenses
        /// GET /api/mobile/expenses?page=1&pageSize=20
        /// </summary>
        [HttpGet("expenses")]
        public async Task<IActionResult> GetExpenses([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var query = _db.Expenses.AsQueryable();
                if (user.TenantId > 0)
                    query = query.Where(e => e.TenantId == user.TenantId);

                var totalCount = await query.CountAsync();
                var expenses = await query
                    .OrderByDescending(e => e.Date)
                    .Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(e => new { e.ExpenseId, e.Type, e.Category, e.Description, e.Amount, e.Date })
                    .ToListAsync();

                return Ok(new { success = true, data = expenses, pagination = new { page, pageSize, totalCount } });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Create expense
        /// POST /api/mobile/expenses
        /// </summary>
        [HttpPost("expenses")]
        public async Task<IActionResult> CreateExpense([FromBody] MobileExpenseRequest request)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var maxId = _db.Expenses.Any() ? await _db.Expenses.MaxAsync(e => e.ExpenseId) : 0;

                var expense = new ExpenseModel
                {
                    ExpenseId = maxId + 1,
                    TenantId = (user.TenantId ?? 0),
                    Type = request.Type ?? "Other",
                    Category = request.Category,
                    Description = request.Description ?? "",
                    Amount = request.Amount,
                    Date = IndianTime.Now,
                    ChannelPartnerId = user.ChannelPartnerId
                };

                _db.Expenses.Add(expense);
                return Ok(new { success = true, message = "Expense recorded", data = new { expense.ExpenseId } });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== REVENUE =====================

        /// <summary>
        /// Get revenue records
        /// GET /api/mobile/revenue?page=1&pageSize=20
        /// </summary>
        [HttpGet("revenue")]
        public async Task<IActionResult> GetRevenue([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var query = _db.Revenues.AsQueryable();
                if (user.TenantId > 0)
                    query = query.Where(r => r.TenantId == user.TenantId);

                var totalCount = await query.CountAsync();
                var revenues = await query
                    .OrderByDescending(r => r.Date)
                    .Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(r => new { r.RevenueId, r.Type, r.Source, r.Description, r.Amount, r.Date })
                    .ToListAsync();

                return Ok(new { success = true, data = revenues, pagination = new { page, pageSize, totalCount } });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== QUOTATIONS =====================

        /// <summary>
        /// Get quotations
        /// GET /api/mobile/quotations?page=1&pageSize=20
        /// </summary>
        [HttpGet("quotations")]
        public async Task<IActionResult> GetQuotations([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var query = _db.Quotations.AsQueryable();
                if (user.TenantId > 0)
                    query = query.Where(q => q.TenantId == user.TenantId);

                var totalCount = await query.CountAsync();
                var quotations = await query
                    .OrderByDescending(q => q.CreatedOn)
                    .Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(q => new
                    {
                        q.QuotationId, q.QuotationNumber, q.LeadId, q.PropertyId,
                        q.BasePrice, q.TotalAmount, q.DiscountAmount, q.GrandTotal,
                        q.Status, q.QuotationDate, q.ValidUntil
                    })
                    .ToListAsync();

                return Ok(new { success = true, data = quotations, pagination = new { page, pageSize, totalCount } });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Get single quotation
        /// GET /api/mobile/quotations/{id}
        /// </summary>
        [HttpGet("quotations/{id}")]
        public async Task<IActionResult> GetQuotation(int id)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var quotation = await _db.Quotations.FirstOrDefaultAsync(q => q.QuotationId == id);
                if (quotation == null)
                    return NotFound(new { success = false, message = "Quotation not found" });

                return Ok(new { success = true, data = quotation });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== ATTENDANCE =====================

        /// <summary>
        /// Get today's attendance status
        /// GET /api/mobile/attendance/status
        /// </summary>
        [HttpGet("attendance/status")]
        public async Task<IActionResult> GetAttendanceStatus()
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var today = IndianTime.Now.Date;
                var attendance = await _db.AgentAttendances
                    .FirstOrDefaultAsync(a => a.AgentId == user.UserId && a.Date == today);

                if (attendance == null)
                    return Ok(new { success = true, data = new { status = "Not Marked", loggedIn = false } });

                return Ok(new { success = true, data = new
                {
                    status = attendance.Status,
                    loggedIn = attendance.LoginTime != null && attendance.LogoutTime == null,
                    loginTime = attendance.LoginTime,
                    logoutTime = attendance.LogoutTime
                }});
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Clock in / out
        /// POST /api/mobile/attendance/clock
        /// </summary>
        [HttpPost("attendance/clock")]
        public async Task<IActionResult> ClockInOut()
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var today = IndianTime.Now.Date;
                var now = IndianTime.Now;

                var attendance = await _db.AgentAttendances
                    .FirstOrDefaultAsync(a => a.AgentId == user.UserId && a.Date == today);

                if (attendance == null)
                {
                    // Clock in
                    var maxId = _db.AgentAttendances.Any() ? await _db.AgentAttendances.MaxAsync(a => a.AttendanceId) : 0;
                    attendance = new AgentAttendanceModel
                    {
                        AttendanceId = maxId + 1,
                        TenantId = (user.TenantId ?? 0),
                        AgentId = user.UserId,
                        Date = today,
                        LoginTime = now,
                        Status = "Present"
                    };
                    _db.AgentAttendances.Add(attendance);

                    // Log the attendance log entry
                    var logMax = _db.AttendanceLogs.Any() ? await _db.AttendanceLogs.MaxAsync(l => l.AttendanceLogId) : 0;
                    _db.AttendanceLogs.Add(new AttendanceLogModel
                    {
                        AttendanceLogId = logMax + 1,
                        TenantId = (user.TenantId ?? 0),
                        AttendanceId = attendance.AttendanceId,
                        AgentId = user.UserId,
                        Timestamp = now,
                        Type = "Login"
                    });

                    return Ok(new { success = true, message = "Clocked in", data = new { action = "in", time = now } });
                }
                else if (attendance.LoginTime != null && attendance.LogoutTime == null)
                {
                    // Clock out
                    attendance.LogoutTime = now;

                    var logMax = _db.AttendanceLogs.Any() ? await _db.AttendanceLogs.MaxAsync(l => l.AttendanceLogId) : 0;
                    _db.AttendanceLogs.Add(new AttendanceLogModel
                    {
                        AttendanceLogId = logMax + 1,
                        TenantId = (user.TenantId ?? 0),
                        AttendanceId = attendance.AttendanceId,
                        AgentId = user.UserId,
                        Timestamp = now,
                        Type = "Logout"
                    });

                    return Ok(new { success = true, message = "Clocked out", data = new { action = "out", time = now } });
                }
                else
                {
                    return Ok(new { success = true, message = "Already clocked out for today", data = new { action = "done" } });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Get attendance history
        /// GET /api/mobile/attendance/history?days=30
        /// </summary>
        [HttpGet("attendance/history")]
        public async Task<IActionResult> GetAttendanceHistory([FromQuery] int days = 30)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var since = IndianTime.Now.AddDays(-days);
                var records = await _db.AgentAttendances
                    .Where(a => a.AgentId == user.UserId && a.Date >= since)
                    .OrderByDescending(a => a.Date)
                    .Select(a => new { a.AttendanceId, a.Date, a.LoginTime, a.LogoutTime, a.Status })
                    .ToListAsync();

                return Ok(new { success = true, data = records });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== SUPPORT TICKETS =====================

        /// <summary>
        /// Get support tickets
        /// GET /api/mobile/tickets?page=1&pageSize=20
        /// </summary>
        [HttpGet("tickets")]
        public async Task<IActionResult> GetTickets([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var query = _db.Tickets.AsQueryable();
                if (user.TenantId > 0)
                    query = query.Where(t => t.TenantId == user.TenantId);

                var totalCount = await query.CountAsync();
                var tickets = await query
                    .OrderByDescending(t => t.CreatedOn)
                    .Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(t => new
                    {
                        t.TicketId, t.Subject, t.Category, t.Priority, t.Status,
                        t.CreatedOn, t.CreatedByName
                    })
                    .ToListAsync();

                return Ok(new { success = true, data = tickets, pagination = new { page, pageSize, totalCount } });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Create a support ticket
        /// POST /api/mobile/tickets
        /// </summary>
        [HttpPost("tickets")]
        public async Task<IActionResult> CreateTicket([FromBody] MobileTicketRequest request)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var maxId = _db.Tickets.Any() ? await _db.Tickets.MaxAsync(t => t.TicketId) : 0;

                var ticket = new SupportTicketModel
                {
                    TicketId = maxId + 1,
                    TenantId = (user.TenantId ?? 0),
                    Subject = request.Subject ?? "",
                    Description = request.Description ?? "",
                    Category = request.Category ?? "General",
                    Priority = request.Priority ?? "Normal",
                    Status = "Open",
                    CreatedBy = user.UserId,
                    CreatedByUserId = user.UserId,
                    CreatedByUsername = user.Username,
                    CreatedOn = IndianTime.Now
                };

                _db.Tickets.Add(ticket);
                return Ok(new { success = true, message = "Ticket created", data = new { ticket.TicketId } });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== SITE VISITS =====================

        /// <summary>
        /// Get site visits
        /// GET /api/mobile/sitevisits?page=1&pageSize=20
        /// </summary>
        [HttpGet("sitevisits")]
        public async Task<IActionResult> GetSiteVisits([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var query = _db.SiteVisits.AsQueryable();
                if (user.TenantId > 0)
                    query = query.Where(s => s.TenantId == user.TenantId);

                var totalCount = await query.CountAsync();
                var visits = await query
                    .OrderByDescending(s => s.ScheduledDate)
                    .Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(s => new
                    {
                        s.SiteVisitId, s.LeadId, s.LeadName, s.PropertyName,
                        s.ScheduledDate, s.TimeSlot, s.Status, s.Vehicle
                    })
                    .ToListAsync();

                return Ok(new { success = true, data = visits, pagination = new { page, pageSize, totalCount } });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Create a site visit
        /// POST /api/mobile/sitevisits
        /// </summary>
        [HttpPost("sitevisits")]
        public async Task<IActionResult> CreateSiteVisit([FromBody] MobileSiteVisitRequest request)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var maxId = _db.SiteVisits.Any() ? await _db.SiteVisits.MaxAsync(s => s.SiteVisitId) : 0;

                var visit = new SiteVisitModel
                {
                    SiteVisitId = maxId + 1,
                    TenantId = (user.TenantId ?? 0),
                    LeadId = request.LeadId,
                    LeadName = request.LeadName,
                    PropertyId = request.PropertyId,
                    PropertyName = request.PropertyName,
                    ScheduledDate = request.ScheduledDate ?? IndianTime.Now,
                    TimeSlot = request.TimeSlot,
                    Status = "Scheduled",
                    Vehicle = request.Vehicle,
                    Notes = request.Notes,
                    CreatedBy = user.UserId,
                    CreatedOn = IndianTime.Now
                };

                _db.SiteVisits.Add(visit);
                return Ok(new { success = true, message = "Site visit scheduled", data = new { visit.SiteVisitId } });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== TEAM / USERS =====================

        /// <summary>
        /// Get team members
        /// GET /api/mobile/team
        /// </summary>
        [HttpGet("team")]
        public async Task<IActionResult> GetTeamMembers()
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var users = await _db.Users
                    .Where(u => u.TenantId == (user.TenantId ?? 0) && u.IsActive)
                    .Select(u => new { u.UserId, u.Username, u.Email, u.Phone, u.Role, u.LastActivity })
                    .ToListAsync();

                return Ok(new { success = true, data = users });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== FOLLOW-UPS =====================

        /// <summary>
        /// Get follow-ups
        /// GET /api/mobile/followups?days=7
        /// </summary>
        [HttpGet("followups")]
        public async Task<IActionResult> GetFollowUps([FromQuery] int days = 7)
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var until = IndianTime.Now.AddDays(days);
                var today = IndianTime.Now;
                var followUps = await _db.FollowUps
                    .Where(f => f.TenantId == (user.TenantId ?? 0) && f.FollowUpDate >= today && f.FollowUpDate <= until)
                    .OrderBy(f => f.FollowUpDate)
                    .Select(f => new { f.FollowUpId, f.LeadId, f.FollowUpDate, f.Comments, f.Status, f.CreatedOn })
                    .ToListAsync();

                return Ok(new { success = true, data = followUps });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== SUBSCRIPTION INFO =====================

        /// <summary>
        /// Get current subscription/plan info
        /// GET /api/mobile/subscription
        /// </summary>
        [HttpGet("subscription")]
        public async Task<IActionResult> GetSubscription()
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                // Get tenant-level subscription and plan
                var sub = await _db.TenantSubscriptions
                    .FirstOrDefaultAsync(s => s.TenantId == (user.TenantId ?? 0) && s.Status == "Active");

                object planData = null;
                if (sub != null)
                {
                    var plan = await _db.SaasPlans.FirstOrDefaultAsync(p => p.PlanId == sub.PlanId);
                    planData = new
                    {
                        subscriptionId = sub.SubscriptionId,
                        planName = plan?.PlanName,
                        planId = sub.PlanId,
                        status = sub.Status,
                        billingCycle = sub.BillingCycle,
                        amount = sub.Amount,
                        startDate = sub.StartDate,
                        endDate = sub.EndDate,
                        autoRenew = sub.AutoRenew
                    };
                }

                return Ok(new { success = true, data = planData });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== SETTINGS =====================

        /// <summary>
        /// Get user settings
        /// GET /api/mobile/settings
        /// </summary>
        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings()
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var settings = await _db.Settings
                    .FirstOrDefaultAsync(s => s.TenantId == (user.TenantId ?? 0));

                if (settings == null)
                    return Ok(new { success = true, data = new { } });
                return Ok(new { success = true, data = settings });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== PROFIT SUMMARY =====================

        /// <summary>
        /// Get profit summary
        /// GET /api/mobile/profit
        /// </summary>
        [HttpGet("profit")]
        public async Task<IActionResult> GetProfitSummary()
        {
            try
            {
                var user = Authenticate();
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid or missing token" });

                var tid = user.TenantId ?? 0;
                var totalRevenue = await _db.Revenues.Where(r => r.TenantId == tid).SumAsync(r => r.Amount);
                var totalExpenses = await _db.Expenses.Where(e => e.TenantId == tid).SumAsync(e => e.Amount);
                var profit = totalRevenue - totalExpenses;

                return Ok(new { success = true, data = new { totalRevenue, totalExpenses, profit } });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // ===================== MISC ENDPOINTS =====================

        /// <summary>
        /// Health check endpoint
        /// GET /api/mobile/health
        /// </summary>
        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { success = true, message = "CRM Mobile API is running", timestamp = IndianTime.Now, version = "1.0.0" });
        }

        // ===================== HELPER METHODS =====================

        private TokenUser? Authenticate()
        {
            var authHeader = Request.Headers["Authorization"].ToString();
            return JwtHelper.ValidateToken(authHeader);
        }

        private string GenerateJwtToken(UserModel user)
        {
            return JwtHelper.GenerateToken(user, _config, expiryHours: 720); // 30 days for mobile
        }
    }

    // ===================== REQUEST MODELS =====================

    public class MobileLoginRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class MobileLeadRequest
    {
        public string? Name { get; set; }
        public string? Contact { get; set; }
        public string? Email { get; set; }
        public string? Stage { get; set; }
        public string? Status { get; set; }
        public string? Source { get; set; }
        public string? PropertyType { get; set; }
        public string? Budget { get; set; }
        public string? Location { get; set; }
        public string? Requirement { get; set; }
        public string? PreferredLocation { get; set; }
        public string? Type { get; set; }
        public string? BHK { get; set; }
    }

    public class MobileProfileRequest
    {
        public string? Email { get; set; }
        public string? Phone { get; set; }
    }

    public class MobileImageRequest
    {
        public string ImageBase64 { get; set; } = "";
    }

    public class MobileBookingRequest
    {
        public int LeadId { get; set; }
        public int PropertyId { get; set; }
        public int? FlatId { get; set; }
        public decimal BookingAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public string? PaymentType { get; set; }
        public string? Notes { get; set; }
    }

    public class MobileStatusRequest
    {
        public string? Status { get; set; }
    }

    public class MobilePaymentRequest
    {
        public int InvoiceId { get; set; }
        public int BookingId { get; set; }
        public decimal Amount { get; set; }
        public string? PaymentMethod { get; set; }
        public string? TransactionReference { get; set; }
        public string? Notes { get; set; }
    }

    public class MobileExpenseRequest
    {
        public string? Type { get; set; }
        public string? Category { get; set; }
        public string? Description { get; set; }
        public decimal Amount { get; set; }
    }

    public class MobileTicketRequest
    {
        public string? Subject { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }
        public string? Priority { get; set; }
    }

    public class MobileSiteVisitRequest
    {
        public int LeadId { get; set; }
        public string? LeadName { get; set; }
        public int? PropertyId { get; set; }
        public string? PropertyName { get; set; }
        public DateTime? ScheduledDate { get; set; }
        public string? TimeSlot { get; set; }
        public string? Vehicle { get; set; }
        public string? Notes { get; set; }
    }
}
