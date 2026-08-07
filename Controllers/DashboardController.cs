using CRM.Helpers;
using CRM.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace CRM.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(AppDbContext db, ILogger<DashboardController> logger)
        {
            _db = db;
            _logger = logger;
        }

        public IActionResult Index()
        {
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid, out int userId);
            var username = User?.FindFirst(ClaimTypes.Name)?.Value ?? User?.FindFirst("name")?.Value ?? "User";

            var currentUser = userId > 0 ? _db.Users.FirstOrDefault(u => u.UserId == userId) : null;
            var channelPartnerId = currentUser?.ChannelPartnerId;

            ViewBag.Username = username;
            ViewBag.CompanyName = BrandingResolver.ResolveCompanyName(_db, channelPartnerId, currentUser?.TenantId);
            ViewBag.CompanyLogo = BrandingResolver.ResolveCompanyLogo(_db, channelPartnerId, currentUser?.TenantId);

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetAnalyticsData()
        {
            var role = User?.FindFirst(ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid, out int userId);
            var currentUser = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;

            // Lead Statistics
            var leadsQuery = _db.Leads.AsQueryable();
            if (role?.ToLower() == "partner")
                leadsQuery = leadsQuery.Where(l => l.ChannelPartnerId == channelPartnerId);
            else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
                leadsQuery = leadsQuery.Where(l => l.ExecutiveId == userId);

            var totalLeads = await leadsQuery.CountAsync();
            var newLeadsToday = await leadsQuery.Where(l => l.CreatedOn.Date == IndianTime.Today).CountAsync();
            var newLeadsThisMonth = await leadsQuery.Where(l => l.CreatedOn.Month == IndianTime.Now.Month && l.CreatedOn.Year == IndianTime.Now.Year).CountAsync();

            // Lead Status Breakdown
            var leadsByStatus = await leadsQuery
                .GroupBy(l => l.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            // Lead Stage Funnel
            var leadsByStage = await leadsQuery
                .GroupBy(l => l.Stage)
                .Select(g => new { Stage = g.Key, Count = g.Count() })
                .OrderBy(x => x.Stage)
                .ToListAsync();

            // Lead Sources
            var leadsBySource = await leadsQuery
                .GroupBy(l => l.Source)
                .Select(g => new { Source = g.Key ?? "Unknown", Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync();

            // Monthly Trend (Last 12 months) - MongoDB compatible (no string.Format in LINQ)
            var monthlyRaw = await leadsQuery
                .Where(l => l.CreatedOn >= IndianTime.Now.AddMonths(-12))
                .GroupBy(l => new { l.CreatedOn.Year, l.CreatedOn.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                .ToListAsync();
            var monthlyTrend = monthlyRaw
                .OrderBy(x => x.Year).ThenBy(x => x.Month)
                .Select(x => new { Month = $"{x.Year}-{x.Month:D2}", x.Count })
                .ToList();

            // Booking Statistics
            var bookingsQuery = _db.Bookings.AsQueryable();
            if (role?.ToLower() == "partner" && channelPartnerId.HasValue)
            {
                var partnerLeadIds = await _db.Leads
                    .Where(l => l.ChannelPartnerId == channelPartnerId)
                    .Select(l => l.LeadId)
                    .ToListAsync();
                bookingsQuery = bookingsQuery.Where(b => partnerLeadIds.Contains(b.LeadId));
            }
            else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
            {
                var agentLeadIds = await _db.Leads
                    .Where(l => l.ExecutiveId == userId)
                    .Select(l => l.LeadId)
                    .ToListAsync();
                bookingsQuery = bookingsQuery.Where(b => agentLeadIds.Contains(b.LeadId));
            }

            var totalBookings = await bookingsQuery.CountAsync();
            var bookingsThisMonth = await bookingsQuery
                .Where(b => b.BookingDate.Month == IndianTime.Now.Month && b.BookingDate.Year == IndianTime.Now.Year)
                .CountAsync();

            var totalBookingValue = await bookingsQuery.SumAsync(b => b.TotalAmount);

            // Revenue Statistics
            var revenueQuery = _db.Payments.AsQueryable();
            if (role?.ToLower() == "partner" && channelPartnerId.HasValue)
            {
                var partnerLeadIds = await _db.Leads
                    .Where(l => l.ChannelPartnerId == channelPartnerId)
                    .Select(l => l.LeadId)
                    .ToListAsync();
                var partnerBookingIds = await _db.Bookings
                    .Where(b => partnerLeadIds.Contains(b.LeadId))
                    .Select(b => b.BookingId)
                    .ToListAsync();
                revenueQuery = revenueQuery.Where(p => partnerBookingIds.Contains(p.BookingId));
            }
            else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
            {
                var agentLeadIds = await _db.Leads
                    .Where(l => l.ExecutiveId == userId)
                    .Select(l => l.LeadId)
                    .ToListAsync();
                var agentBookingIds = await _db.Bookings
                    .Where(b => agentLeadIds.Contains(b.LeadId))
                    .Select(b => b.BookingId)
                    .ToListAsync();
                revenueQuery = revenueQuery.Where(p => agentBookingIds.Contains(p.BookingId));
            }

            var totalRevenue = await revenueQuery.SumAsync(p => p.Amount);
            var revenueThisMonth = await revenueQuery
                .Where(p => p.PaymentDate.Month == IndianTime.Now.Month && p.PaymentDate.Year == IndianTime.Now.Year)
                .SumAsync(p => p.Amount);

            // Monthly Revenue Trend - MongoDB compatible (no string.Format in LINQ)
            var revenueMonthlyRaw = await revenueQuery
                .Where(p => p.PaymentDate >= IndianTime.Now.AddMonths(-12))
                .GroupBy(p => new { p.PaymentDate.Year, p.PaymentDate.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Revenue = g.Sum(p => p.Amount) })
                .ToListAsync();
            var revenueMonthlyTrend = revenueMonthlyRaw
                .OrderBy(x => x.Year).ThenBy(x => x.Month)
                .Select(x => new { Month = $"{x.Year}-{x.Month:D2}", x.Revenue })
                .ToList();

            // Conversion Rate
            var convertedLeads = await leadsQuery.Where(l => l.Stage == "Closed Won").CountAsync();
            var conversionRate = totalLeads > 0 ? (convertedLeads * 100.0 / totalLeads) : 0;

            // Top Performing Agents (Admin/Partner only)
            List<object> topAgents = new List<object>();
            if (role?.ToLower() == "admin" || role?.ToLower() == "partner")
            {
                topAgents = await leadsQuery
                    .Where(l => l.ExecutiveId.HasValue)
                    .GroupBy(l => l.ExecutiveId)
                    .Select(g => new { 
                        ExecutiveId = g.Key,
                        LeadCount = g.Count(),
                        ConvertedCount = g.Count(l => l.Stage == "Closed Won")
                    })
                    .OrderByDescending(x => x.ConvertedCount)
                    .Take(5)
                    .ToListAsync<object>();

                // Get agent names
                var agentIds = topAgents.Select(a => ((dynamic)a).ExecutiveId).ToList();
                var agents = await _db.Users.Where(u => agentIds.Contains(u.UserId)).ToListAsync();
                var agentProfiles = await _db.UserProfiles.Where(up => agentIds.Contains(up.UserId)).ToListAsync();

                topAgents = topAgents.Select(a => {
                    var agentId = ((dynamic)a).ExecutiveId;
                    var agent = agents.FirstOrDefault(ag => ag.UserId == agentId);
                    var profile = agentProfiles.FirstOrDefault(p => p.UserId == agentId);
                    var name = profile != null ? $"{profile.FirstName} {profile.LastName}".Trim() : agent?.Username ?? "Unknown";
                    
                    return new {
                        AgentName = name,
                        LeadCount = ((dynamic)a).LeadCount,
                        ConvertedCount = ((dynamic)a).ConvertedCount,
                        ConversionRate = ((dynamic)a).LeadCount > 0 ? (((dynamic)a).ConvertedCount * 100.0 / ((dynamic)a).LeadCount) : 0
                    };
                }).Cast<object>().ToList();
            }

            return Json(new
            {
                success = true,
                data = new
                {
                    overview = new
                    {
                        totalLeads,
                        newLeadsToday,
                        newLeadsThisMonth,
                        totalBookings,
                        bookingsThisMonth,
                        totalBookingValue,
                        totalRevenue,
                        revenueThisMonth,
                        conversionRate = Math.Round(conversionRate, 2)
                    },
                    leadsByStatus,
                    leadsByStage,
                    leadsBySource,
                    monthlyTrend,
                    revenueMonthlyTrend,
                    topAgents
                }
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetRecentActivities()
        {
            var role = User?.FindFirst(ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid, out int userId);
            var currentUser = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;

            // Get recent leads
            var leadsQuery = _db.Leads.AsQueryable();
            if (role?.ToLower() == "partner")
                leadsQuery = leadsQuery.Where(l => l.ChannelPartnerId == channelPartnerId);
            else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
                leadsQuery = leadsQuery.Where(l => l.ExecutiveId == userId);

            var recentLeads = await leadsQuery
                .OrderByDescending(l => l.CreatedOn)
                .Take(5)
                .Select(l => new
                {
                    l.LeadId,
                    l.Name,
                    l.Email,
                    Contact = l.Contact,
                    l.Status,
                    l.Stage,
                    l.CreatedOn,
                    Type = "Lead"
                })
                .ToListAsync();

            // Get recent bookings
            var bookingsQuery = _db.Bookings.AsQueryable();
            if (role?.ToLower() == "partner" && channelPartnerId.HasValue)
            {
                var partnerLeadIds = await _db.Leads
                    .Where(l => l.ChannelPartnerId == channelPartnerId)
                    .Select(l => l.LeadId)
                    .ToListAsync();
                bookingsQuery = bookingsQuery.Where(b => partnerLeadIds.Contains(b.LeadId));
            }
            else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
            {
                var agentLeadIds = await _db.Leads
                    .Where(l => l.ExecutiveId == userId)
                    .Select(l => l.LeadId)
                    .ToListAsync();
                bookingsQuery = bookingsQuery.Where(b => agentLeadIds.Contains(b.LeadId));
            }

            // Get leads for customer name mapping
            var allLeads = await _db.Leads.ToListAsync();
            var recentBookings = await bookingsQuery
                .OrderByDescending(b => b.BookingDate)
                .Take(5)
                .ToListAsync();

            var bookingViewModels = recentBookings.Select(b =>
            {
                var lead = allLeads.FirstOrDefault(l => l.LeadId == b.LeadId);
                return new
                {
                    b.BookingId,
                    CustomerName = lead?.Name ?? "Unknown",
                    b.TotalAmount,
                    b.BookingDate,
                    b.Status,
                    Type = "Booking"
                };
            }).ToList();

            return Json(new
            {
                success = true,
                data = new
                {
                    recentLeads,
                    recentBookings = bookingViewModels
                }
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetUpcomingFollowUps()
        {
            var role = User?.FindFirst(ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid, out int userId);
            var currentUser = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;

            var followUpsQuery = _db.LeadFollowUps
                .Where(f => f.FollowUpDate >= IndianTime.Today && f.Status != "Completed")
                .AsQueryable();

            if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
                followUpsQuery = followUpsQuery.Where(f => f.ExecutiveId == userId);

            var followUpLeads = followUpsQuery
                .OrderBy(f => f.FollowUpDate)
                .Take(10)
                .ToList()
                .Join(_db.Leads.ToList(),
                    f => f.LeadId,
                    l => l.LeadId,
                    (f, l) => new { FollowUp = f, Lead = l });

            // Filter by partner if needed
            if (role?.ToLower() == "partner")
            {
                followUpLeads = followUpLeads.Where(x => x.Lead.ChannelPartnerId == channelPartnerId).ToList();
            }

            var upcomingFollowUps = followUpLeads.Select(x => new
                {
                    x.FollowUp.FollowUpId,
                    x.FollowUp.LeadId,
                    encodedId = IdObfuscator.Encode(x.Lead.LeadId),
                    LeadName = x.Lead.Name,
                    LeadContact = x.Lead.Contact,
                    x.FollowUp.FollowUpDate,
                    Notes = x.FollowUp.Comments,
                    x.FollowUp.Status,
                    Priority = x.FollowUp.Stage,
                    IsOverdue = x.FollowUp.FollowUpDate < IndianTime.Today
                })
                .ToList();

            return Json(new
            {
                success = true,
                data = upcomingFollowUps
            });
        }
    }
}
