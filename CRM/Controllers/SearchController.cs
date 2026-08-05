using CRM.Helpers;
using CRM.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CRM.Controllers
{
    [Authorize]
    public class SearchController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<SearchController> _logger;
        public SearchController(AppDbContext context, ILogger<SearchController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public async Task<IActionResult> GlobalSearch(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
                return Json(new { success = false, message = "Query too short" });

            var userId = GetCurrentUserId();
            var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
            var channelPartnerId = _context.Users.FirstOrDefault(u => u.UserId == userId)?.ChannelPartnerId;

            var results = new List<SearchResult>();
            query = query.ToLower();

            // Search Sidebar Menu Items/Pages
            var menuItems = new List<SearchResult>();

            if (role == "SuperAdmin")
            {
                if (query.Contains("das") || query.Contains("sup"))
                    menuItems.Add(new SearchResult { Title = "Super Admin Dashboard", Subtitle = "System Overview", Type = "Page", Icon = "shield", Url = "/SuperAdmin/Dashboard" });

                if (query.Contains("ten") || query.Contains("com"))
                    menuItems.Add(new SearchResult { Title = "Tenants", Subtitle = "Manage Tenants", Type = "Page", Icon = "database", Url = "/SuperAdmin/Tenants" });

                if (query.Contains("inq") || query.Contains("enq"))
                    menuItems.Add(new SearchResult { Title = "Inquiries", Subtitle = "Manage Inquiries", Type = "Page", Icon = "mail", Url = "/SuperAdmin/Inquiries" });

                if (query.Contains("cre") || query.Contains("new"))
                    menuItems.Add(new SearchResult { Title = "Create Tenant", Subtitle = "Add New Tenant", Type = "Page", Icon = "plus-circle", Url = "/SuperAdmin/CreateTenant" });

                results.AddRange(menuItems);
                return Json(new { success = true, results = results.Take(20) });
            }

            // Dashboard
            if (query.Contains("das"))
                menuItems.Add(new SearchResult { Title = "Dashboard", Subtitle = "Main Dashboard", Type = "Page", Icon = "home", Url = "/home" });

            if ((role == "Admin" || role == "Partner") && (query.Contains("sales") || query.Contains("ove")))
                menuItems.Add(new SearchResult { Title = "Sales Overview", Subtitle = "Dashboard - Sales Overview", Type = "Page", Icon = "trending-up", Url = "/Home/SalesOverview" });

            if ((role == "Admin" || role == "managerrr") && query.Contains("team"))
                menuItems.Add(new SearchResult { Title = "Team Dashboard", Subtitle = "Dashboard - Team Performance", Type = "Page", Icon = "users", Url = "/Home/TeamDashboard" });

            if (role == "Admin" && (query.Contains("mil") || query.Contains("track")))
                menuItems.Add(new SearchResult { Title = "Milestone Payment Tracking", Subtitle = "Dashboard - Milestones", Type = "Page", Icon = "check-square", Url = "/MilestoneTracking" });


            // Leads & Properties
            if (query.Contains("lead"))
                menuItems.Add(new SearchResult { Title = "Leads", Subtitle = "Manage Leads", Type = "Page", Icon = "users", Url = "/Leads" });

            if (query.Contains("pipe"))
                menuItems.Add(new SearchResult { Title = "Sales Pipeline", Subtitle = "Leads - Pipeline View", Type = "Page", Icon = "trello", Url = "/SalesPipelines" });

            if (query.Contains("tas"))
                menuItems.Add(new SearchResult { Title = "Tasks", Subtitle = "Leads - Task Management", Type = "Page", Icon = "calendar", Url = "/Tasks" });

            if (role != "Sales" && role != "Agent" && (query.Contains("una") || query.Contains("web")))
                menuItems.Add(new SearchResult { Title = "Unassigned Leads", Subtitle = "Leads - Unassigned", Type = "Page", Icon = "user-plus", Url = "/WebhookLeads" });

            if (query.Contains("prop") || (query.Contains("pro") && !query.Contains("prof")))
                menuItems.Add(new SearchResult { Title = "Properties", Subtitle = "Property Management", Type = "Page", Icon = "home", Url = "/Properties" });


            // Sales
            if (query.Contains("quo"))
                menuItems.Add(new SearchResult { Title = "Quotations", Subtitle = "Sales - Quotations", Type = "Page", Icon = "file-text", Url = "/Quotations" });

            if (query.Contains("boo"))
                menuItems.Add(new SearchResult { Title = "Bookings", Subtitle = "Sales - Bookings", Type = "Page", Icon = "book-open", Url = "/Bookings" });

            if (query.Contains("inv"))
                menuItems.Add(new SearchResult { Title = "Invoices", Subtitle = "Sales - Invoices", Type = "Page", Icon = "file-text", Url = "/Invoices" });

            if (query.Contains("pay") && !query.Contains("payo"))
                menuItems.Add(new SearchResult { Title = "Payments", Subtitle = "Sales - Payments", Type = "Page", Icon = "credit-card", Url = "/Payments" });


            // Finance
            if (role != "Sales" && role != "Agent")
            {
                menuItems.Add(new SearchResult { Title = "Finance", Subtitle = "Financial Management", Type = "Page", Icon = "dollar-sign", Url = "/Expenses" });

                if (query.Contains("exp"))
                    menuItems.Add(new SearchResult { Title = "Expenses", Subtitle = "Finance - Expenses", Type = "Page", Icon = "minus-circle", Url = "/Expenses" });

                if (query.Contains("rev"))
                    menuItems.Add(new SearchResult { Title = "Revenue", Subtitle = "Finance - Revenue", Type = "Page", Icon = "plus-circle", Url = "/Revenue" });

                if (query.Contains("pro"))
                    menuItems.Add(new SearchResult { Title = "Profit", Subtitle = "Finance - Profit", Type = "Page", Icon = "trending-up", Url = "/Profit" });
            }


            // Team Management
            if (role != "Sales" && role != "Agent")
            {
                if (query.Contains("age") && !query.Contains("page"))
                    menuItems.Add(new SearchResult { Title = "Agent List", Subtitle = "Team - Agents", Type = "Page", Icon = "user", Url = "/Agent/List" });

                if (role == "Admin" && (query.Contains("cha") || query.Contains("par")))
                    menuItems.Add(new SearchResult { Title = "Channel Partners", Subtitle = "Team - Partners", Type = "Page", Icon = "briefcase", Url = "/ManageUsers/PartnerApproval" });
            }


            // Attendance
            if (role == "Sales" && query.Contains("my att"))
                menuItems.Add(new SearchResult { Title = "My Attendance", Subtitle = "Attendance - Personal", Type = "Page", Icon = "calendar", Url = "/Attendance/Calendar" });

            if ((role == "Admin" || role == "Partner") && query.Contains("att"))
                menuItems.Add(new SearchResult { Title = "Agent Attendance", Subtitle = "Attendance - Team", Type = "Page", Icon = "user-check", Url = "/Attendance/AgentList" });


            // Payouts
            if (role != "Sales" && role != "Agent")
            {
                if (query.Contains("payo"))
                    menuItems.Add(new SearchResult { Title = "Agent Payouts", Subtitle = "Payouts - Agents", Type = "Page", Icon = "credit-card", Url = "/AgentPayout" });

                if (role == "Admin" && query.Contains("par") && query.Contains("pay"))
                    menuItems.Add(new SearchResult { Title = "Partner Payouts", Subtitle = "Payouts - Partners", Type = "Page", Icon = "briefcase", Url = "/PartnerCommission" });
            }


            // User Management
            if (role != "Sales" && role != "Agent")
            {
                if (query.Contains("use"))
                    menuItems.Add(new SearchResult { Title = "Manage Users", Subtitle = "User Management", Type = "Page", Icon = "users", Url = "/ManageUsers" });

                if (query.Contains("rol"))
                    menuItems.Add(new SearchResult { Title = "Roles Management", Subtitle = "User - Roles", Type = "Page", Icon = "shield", Url = "/ManageUsers/Roles" });
            }


            // Settings
            if (query.Contains("prof"))
                menuItems.Add(new SearchResult { Title = "My Profile", Subtitle = "User Profile", Type = "Page", Icon = "user", Url = "/Profile" });

            if ((role == "Admin" || role == "Partner") && query.Contains("set"))
                menuItems.Add(new SearchResult { Title = "System Settings", Subtitle = "Settings", Type = "Page", Icon = "settings", Url = "/Settings" });

            if (role == "Admin" && query.Contains("bra"))
                menuItems.Add(new SearchResult { Title = "Branding", Subtitle = "Settings - Branding", Type = "Page", Icon = "image", Url = "/Settings/Branding" });

            if (role == "Admin" && query.Contains("inp"))
                menuItems.Add(new SearchResult { Title = "Impersonation", Subtitle = "Settings - User Impersonation", Type = "Page", Icon = "user-check", Url = "/Settings/Impersonation" });

            if ((role == "Admin" || role == "Partner") && query.Contains("ema"))
                menuItems.Add(new SearchResult { Title = "Email Settings", Subtitle = "Settings - Email", Type = "Page", Icon = "mail", Url = "/EmailSettings" });


            // Subscriptions
            if (role == "Admin" || role == "Partner")
            {
                if (query.Contains("sub") || query.Contains("plan"))
                    menuItems.Add(new SearchResult { Title = "Subscriptions", Subtitle = "Subscription Plans", Type = "Page", Icon = "credit-card", Url = role == "Admin" ? "/Subscription/Plans" : "/Subscription/MyPlan" });

                if (query.Contains("tra"))
                    menuItems.Add(new SearchResult { Title = "Transactions", Subtitle = "Payment Transactions", Type = "Page", Icon = "credit-card", Url = role == "Admin" ? "/RazorpayTransactions" : "/PartnerTransactions" });

                if (query.Contains("ref") || query.Contains("pen"))
                    menuItems.Add(new SearchResult { Title = "Pending Refunds", Subtitle = "Subscriptions - Refunds", Type = "Page", Icon = "dollar-sign", Url = "/Subscription/PendingRefunds" });
            }


            // Financial Settings
            if (role == "Admin")
            {
                if (query.Contains("gat"))
                    menuItems.Add(new SearchResult { Title = "Payment Gateways", Subtitle = "Financial Settings", Type = "Page", Icon = "credit-card", Url = "/Financial/PaymentGateways" });

                if (query.Contains("ban"))
                    menuItems.Add(new SearchResult { Title = "Bank Accounts", Subtitle = "Financial Settings", Type = "Page", Icon = "home", Url = "/Financial/BankAccounts" });
            }


            // Integrations (FULL restored)
            if (role == "Admin" || role == "Partner")
            {
                if (query.Contains("int") || query.Contains("lead integr"))
                    menuItems.Add(new SearchResult { Title = "Lead Integrations", Subtitle = "Connect lead sources", Type = "Page", Icon = "zap", Url = "/Integrations/LeadIntegrations" });

                if (query.Contains("goo") || query.Contains("ads"))
                    menuItems.Add(new SearchResult { Title = "Lead Integrations", Subtitle = "Google Ads Integration", Type = "Page", Icon = "zap", Url = "/Integrations/LeadIntegrations" });

                if (query.Contains("99") || query.Contains("acr"))
                    menuItems.Add(new SearchResult { Title = "Lead Integrations", Subtitle = "99acres Integration", Type = "Page", Icon = "zap", Url = "/Integrations/LeadIntegrations" });

                if (query.Contains("hou"))
                    menuItems.Add(new SearchResult { Title = "Lead Integrations", Subtitle = "Housing.com Integration", Type = "Page", Icon = "zap", Url = "/Integrations/LeadIntegrations" });

                if (query.Contains("mag") || query.Contains("bri"))
                    menuItems.Add(new SearchResult { Title = "Lead Integrations", Subtitle = "MagicBricks Integration", Type = "Page", Icon = "zap", Url = "/Integrations/LeadIntegrations" });

                if (query.Contains("fac") || query.Contains("meta"))
                    menuItems.Add(new SearchResult { Title = "Lead Integrations", Subtitle = "Facebook Lead Ads Integration", Type = "Page", Icon = "zap", Url = "/Integrations/LeadIntegrations" });

                if (query.Contains("jus") || query.Contains("dia"))
                    menuItems.Add(new SearchResult { Title = "Lead Integrations", Subtitle = "JustDial Integration", Type = "Page", Icon = "zap", Url = "/Integrations/LeadIntegrations" });

                if (query.Contains("ind") || query.Contains("nob"))
                    menuItems.Add(new SearchResult { Title = "Lead Integrations", Subtitle = "IndiaMART Integration", Type = "Page", Icon = "zap", Url = "/Integrations/LeadIntegrations" });

                if (query.Contains("olx"))
                    menuItems.Add(new SearchResult { Title = "Lead Integrations", Subtitle = "NoBroker Integration", Type = "Page", Icon = "zap", Url = "/Integrations/LeadIntegrations" });

                if (query.Contains("web") || query.Contains("api"))
                    menuItems.Add(new SearchResult { Title = "Lead Integrations", Subtitle = "Webhook & API Integrations", Type = "Page", Icon = "zap", Url = "/Integrations/LeadIntegrations" });
            }

            results.AddRange(menuItems);

            // Search Leads
            var leads = await _context.Leads
                .Where(l => (role == "Admin" || l.ExecutiveId == userId || l.ChannelPartnerId == channelPartnerId) &&
                           ((l.Name != null && l.Name.ToLower().Contains(query)) ||
                            (l.Contact != null && l.Contact.Contains(query)) ||
                            (l.Email != null && l.Email.ToLower().Contains(query))))
                .Take(5)
                .Select(l => new SearchResult
                {
                    Id = l.LeadId,
                    Title = l.Name ?? "Untitled",
                    Subtitle = (l.Contact ?? "") + " - " + (l.Stage ?? ""),
                    Type = "Lead",
                    Icon = "users",
                    Url = "/leaddetails/" + IdObfuscator.Encode(l.LeadId)
                }).ToListAsync();
            results.AddRange(leads);

            // Search Properties
            var properties = await _context.Properties
                .Where(p => p.IsActive &&
                           ((p.PropertyName != null && p.PropertyName.ToLower().Contains(query)) ||
                            (p.Location != null && p.Location.ToLower().Contains(query))))
                .Take(5)
                .Select(p => new SearchResult
                {
                    Id = p.PropertyId,
                    Title = p.PropertyName ?? "Untitled",
                    Subtitle = (p.Location ?? "") + " - " + (p.PurchaseType ?? ""),
                    Type = "Property",
                    Icon = "home",
                    Url = "/Properties/Details/" + p.PropertyId
                }).ToListAsync();
            results.AddRange(properties);

            // Search Users (Team Members)
            if (role == "Admin" || role == "Partner")
            {
                var users = await _context.Users
                    .Where(u => (role == "Admin" || u.ChannelPartnerId == channelPartnerId) &&
                               ((u.Username != null && u.Username.ToLower().Contains(query)) ||
                                (u.Email != null && u.Email.ToLower().Contains(query))))
                    .Take(5)
                    .Select(u => new SearchResult
                    {
                        Id = u.UserId,
                        Title = u.Username ?? u.Email ?? "Unknown",
                        Subtitle = (u.Email ?? "") + " - " + (u.Role ?? ""),
                        Type = "User",
                        Icon = "user",
                        Url = "/ManageUsers"
                    }).ToListAsync();
                results.AddRange(users);
            }

            // Search Agents
            if (role == "Admin" || role == "Partner")
            {
                var agents = await _context.Agents
                    .Where(a => (role == "Admin" || a.ChannelPartnerId == channelPartnerId) &&
                               ((a.FullName != null && a.FullName.ToLower().Contains(query)) ||
                                (a.Phone != null && a.Phone.Contains(query)) ||
                                (a.Email != null && a.Email.ToLower().Contains(query))))
                    .Take(5)
                    .Select(a => new SearchResult
                    {
                        Id = a.AgentId,
                        Title = a.FullName ?? "Untitled",
                        Subtitle = (a.Phone ?? "") + " - " + (a.AgentType ?? ""),
                        Type = "Agent",
                        Icon = "user",
                        Url = "/Agent/Details/" + a.AgentId
                    }).ToListAsync();
                results.AddRange(agents);
            }

            // Search Bookings (MongoDB: navigation properties are null, search by BookingNumber and LeadId only)
            var matchingLeadIds = query.Any(char.IsLetter)
                ? (await _context.Leads.ToListAsync())
                    .Where(l => l.Name != null && l.Name.ToLower().Contains(query))
                    .Take(10)
                    .Select(l => l.LeadId)
                    .ToList()
                : new List<int>();

            // Load all bookings in-memory for null-safe filtering
            var allBookings = await _context.Bookings.ToListAsync();
            var bookingEntities = allBookings
                .Where(b => (role == "Admin" || b.ChannelPartnerId == channelPartnerId) &&
                           (matchingLeadIds.Contains(b.LeadId) ||
                            (b.BookingNumber != null && b.BookingNumber.ToLower().Contains(query))))
                .Take(5)
                .ToList();

            // Build lead name lookup from matchingLeadIds to enrich booking subtitles
            var leadNameLookup = new Dictionary<int, string>();
            if (matchingLeadIds.Any())
            {
                foreach (var lead in await _context.Leads
                    .Where(l => matchingLeadIds.Contains(l.LeadId))
                    .ToListAsync())
                {
                    if (!leadNameLookup.ContainsKey(lead.LeadId))
                        leadNameLookup[lead.LeadId] = lead.Name;
                }
            }

            var bookings = bookingEntities.Select(b => new SearchResult
            {
                Id = b.BookingId,
                Title = "Booking #" + b.BookingNumber,
                Subtitle = (leadNameLookup.TryGetValue(b.LeadId, out var ln) ? ln + " - " : "") + b.Status,
                Type = "Booking",
                Icon = "book-open",
                Url = "/Bookings/Details/" + b.BookingId
            }).ToList();
            results.AddRange(bookings);

            return Json(new { success = true, results = results.Take(20) });
        }
        [HttpGet]
        public IActionResult Getuserrole()
        {
            var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
            return Json(new { success = true, role });
        }

        [HttpPost]
        public async Task<IActionResult> ToggleFavorite([FromBody] FavoriteRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == 0)
                {
                    return Json(new { success = false, message = "User not authenticated" });
                }

                var existing = await _context.UserFavorites.FirstOrDefaultAsync(f => f.UserId == userId && f.PageName == request.PageName);

                if (existing != null)
                {
                    _context.UserFavorites.Remove(existing);
                    await _context.SaveChangesAsync();
                    return Json(new { success = true, isFavorite = false });
                }
                else
                {
                    _context.UserFavorites.Add(new UserFavorite
                    {
                        UserId = userId,
                        PageName = request.PageName,
                        PageUrl = request.PageUrl,
                        PageIcon = request.PageIcon,
                        PageColor = request.PageColor
                    });
                    await _context.SaveChangesAsync();
                    return Json(new { success = true, isFavorite = true });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ToggleFavorite failed - favorites table may not exist");
                return Json(new { success = true, isFavorite = true, warning = "Favorites table not available" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetFavorites()
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == 0)
                {
                    return Json(new { success = true, favorites = new List<object>() });
                }

                var favorites = await _context.UserFavorites.Where(f => f.UserId == userId).ToListAsync();
                return Json(new { success = true, favorites });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetFavorites failed");
                return Json(new { success = true, favorites = new List<object>(), error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SaveRecentSearch([FromBody] RecentSearchRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == 0)
                {
                    return Json(new { success = true }); // Silent fail for unauthenticated
                }

                var existing = await _context.UserRecentSearches.Where(r => r.UserId == userId && r.SearchTerm == request.SearchTerm).FirstOrDefaultAsync();

                if (existing != null)
                {
                    _context.UserRecentSearches.Remove(existing);
                }

                _context.UserRecentSearches.Add(new UserRecentSearch { UserId = userId, SearchTerm = request.SearchTerm, SearchedAt = IndianTime.Now });

                var allRecent = await _context.UserRecentSearches.Where(r => r.UserId == userId).OrderByDescending(r => r.SearchedAt).ToListAsync();
                if (allRecent.Count > 5)
                {
                    _context.UserRecentSearches.RemoveRange(allRecent.Skip(5));
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SaveRecentSearch failed - table may not exist");
                return Json(new { success = true });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetRecentSearches()
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == 0)
                {
                    return Json(new { success = true, searches = new List<string>() });
                }

                var recent = await _context.UserRecentSearches.Where(r => r.UserId == userId).OrderByDescending(r => r.SearchedAt).Take(5).Select(r => r.SearchTerm).ToListAsync();
                return Json(new { success = true, searches = recent });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetRecentSearches failed");
                return Json(new { success = true, searches = new List<string>(), error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> RemoveRecentSearch([FromBody] RecentSearchRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == 0)
                {
                    return Json(new { success = true });
                }

                var existing = await _context.UserRecentSearches.FirstOrDefaultAsync(r => r.UserId == userId && r.SearchTerm == request.SearchTerm);

                if (existing != null)
                {
                    _context.UserRecentSearches.Remove(existing);
                    await _context.SaveChangesAsync();
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RemoveRecentSearch failed - table may not exist");
                return Json(new { success = true });
            }
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst("UserId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out int userId))
            {
                return userId;
            }
            return 0;
        }
    }

    public class SearchResult
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string Type { get; set; }
        public string Icon { get; set; }
        public string Url { get; set; }
    }

    public class FavoriteRequest
    {
        public string PageName { get; set; }
        public string PageUrl { get; set; }
        public string PageIcon { get; set; }
        public string PageColor { get; set; }
    }

    public class RecentSearchRequest
    {
        public string SearchTerm { get; set; }
    }
}