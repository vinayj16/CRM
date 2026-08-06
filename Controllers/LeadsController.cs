using CRM.Helpers;
using ClosedXML.Excel;
using CRM.Models;
using CRM.Services;
using CRM.Attributes;
using DocumentFormat.OpenXml.InkML;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml; // EPPlus
using System;
using System.ComponentModel;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Security.Claims;


namespace CRM.Controllers
{
    [Authorize]
    public class LeadsController : Controller
    
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly INotificationService _notificationService;
        private readonly SubscriptionService _subscriptionService;
        private readonly ILogger<LeadsController> _logger;

        public LeadsController(AppDbContext db, IWebHostEnvironment env, IHttpContextAccessor httpContextAccessor, INotificationService notificationService, SubscriptionService subscriptionService, ILogger<LeadsController> logger)
        {
            _db = db;
            _env = env;
            _httpContextAccessor = httpContextAccessor;
            _notificationService = notificationService;
            _subscriptionService = subscriptionService;
            _logger = logger;
        }

        private int _getCurrentUserId()
        {
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(uid, out int id)) return id;
            return 0; // fallback (adjust for your auth)
        }
        private string _getCurrentUserRole()
        {
            var role = User?.FindFirst(ClaimTypes.Role)?.Value;
            return role ?? "Admin";
        }

        private (int? userId, string? role) GetUserFromToken()
        {
            string token = _httpContextAccessor.HttpContext?.Request.Cookies["jwtToken"];
            if (string.IsNullOrEmpty(token))
                return (null, null);

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

            var uid = jwt.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            var role = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;


            if (int.TryParse(uid, out int userId))
                return (userId, role);

            return (null, role);
        }

        [PermissionAuthorize("View")]
        public IActionResult Index(string search = "", int page = 1, int pageSize = 7,
            string stage = "", string status = "", int? executiveId = null,
            string source = "", string type = "", string propertyType = "", DateTime? from = null, DateTime? to = null,
            string groupName = "", string preferredLocation = "", string handoverStatus = "",
            string sortBy = "", string sortDir = "desc")
        {
            var q = BuildFilteredQuery(search, stage, status, executiveId, source, type, propertyType, from, to, groupName, preferredLocation, handoverStatus);
            var (UserId, Role) = GetUserFromToken();

            var role = Role;
            var curUserId = UserId;
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == UserId);
            var channelPartnerId = currentUser?.ChannelPartnerId;
            if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
            {
                if (channelPartnerId.HasValue)
                {
                    q = q.Where(l => (l.PartnerAssignedAgentUserId ?? l.ExecutiveId) == UserId);
                }
                else
                {
                    q = q.Where(l => l.ExecutiveId == UserId);
                }
            }

            if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Name.Contains(search));
            if (!string.IsNullOrWhiteSpace(stage)) q = q.Where(x => x.Stage == stage);
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
            if (executiveId.HasValue)
            {
                if (channelPartnerId.HasValue)
                {
                    q = q.Where(x => (x.PartnerAssignedAgentUserId ?? x.ExecutiveId) == executiveId.Value);
                }
                else
                {
                    q = q.Where(x => x.ExecutiveId == executiveId.Value);
                }
            }
            if (!string.IsNullOrWhiteSpace(source)) q = q.Where(x => x.Source == source);
            if (!string.IsNullOrWhiteSpace(type)) q = q.Where(x => x.Type == type);
            if (!string.IsNullOrWhiteSpace(propertyType)) q = q.Where(x => x.PropertyType == propertyType);
            if (!string.IsNullOrWhiteSpace(groupName)) q = q.Where(x => x.GroupName != null && x.GroupName.Contains(groupName));
            if (!string.IsNullOrWhiteSpace(preferredLocation)) q = q.Where(x => x.PreferredLocation != null && x.PreferredLocation.Contains(preferredLocation));
            if (from.HasValue) q = q.Where(x => x.CreatedOn >= from.Value.Date);
            if (to.HasValue) q = q.Where(x => x.CreatedOn <= to.Value.Date.AddDays(1).AddSeconds(-1));

            var total = q.Count();

            // apply sorting
            if (!string.IsNullOrWhiteSpace(sortBy))
            {
                var dir = (sortDir ?? "desc").ToLower();
                q = sortBy.ToLower() switch
                {
                    "name" => dir == "asc" ? q.OrderBy(x => x.Name) : q.OrderByDescending(x => x.Name),
                    "followupdate" => dir == "asc" ? q.OrderBy(x => x.FollowUpDate) : q.OrderByDescending(x => x.FollowUpDate),
                    "status" => dir == "asc" ? q.OrderBy(x => x.Status) : q.OrderByDescending(x => x.Status),
                    _ => dir == "asc" ? q.OrderBy(x => x.CreatedOn) : q.OrderByDescending(x => x.CreatedOn),
                };
            }
            else
            {
                q = q.OrderByDescending(x => x.CreatedOn);
            }

            // For client-side pagination, return ALL leads (not paginated)
            var leads = q.ToList();

            ViewBag.Total = total;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            
            // Set subscription status - default to true to avoid database errors
            ViewBag.HasActiveSubscription = true;
            
            // Get Sales users with their profiles - filtered by role and ChannelPartnerId
            var salesUsersQuery = _db.Users.Where(u => u.Role == "Sales" || u.Role == "Agent");
            
            // Filter executives based on current user's role
            if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
            {
                // Sales/Agent sees only themselves
                salesUsersQuery = salesUsersQuery.Where(u => u.UserId == UserId);
            }
            else if (role?.ToLower() == "partner")
            {
                // Partner sees only their organization's executives
                salesUsersQuery = salesUsersQuery.Where(u => u.ChannelPartnerId == channelPartnerId);
            }
            else if (role?.ToLower() == "admin")
            {
                // Admin sees only their own executives (where ChannelPartnerId is null)
                salesUsersQuery = salesUsersQuery.Where(u => u.ChannelPartnerId == null);
            }
            
            var salesUsers = salesUsersQuery.ToList();

            // Include current user (Admin/Partner) in the executives list so they see their own name
            if ((role?.ToLower() == "admin" || role?.ToLower() == "partner") && !salesUsers.Any(u => u.UserId == UserId))
            {
                var self = _db.Users.FirstOrDefault(u => u.UserId == UserId);
                if (self != null) salesUsers.Insert(0, self);
            }

            var salesUserIds = salesUsers.Select(u => u.UserId).ToList();
            var userProfiles = _db.UserProfiles.Where(up => salesUserIds.Contains(up.UserId)).ToList();
            
            // Create executive list with full names
            var executives = salesUsers.Select(u => new {
                u.UserId,
                u.Username,
                Profile = userProfiles.FirstOrDefault(p => p.UserId == u.UserId),
                FullName = userProfiles.FirstOrDefault(p => p.UserId == u.UserId) != null
                    ? $"{userProfiles.FirstOrDefault(p => p.UserId == u.UserId).FirstName} {userProfiles.FirstOrDefault(p => p.UserId == u.UserId).LastName}".Trim()
                    : u.Username
            }).ToList();
            
            ViewBag.Executives = executives;

            return View(leads);
        }

        // Helper to build filtered query (used by Index and export actions)
        private IQueryable<LeadModel> BuildFilteredQuery(string search, string stage, string status, int? executiveId, string source, string type, string propertyType, DateTime? from, DateTime? to, string groupName = "", string preferredLocation = "", string handoverStatus = "")
        {
            var q = _db.Leads.AsQueryable();
            var (UserId, Role) = GetUserFromToken();

            var role = Role;
            var curUserId = UserId;
            
            // Get current user's ChannelPartnerId
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == UserId);
            var channelPartnerId = currentUser?.ChannelPartnerId;
            
            if (role?.ToLower() == "admin")
            {
                // Admin sees their own leads + partner leads (ReadyToBook or HandedOver)
                q = q.Where(l => l.ChannelPartnerId == null || 
                               l.HandoverStatus == "ReadyToBook" || 
                               l.HandoverStatus == "HandedOver");
            }
            else if (channelPartnerId.HasValue)
            {
                // Partner org users (partner + partner-side agent/sales) see their org leads.
                q = q.Where(l => l.ChannelPartnerId == channelPartnerId);
            }
            else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
            {
                // Sales/Agent sees only their assigned leads
                q = q.Where(l => l.ExecutiveId == UserId);
            }

            if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Name.Contains(search));
            if (!string.IsNullOrWhiteSpace(stage)) q = q.Where(x => x.Stage == stage);
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
            if (executiveId.HasValue)
            {
                if (channelPartnerId.HasValue)
                {
                    q = q.Where(x => (x.PartnerAssignedAgentUserId ?? x.ExecutiveId) == executiveId.Value);
                }
                else
                {
                    q = q.Where(x => x.ExecutiveId == executiveId.Value);
                }
            }
            if (!string.IsNullOrWhiteSpace(source)) q = q.Where(x => x.Source == source);
            if (!string.IsNullOrWhiteSpace(type)) q = q.Where(x => x.Type == type);
            if (!string.IsNullOrWhiteSpace(propertyType)) q = q.Where(x => x.PropertyType == propertyType);
            if (!string.IsNullOrWhiteSpace(groupName)) q = q.Where(x => x.GroupName != null && x.GroupName.Contains(groupName));
            if (!string.IsNullOrWhiteSpace(preferredLocation)) q = q.Where(x => x.PreferredLocation != null && x.PreferredLocation.Contains(preferredLocation));
            if (!string.IsNullOrWhiteSpace(handoverStatus)) q = q.Where(x => x.HandoverStatus == handoverStatus);
            if (from.HasValue) q = q.Where(x => x.CreatedOn >= from.Value.Date);
            if (to.HasValue) q = q.Where(x => x.CreatedOn <= to.Value.Date.AddDays(1).AddSeconds(-1));

            return q;
        }

        [HttpGet]
        [PermissionAuthorize("Export")]
        public IActionResult ExportExcel(string search = "", string stage = "", string status = "", int? executiveId = null, string source = "", string type = "", string propertyType = "", DateTime? from = null, DateTime? to = null, string groupName = "", string preferredLocation = "", string cols = null, string sortBy = "", string sortDir = "desc")
        {
            var q = BuildFilteredQuery(search, stage, status, executiveId, source, type, propertyType, from, to, groupName, preferredLocation);
            // apply sorting if present
            if (!string.IsNullOrWhiteSpace(sortBy))
            {
                var dir = (sortDir ?? "desc").ToLower();
                q = sortBy.ToLower() switch
                {
                    "name" => dir == "asc" ? q.OrderBy(x => x.Name) : q.OrderByDescending(x => x.Name),
                    _ => dir == "asc" ? q.OrderBy(x => x.CreatedOn) : q.OrderByDescending(x => x.CreatedOn),
                };
            }

            var data = q.ToList();

            var selected = ParseColumns(cols);

            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Leads");
                int c = 1;
                // headers
                foreach (var col in selected)
                {
                    ws.Cell(1, c++).SetValue(col.Header ?? string.Empty);
                }

                int r = 2;
                foreach (var item in data)
                {
                    c = 1;
                    foreach (var col in selected)
                    {
                        var val = GetLeadColumnValue(item, col.Key);
                        ws.Cell(r, c++).SetValue(val?.ToString() ?? string.Empty);
                    }
                    r++;
                }

                using (var ms = new MemoryStream())
                {
                    wb.SaveAs(ms);
                    return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Leads.xlsx");
                }
            }
        }

        [HttpGet]
        [PermissionAuthorize("Export")]
        public IActionResult ExportCsv(string search = "", string stage = "", string status = "", int? executiveId = null, string source = "", string type = "", string propertyType = "", DateTime? from = null, DateTime? to = null, string groupName = "", string preferredLocation = "", string cols = null, string sortBy = "", string sortDir = "desc")
        {
            var q = BuildFilteredQuery(search, stage, status, executiveId, source, type, propertyType, from, to, groupName, preferredLocation);
            var data = q.ToList();
            var selected = ParseColumns(cols);

            var sb = new StringWriter();
            // header
            sb.WriteLine(string.Join(",", selected.Select(s => QuoteCsv(s.Header))));
            foreach (var item in data)
            {
                var row = selected.Select(s => QuoteCsv(Convert.ToString(GetLeadColumnValue(item, s.Key))));
                sb.WriteLine(string.Join(",", row));
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", "Leads.csv");
        }

        private string QuoteCsv(string s)
        {
            if (s == null) return "";
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private List<(string Key, string Header)> ParseColumns(string cols)
        {
            // cols is comma-separated like .col-name,.col-stage
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { ".col-name", "Name" },
                { ".col-stage", "Stage" },
                { ".col-status", "Status" },
                { ".col-group", "Group" },
                { ".col-source", "Source" },
                { ".col-location", "PreferredLocation" },
                { ".col-type", "Type" },
                { ".col-property", "PropertyType" },
                { ".col-exec", "Executive" },
                { ".col-followup", "FollowUpDate" },
            };

            var res = new List<(string Key, string Header)>();
            if (string.IsNullOrWhiteSpace(cols))
            {
                // default set: return selector keys with headers
                foreach (var kv in map) res.Add((kv.Key, kv.Value));
                return res;
            }

            var parts = cols.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList();
            foreach (var p in parts)
            {
                if (map.TryGetValue(p, out var header)) res.Add((p, header));
            }
            // if none matched, fallback to full set
            if (!res.Any()) foreach (var kv in map) res.Add((kv.Key, kv.Value));
            return res;
        }

        private object GetLeadColumnValue(LeadModel lead, string key)
        {
            try
            {
                switch (key)
                {
                    case ".col-name": return lead.Name ?? "-";
                    case ".col-stage": return lead.Stage ?? "-";
                    case ".col-status": return lead.Status ?? "-";
                    case ".col-group": return lead.GroupName ?? "-";
                    case ".col-source": return lead.Source ?? "-";
                    case ".col-location": return lead.PreferredLocation ?? "-";
                    case ".col-type": return lead.Type ?? "-";
                    case ".col-property": return lead.PropertyType ?? "-";
                    case ".col-bhk": return lead.BHK ?? "-";
                    case ".col-sqft": return lead.Sqft ?? "-";
                    case ".col-facing": return lead.Facing ?? "-";

                    case ".col-exec":
                        return _db.Users
                            .Where(u => u.UserId == lead.ExecutiveId)
                            .Select(u => u.Username)
                            .FirstOrDefault() ?? "-";

                    case ".col-followup":
                        return lead.FollowUpDate.HasValue
                               ? lead.FollowUpDate.Value.ToString("dd-MMM-yyyy")
                               : "-";

                    default: return "-";
                }
            }
            catch
            {
                return "-";
            }
        }

        [HttpPost]
        [PermissionAuthorize("Create")]
        public async Task<IActionResult> SaveLead([FromBody] LeadModel model)
        {
            try
            {
                // Check if this is an AJAX request
                bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
                
                ModelState.Remove("Rating");
                ModelState.Remove("Comments");
                if (!ModelState.IsValid)
                {
                    if (isAjax)
                    {
                        return Json(new { success = false, message = "Invalid lead data" });
                    }
                    TempData["Error"] = "Invalid lead data";
                    return RedirectToAction("Index");
                }

                // Get current user ID using the standard method
                var currentUserId = _getCurrentUserId();
                if (currentUserId == 0)
                {
                    if (isAjax)
                    {
                        return Json(new { success = false, message = "Unable to identify current user" });
                    }
                    TempData["Error"] = "Unable to identify current user";
                    return RedirectToAction("Index");
                }

                var currentUser = _db.Users.FirstOrDefault(u => u.UserId == currentUserId);
                var userRole = _getCurrentUserRole();
                var isPartnerTeamUser = userRole?.ToLower() == "partner" || currentUser?.ChannelPartnerId != null;

                if (model.LeadId == 0)
                {
                    // Check email uniqueness before creating
                    if (!string.IsNullOrWhiteSpace(model.Email))
                    {
                        var emailQuery = _db.Leads.Where(l => l.Email != null && l.Email.ToLower() == model.Email.Trim().ToLower());
                        if (currentUser?.ChannelPartnerId.HasValue == true)
                            emailQuery = emailQuery.Where(l => l.ChannelPartnerId == currentUser.ChannelPartnerId);
                        else
                            emailQuery = emailQuery.Where(l => l.ChannelPartnerId == null);

                        if (emailQuery.Any())
                        {
                            if (isAjax)
                                return Json(new { success = false, message = "A lead with this email already exists." });
                            TempData["Error"] = "A lead with this email already exists.";
                            return RedirectToAction("Index");
                        }
                    }

                    // Check subscription limits for new leads
                    if (currentUser?.ChannelPartnerId != null)
                    {
                        var (canAdd, message) = await _subscriptionService.CanAddLeadAsync(currentUser.ChannelPartnerId.Value);
                        if (!canAdd)
                        {
                            // Check if this is an AJAX request for sweet alert
                            if (isAjax)
                            {
                                // Get available plans for upgrade options
                                var availablePlans = await _subscriptionService.GetAvailablePlansAsync();
                                return Json(new { 
                                    success = false, 
                                    planExpired = true,
                                    message = message,
                                    availablePlans = availablePlans.Select(p => new {
                                        planId = p.PlanId,
                                        planName = p.PlanName,
                                        monthlyPrice = p.MonthlyPrice,
                                        yearlyPrice = p.YearlyPrice,
                                        maxLeadsPerMonth = p.MaxLeadsPerMonth
                                    }).ToList()
                                });
                            }
                            
                            TempData["Error"] = message;
                            return RedirectToAction("Index");
                        }
                    }
                    
                    // New lead
                    // Auto-generate LeadId (MongoDbSet does not auto-increment int keys)
                    model.LeadId = _db.Leads.Any() ? await _db.Leads.MaxAsync(l => l.LeadId) + 1 : 1;
                    model.CreatedOn = IndianTime.Now;
                    model.CreatedBy = currentUserId;
                    
                    // Set ChannelPartnerId based on current user
                    model.ChannelPartnerId = currentUser?.ChannelPartnerId;
                    
                    // Set HandoverStatus based on user role
                    if (isPartnerTeamUser)
                    {
                        model.HandoverStatus = "Partner";
                        model.IsReadyToBook = false;
                        model.PartnerAssignedAgentUserId = model.ExecutiveId;
                    }
                    else
                    {
                        model.HandoverStatus = "Admin";
                    }
                    
                    _db.Leads.Add(model);
                    await _db.SaveChangesAsync();

                    // Add history entry
                    _db.LeadHistory.Add(new LeadHistoryModel
                    {
                        LeadId = model.LeadId,
                        Activity = "Lead created",
                        ExecutiveId = model.ExecutiveId,
                        ActivityDate = IndianTime.Now
                    });
                    await _db.SaveChangesAsync();

                    // Add audit log
                    _db.AuditLogs.Add(new AuditLogModel
                    {
                        UserId = currentUserId,
                        Action = "Create",
                        EntityType = "Lead",
                        EntityId = model.LeadId,
                        IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                        UserAgent = Request.Headers["User-Agent"].ToString(),
                        Timestamp = IndianTime.Now
                    });
                    await _db.SaveChangesAsync();

                    // Send notification for new lead
                    try
                    {
                        await _notificationService.NotifyLeadAddedAsync(model.LeadId, model.Name ?? "Unknown", model.Source ?? "Manual");
                    }
                    catch { }

                    if (isAjax)
                    {
                        return Json(new { success = true, message = "Lead created successfully!", leadId = model.LeadId, encodedId = IdObfuscator.Encode(model.LeadId) });
                    }
                    TempData["Success"] = "Lead created successfully!";
                }
                else
                {
                    // Update existing lead
                    var existing = _db.Leads.FirstOrDefault(l => l.LeadId == model.LeadId);
                    if (existing == null)
                    {
                        if (isAjax)
                        {
                            return Json(new { success = false, message = "Lead not found" });
                        }
                        return NotFound();
                    }

                    // Check email uniqueness when updating
                    if (!string.IsNullOrWhiteSpace(model.Email))
                    {
                        var emailQuery = _db.Leads.Where(l => l.Email != null && l.Email.ToLower() == model.Email.Trim().ToLower() && l.LeadId != model.LeadId);
                        if (currentUser?.ChannelPartnerId.HasValue == true)
                            emailQuery = emailQuery.Where(l => l.ChannelPartnerId == currentUser.ChannelPartnerId);
                        else
                            emailQuery = emailQuery.Where(l => l.ChannelPartnerId == null);

                        if (emailQuery.Any())
                        {
                            if (isAjax)
                                return Json(new { success = false, message = "A lead with this email already exists." });
                            TempData["Error"] = "A lead with this email already exists.";
                            return RedirectToAction("Index");
                        }
                    }

                    // Update fields
                    existing.Name = model.Name;
                    existing.Contact = model.Contact;
                    existing.Email = model.Email;
                    existing.Stage = model.Stage;
                    existing.Status = model.Status;
                    existing.GroupName = model.GroupName;
                    existing.Source = model.Source;
                    existing.PreferredLocation = model.PreferredLocation;
                    existing.Sqft = model.Sqft;
                    existing.Facing = model.Facing;
                    existing.Type = model.Type;
                    existing.PropertyType = model.PropertyType;
                    existing.BHK = model.BHK;
                    existing.LocationDistance = model.LocationDistance;
                    existing.Requirement = model.Requirement;
                    existing.ExecutiveId = model.ExecutiveId;
                    existing.FollowUpDate = model.FollowUpDate;
                    existing.Rating = model.Rating;
                    existing.Comments = model.Comments;
                    existing.ModifiedOn = IndianTime.Now;

                    // Keep partner-side assignment history independent from admin reassignment.
                    if (isPartnerTeamUser && existing.ChannelPartnerId == currentUser?.ChannelPartnerId)
                    {
                        existing.PartnerAssignedAgentUserId = model.ExecutiveId;
                    }

                    _db.Leads.Update(existing);
                    await _db.SaveChangesAsync();

                    // Add audit log for update
                    _db.AuditLogs.Add(new AuditLogModel
                    {
                        UserId = currentUserId,
                        Action = "Update",
                        EntityType = "Lead",
                        EntityId = existing.LeadId,
                        IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                        UserAgent = Request.Headers["User-Agent"].ToString(),
                        Timestamp = IndianTime.Now
                    });
                    await _db.SaveChangesAsync();

                    if (isAjax)
                    {
                        return Json(new { success = true, message = "Lead updated successfully!" });
                    }
                    TempData["Success"] = "Lead updated successfully!";
                }

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
                if (isAjax)
                {
                    return Json(new { success = false, message = $"Error saving lead: {ex.Message}" });
                }
                TempData["Error"] = $"Error saving lead: {ex.Message}";
                return RedirectToAction("Index");
            }
        }
        [HttpGet]
        public IActionResult CheckEmailUnique(string email, int? leadId)
        {
            if (string.IsNullOrWhiteSpace(email))
                return Json(new { isUnique = true });

            var (UserId, Role) = GetUserFromToken();
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == UserId);
            var channelPartnerId = currentUser?.ChannelPartnerId;

            var query = _db.Leads.Where(l => l.Email != null && l.Email.ToLower() == email.Trim().ToLower());

            // Scope: Partner group = ChannelPartnerId matches, Admin group = ChannelPartnerId is null
            if (channelPartnerId.HasValue)
                query = query.Where(l => l.ChannelPartnerId == channelPartnerId);
            else
                query = query.Where(l => l.ChannelPartnerId == null);

            // Exclude current lead when editing
            if (leadId.HasValue && leadId.Value > 0)
                query = query.Where(l => l.LeadId != leadId.Value);

            var exists = query.Any();
            return Json(new { isUnique = !exists });
        }

        [HttpGet]
        [PermissionAuthorize("View")]
        public IActionResult GetLead(int id)
        {
            var lead = _db.Leads.FirstOrDefault(l => l.LeadId == id);
            if (lead == null) return NotFound();
            return Json(lead);
        }


        [HttpGet]
        [PermissionAuthorize("View")]

        public IActionResult GetLeadDetails(int id)
        {
            var lead = _db.Leads.FirstOrDefault(x => x.LeadId == id);
            if (lead == null) return NotFound();
            
            var (UserId, Role) = GetUserFromToken();
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == UserId);
            var channelPartnerId = currentUser?.ChannelPartnerId;
            
            // Filter executives based on current user's role
            var executivesQuery = _db.Users.Where(u => u.Role == "Sales" || u.Role == "Agent");
            if (Role?.ToLower() == "sales" || Role?.ToLower() == "agent")
            {
                executivesQuery = executivesQuery.Where(u => u.UserId == UserId);
            }
            else if (Role?.ToLower() == "partner")
            {
                executivesQuery = executivesQuery.Where(u => u.ChannelPartnerId == channelPartnerId);
            }
            else if (Role?.ToLower() == "admin")
            {
                executivesQuery = executivesQuery.Where(u => u.ChannelPartnerId == null);
            }
            
            var execList = executivesQuery.ToList();
            if ((Role?.ToLower() == "admin" || Role?.ToLower() == "partner") && !execList.Any(u => u.UserId == UserId))
            {
                var self = _db.Users.FirstOrDefault(u => u.UserId == UserId);
                if (self != null) execList.Insert(0, self);
            }
            ViewBag.Executives = execList;
            return PartialView("_LeadDetailsSidebar", lead);
        }

        [HttpPost]
        [PermissionAuthorize("Edit")]
        public async Task<IActionResult> SaveFollowUp([FromBody] FollowUpModel model)
        {
            try
            {
                var (UserId, Role) = GetUserFromToken();

                if (model == null)
                {
                    return Json(new { success = false, message = "Invalid follow-up data" });
                }

                // Use provided ExecutiveId if available, otherwise use current user
                if (model.ExecutiveId == 0)
                {
                    model.ExecutiveId = Convert.ToInt32(UserId);
                }

                // Auto-generate FollowUpId (MongoDbSet does not auto-increment int keys)
                model.FollowUpId = _db.LeadFollowUps.Any() ? await _db.LeadFollowUps.MaxAsync(f => f.FollowUpId) + 1 : 1;
                model.CreatedOn = IndianTime.Now;

                _db.LeadFollowUps.Add(model);
                _db.LeadHistory.Add(new LeadHistoryModel
                {
                    LeadId = model.LeadId,
                    Activity = $"Follow-up added - Stage: {model.Stage}, Status: {model.Status}, Scheduled: {(model.FollowUpDate.HasValue ? model.FollowUpDate.Value.ToString("MMM dd, yyyy") : "")} {(!string.IsNullOrEmpty(model.FollowUpTime) && TimeSpan.TryParse(model.FollowUpTime, out var ts) ? DateTime.Today.Add(ts).ToString("hh:mm tt") : model.FollowUpTime ?? "")}",
                    ExecutiveId = UserId,
                    ActivityDate = IndianTime.Now
                });
                _db.SaveChanges();

                // **SCHEDULE THE FOLLOW-UP NOTIFICATION** if date and time are provided
                if (model.FollowUpDate.HasValue && !string.IsNullOrEmpty(model.FollowUpTime))
                {
                    try
                    {
                        // Call the scheduling API to schedule the notification
                        using var httpClient = new HttpClient();
                        var baseUrl = $"{Request.Scheme}://{Request.Host}";
                        var scheduleUrl = $"{baseUrl}/api/ScheduledNotification/schedule-followup-notification";
                        
                        var scheduleData = new
                        {
                            FollowUpId = model.FollowUpId,
                            LeadId = model.LeadId,
                            Stage = model.Stage,
                            Status = model.Status,
                            FollowUpDate = model.FollowUpDate,
                            FollowUpTime = model.FollowUpTime,
                            Comments = model.Comments,
                            ExecutiveId = model.ExecutiveId
                        };
                        
                        var json = System.Text.Json.JsonSerializer.Serialize(scheduleData);
                        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                        
                        var response = await httpClient.PostAsync(scheduleUrl, content);
                        var responseText = await response.Content.ReadAsStringAsync();
                        
                        Console.WriteLine($"SCHEDULE-FOLLOWUP: Response={response.StatusCode}, Content={responseText}");
                        
                        if (response.IsSuccessStatusCode)
                        {
                            Console.WriteLine($"SCHEDULE-FOLLOWUP: Successfully scheduled notification for FollowUpId {model.FollowUpId} at {model.FollowUpTime}");
                        }
                        else
                        {
                            Console.WriteLine($"SCHEDULE-FOLLOWUP: Failed to schedule - {responseText}");
                        }
                    }
                    catch (Exception scheduleEx)
                    {
                        Console.WriteLine($"SCHEDULE-FOLLOWUP ERROR: {scheduleEx.Message}");
                    }
                }

                // Update lead stage/status/last followup date
                var lead = _db.Leads.FirstOrDefault(l => l.LeadId == model.LeadId);
                if (lead != null)
                {
                    lead.Stage = model.Stage;
                    lead.Status = model.Status;
                    lead.FollowUpDate = model.FollowUpDate;
                    lead.Rating = model.Rating?.ToString();
                    lead.ModifiedOn = IndianTime.Now;
                    _db.Leads.Update(lead);
                    _db.SaveChanges();
                }

                // Send notification for follow-up
                try
                {
                    var (currentUserId, _) = GetUserFromToken();
                    var lead2 = _db.Leads.AsNoTracking().FirstOrDefault(l => l.LeadId == model.LeadId);
                    Console.WriteLine($"FOLLOWUP-NOTIF: LeadId={model.LeadId}, Lead found={lead2 != null}, Lead name={lead2?.Name}");
                    if (lead2 != null)
                    {
                        // Notify the assigned executive
                        if (lead2.ExecutiveId.HasValue)
                        {
                            Console.WriteLine($"FOLLOWUP-NOTIF: Sending to executive UserId={lead2.ExecutiveId.Value}");
                            await _notificationService.CreateNotificationAsync(
                                "Follow-up Added",
                                $"Follow-up added for lead '{lead2.Name}' - Stage: {model.Stage}, Status: {model.Status}",
                                "FollowUp",
                                lead2.ExecutiveId.Value,
                                $"/leaddetails/{IdObfuscator.Encode(model.LeadId)}#scrollspyFollowups",
                                model.FollowUpId,
                                "FollowUp",
                                "Normal"
                            );
                        }
                        // Notify all admins
                        var admins = _db.Users.Where(u => u.Role == "Admin" && u.ChannelPartnerId == null).ToList();
                        Console.WriteLine($"FOLLOWUP-NOTIF: Found {admins.Count} admins");
                        foreach (var admin in admins)
                        {
                            if (lead2.ExecutiveId.HasValue && admin.UserId == lead2.ExecutiveId.Value) continue;
                            Console.WriteLine($"FOLLOWUP-NOTIF: Sending to admin UserId={admin.UserId}");
                            await _notificationService.CreateNotificationAsync(
                                "Follow-up Added",
                                $"Follow-up added for lead '{lead2.Name}' - Stage: {model.Stage}, Status: {model.Status}",
                                "FollowUp",
                                admin.UserId,
                                $"/leaddetails/{IdObfuscator.Encode(model.LeadId)}#scrollspyFollowups",
                                model.FollowUpId,
                                "FollowUp",
                                "Normal"
                            );
                        }
                    }
                }
                catch (Exception notifEx)
                {
                    Console.WriteLine($"Follow-up notification error: {notifEx.Message}");
                }

                return Json(new { success = true, message = "Follow-up saved successfully!" });
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException?.Message ?? ex.Message;
                return Json(new { success = false, message = $"Error saving follow-up: {innerMsg}" });
            }
        }

        [HttpGet]
        public IActionResult GetFollowups(int leadId)
        {
            try
            {
                // Get the main lead's follow-up date
                var lead = _db.Leads.AsNoTracking().FirstOrDefault(l => l.LeadId == leadId);
                
                // Get follow-ups for the lead with null handling
                var followups = _db.LeadFollowUps
                    .Where(x => x.LeadId == leadId)
                    .Select(x => new FollowUpModel
                    {
                        FollowUpId = x.FollowUpId,
                        LeadId = x.LeadId,
                        ExecutiveId = x.ExecutiveId,
                        FollowUpDate = x.FollowUpDate,
                        FollowUpTime = x.FollowUpTime,
                        CreatedOn = x.CreatedOn,
                        Stage = x.Stage ?? "",
                        Status = x.Status ?? "",
                        Comments = x.Comments ?? "",
                        Rating = x.Rating
                    })
                    .ToList();
                
                // Add main lead's follow-up date as a virtual record if it exists
                if (lead?.FollowUpDate.HasValue == true)
                {
                    // Remove any existing follow-up records with the same date to avoid duplicates
                    followups = followups.Where(f => f.FollowUpDate?.Date != lead.FollowUpDate.Value.Date).ToList();
                    
                    followups.Add(new FollowUpModel
                    {
                        FollowUpId = 0, // Virtual record
                        LeadId = leadId,
                        ExecutiveId = lead.ExecutiveId ?? 0,
                        FollowUpDate = lead.FollowUpDate,
                        CreatedOn = IndianTime.Now, // Use current time to make it appear first
                        Stage = lead.Stage ?? "Current Task",
                        Status = lead.Status ?? "Active",
                        Comments = "Updated from Tasks page",
                        Rating = null
                    });
                }
                
                followups = followups.OrderByDescending(x => x.FollowUpDate ?? x.CreatedOn).ToList();

                // Get all unique executive IDs from follow-ups
                var execIds = followups
                    .Select(x => x.ExecutiveId)
                    .Distinct()
                    .ToList();

                // Get user profiles for images
                var profiles = _db.UserProfiles
                    .Where(p => execIds.Contains(p.UserId))
                    .ToList();

                // Get executive names for display
                var executives = _db.Users
                    .Where(u => execIds.Contains(u.UserId))
                    .ToList();

                ViewBag.UserProfiles = profiles;
                ViewBag.Executives = executives;

                return PartialView("_FollowupListPartial", followups);
            }
            catch (Exception ex)
            {
                return Content($"<div class='alert alert-danger'>Failed to load follow-ups: {ex.Message}</div>");
            }
        }




        // Save note (AJAX)
        [HttpPost]
        [PermissionAuthorize("Edit")]
        public IActionResult SaveNote(int leadId, string noteText)
        {
            try
            {
                var execId = _getCurrentUserId();

                var note = new LeadNoteModel
                {
                    LeadId = leadId,
                    NoteText = noteText,
                    ExecutiveId = execId,
                    CreatedOn = IndianTime.Now
                };

                _db.LeadNotes.Add(note);

                _db.LeadHistory.Add(new LeadHistoryModel
                {
                    LeadId = leadId,
                    Activity = "Note added",
                    ExecutiveId = execId,
                    ActivityDate = IndianTime.Now
                });

                _db.SaveChanges();

                return Json(new { success = true, message = "Note saved successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // Get notes
        [HttpGet]
        public IActionResult GetNotes(int leadId)
        {
            ViewBag.LeadId = leadId;

            var notes = _db.LeadNotes.Where(n => n.LeadId == leadId).OrderByDescending(n => n.CreatedOn).ToList();
            
            // Get all unique executive IDs from notes
            var execIds = notes
                .Where(n => n.ExecutiveId != null)
                .Select(n => n.ExecutiveId.Value)
                .Distinct()
                .ToList();

            // Get executives for display
            ViewBag.Executives = _db.Users.Where(u => execIds.Contains(u.UserId)).ToList();
            
            return PartialView("_NotesPartial", notes);
        }



        //[HttpPost]
        //public async Task<IActionResult> UploadLeadFile(IFormFile file)
        //{
        //    if (file == null || file.Length == 0)
        //        return RedirectToAction("Index");

        //    using (var ms = new MemoryStream())
        //    {
        //        await file.CopyToAsync(ms);
        //        ms.Position = 0;

        //        using (var workbook = new XLWorkbook(ms))
        //        {
        //            var worksheet = workbook.Worksheets.First();
        //            var lastRow = worksheet.LastRowUsed().RowNumber();

        //            for (int r = 2; r <= lastRow; r++) // assuming first row = headers
        //            {
        //                var name = worksheet.Cell(r, 1).GetString().Trim();
        //                if (string.IsNullOrEmpty(name)) continue;

        //                var lead = new LeadModel
        //                {
        //                    Name = name,
        //                    Contact = worksheet.Cell(r, 2).GetString().Trim(),
        //                    Email = worksheet.Cell(r, 3).GetString().Trim(),
        //                    Stage = worksheet.Cell(r, 4).GetString().Trim(),
        //                    Status = worksheet.Cell(r, 5).GetString().Trim(),
        //                    Source = worksheet.Cell(r, 6).GetString().Trim(),
        //                    PreferredLocation = worksheet.Cell(r, 7).GetString().Trim(),
        //                    CreatedBy = _getCurrentUserId(),
        //                    CreatedOn = IndianTime.Now
        //                };

        //                _db.Leads.Add(lead);
        //            }

        //            await _db.SaveChangesAsync();
        //        }
        //    }

        //    return RedirectToAction("Index");
        //}




        [PermissionAuthorize("View")]
        [Route("leaddetails/{id}")]
        public IActionResult Details(string id)
        {
            var decodedId = IdObfuscator.Decode(id);
            if (decodedId == null)
            {
                return NotFound();
            }

            ViewBag.EncodedId = id;

            // Force fresh query without caching

            var lead = _db.Leads
                          .AsNoTracking()
                          .FirstOrDefault(l => l.LeadId == decodedId.Value);

            if (lead == null)
                return NotFound();

            // Keep the main lead's FollowUpDate as-is (don't override with follow-up data)
            // The Tasks page updates this field directly, so we preserve it

            // Pass executives with full names for display - filtered by role
            var (currentUserId, currentRole) = GetUserFromToken();
            var currentUserData = _db.Users.FirstOrDefault(u => u.UserId == currentUserId);
            var currentChannelPartnerId = currentUserData?.ChannelPartnerId;
            var isPartnerTeamUser = currentRole?.ToLower() == "partner" || currentChannelPartnerId.HasValue;
            
            var salesUsersQuery = _db.Users.Where(u => u.Role == "Sales" || u.Role == "Agent");
            if (currentRole?.ToLower() == "sales" || currentRole?.ToLower() == "agent")
            {
                salesUsersQuery = salesUsersQuery.Where(u => u.UserId == currentUserId);
            }
            else if (currentRole?.ToLower() == "partner")
            {
                salesUsersQuery = salesUsersQuery.Where(u => u.ChannelPartnerId == currentChannelPartnerId);
            }
            else if (currentRole?.ToLower() == "admin")
            {
                salesUsersQuery = salesUsersQuery.Where(u => u.ChannelPartnerId == null);
            }
            
            var salesUsers = salesUsersQuery.ToList();

            // Include current user (Admin/Partner) in the executives list so they see their own name
            if ((currentRole?.ToLower() == "admin" || currentRole?.ToLower() == "partner") && !salesUsers.Any(u => u.UserId == currentUserId))
            {
                var self = _db.Users.FirstOrDefault(u => u.UserId == currentUserId);
                if (self != null) salesUsers.Insert(0, self);
            }

            // Ensure currently attributed assignees are available for display name resolution.
            var assignedUserIds = new List<int>();
            if (lead.ExecutiveId.HasValue)
            {
                assignedUserIds.Add(lead.ExecutiveId.Value);
            }
            if (lead.PartnerAssignedAgentUserId.HasValue)
            {
                assignedUserIds.Add(lead.PartnerAssignedAgentUserId.Value);
            }

            if (assignedUserIds.Count > 0)
            {
                var missingAssignedUsers = _db.Users
                    .Where(u => assignedUserIds.Contains(u.UserId) && !salesUsers.Select(s => s.UserId).Contains(u.UserId))
                    .ToList();

                if (missingAssignedUsers.Count > 0)
                {
                    salesUsers.AddRange(missingAssignedUsers);
                }
            }

            var salesUserIds = salesUsers.Select(u => u.UserId).ToList();
            var userProfiles = _db.UserProfiles.Where(up => salesUserIds.Contains(up.UserId)).ToList();

            var executives = salesUsers.Select(u =>
            {
                var profile = userProfiles.FirstOrDefault(p => p.UserId == u.UserId);
                var fullName = u.Username;
                if (profile != null && !string.IsNullOrWhiteSpace(profile.FirstName))
                {
                    fullName = $"{profile.FirstName} {profile.LastName}".Trim();
                }
                return new
                {
                    u.UserId,
                    u.Username,
                    FullName = fullName
                };
            }).ToList();

            ViewBag.Executives = executives;
            ViewBag.IsPartnerTeamUser = isPartnerTeamUser;

            // Load initial follow-ups data for the page - handle null values
            var followups = _db.LeadFollowUps
                .Where(x => x.LeadId == decodedId.Value)
                .Select(x => new FollowUpModel
                {
                    FollowUpId = x.FollowUpId,
                    LeadId = x.LeadId,
                    ExecutiveId = x.ExecutiveId,
                    FollowUpDate = x.FollowUpDate,
                    CreatedOn = x.CreatedOn,
                    Stage = x.Stage ?? "",
                    Status = x.Status ?? "",
                    Comments = x.Comments ?? "",
                    Rating = x.Rating
                })
                .ToList();
            
            // Add main lead's follow-up date as a virtual record if it exists
            if (lead.FollowUpDate.HasValue)
            {
                // Remove any existing follow-up records with the same date to avoid duplicates
                followups = followups.Where(f => f.FollowUpDate?.Date != lead.FollowUpDate.Value.Date).ToList();
                
                followups.Add(new FollowUpModel
                {
                    FollowUpId = 0, // Virtual record
                    LeadId = decodedId.Value,
                    ExecutiveId = lead.ExecutiveId ?? 0,
                    FollowUpDate = lead.FollowUpDate,
                    CreatedOn = IndianTime.Now, // Use current time to make it appear first
                    Stage = lead.Stage ?? "Current Task",
                    Status = lead.Status ?? "Active",
                    Comments = "Updated from Tasks page",
                    Rating = null
                });
            }
            
            followups = followups.OrderByDescending(x => x.FollowUpDate ?? x.CreatedOn).ToList();

            var execIds = followups.Select(x => x.ExecutiveId).Distinct().ToList();
            var profiles = _db.UserProfiles.Where(p => execIds.Contains(p.UserId)).ToList();
            var execs = _db.Users.Where(u => execIds.Contains(u.UserId)).ToList();

            ViewBag.UserProfiles = profiles;
            ViewBag.FollowupExecutives = execs;
            ViewBag.InitialFollowups = followups;

            return View(lead); // Single LeadModel
        }

        // Matching properties count (stub)
        [HttpGet]
        public IActionResult GetMatchingCount(int leadId)
        {
            // TODO: real implementation. For now, return approximate count
            // Example: match by PreferredLocation / BHK / PropertyType
            var lead = _db.Leads.FirstOrDefault(l => l.LeadId == leadId);
            int count = 0;
            if (lead != null)
            {
                // placeholder logic (replace with actual projects tableSearch)
                count = new Random().Next(0, 10);
            }
            return Json(new { count });
        }

        // Matching properties partial (stub)
        [HttpGet]
        public IActionResult GetMatchingProperties(int leadId)
        {
            var sample = new List<dynamic>
            {
                new { Id = 1, Name = "Green Meadows", Location = "Sector 1", Price="₹50L" },
                new { Id = 2, Name = "Skyline Apartments", Location="Sector 3", Price="₹75L" }
            };
            return PartialView("_MatchingPropertiesPartial", sample);
        }

        // Get uploads for a lead
        [HttpGet]
        public IActionResult GetUploads(int leadId)
        {

            ViewBag.LeadId = leadId;

            var ups = _db.LeadUploads.Where(u => u.LeadId == leadId).OrderByDescending(u => u.UploadedOn).ToList();

            return PartialView("_UploadsPartial", ups);
        }

        // Delete upload
        [HttpPost]
        public IActionResult DeleteUpload(int uploadId)
        {
            try
            {
                var upload = _db.LeadUploads.FirstOrDefault(u => u.UploadId == uploadId);
                if (upload == null)
                {
                    return Json(new { success = false, message = "File not found" });
                }

                var leadId = upload.LeadId;
                _db.LeadUploads.Remove(upload);

                // Add history entry if leadId exists
                if (leadId.HasValue)
                {
                    _db.LeadHistory.Add(new LeadHistoryModel
                    {
                        LeadId = leadId.Value,
                        Activity = $"File deleted: {upload.FileName}",
                        ExecutiveId = _getCurrentUserId(),
                        ActivityDate = IndianTime.Now
                    });
                }

                _db.SaveChanges();

                return Json(new { success = true, message = "File deleted successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }
        [HttpGet]
        public IActionResult DownloadLeadUploadSample()
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Lead Upload Sample");

                // Headers
                var headers = new string[]
                {
            "Name", "Contact", "Email", "Stage", "Status", "Group", "Source",
            "Preferred Location", "Sqft", "Facing", "Type", "Property Type",
            "BHK", "Location Distance", "Requirement", "Executive", "Follow Up Date"
                };

                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cell(1, i + 1).Value = headers[i];
                    worksheet.Cell(1, i + 1).Style.Font.Bold = true;
                    worksheet.Column(i + 1).AdjustToContents();
                }

                // Optional: sample data row
                worksheet.Cell(2, 1).Value = "Test Lead";
                worksheet.Cell(2, 2).Value = "9999999999";
                worksheet.Cell(2, 3).Value = "test@example.com";
                worksheet.Cell(2, 4).Value = "Site Visit Requested";
                worksheet.Cell(2, 5).Value = "Active";
                worksheet.Cell(2, 6).Value = "Group A";
                worksheet.Cell(2, 7).Value = "Website";
                worksheet.Cell(2, 8).Value = "Hyderabad";
                worksheet.Cell(2, 9).Value = "1200";
                worksheet.Cell(2, 10).Value = "North";
                worksheet.Cell(2, 11).Value = "Residential";
                worksheet.Cell(2, 12).Value = "Apartment";
                worksheet.Cell(2, 13).Value = "3BHK";
                worksheet.Cell(2, 14).Value = "5km";
                worksheet.Cell(2, 15).Value = "Immediate";
                worksheet.Cell(2, 16).Value = "John Doe";
                worksheet.Cell(2, 17).Value = IndianTime.Now.ToString("dd-MMM-yyyy");

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(
                        content,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        "LeadUploadSample.xlsx");
                }
            }
        }

        [HttpGet]
        public IActionResult ExportPrintable(string search = "", string stage = "", string status = "", int? executiveId = null, string source = "", string type = "", string propertyType = "", DateTime? from = null, DateTime? to = null, string groupName = "", string preferredLocation = "", string cols = null, string sortBy = "", string sortDir = "desc")
        {
            var q = BuildFilteredQuery(search, stage, status, executiveId, source, type, propertyType, from, to, groupName, preferredLocation);
            var data = q.ToList();
            var selected = ParseColumns(cols);

            // prepare rows as strings to make view simple
            var headers = selected.Select(s => s.Header).ToList();
            var rows = data.Select(d => selected.Select(s => Convert.ToString(GetLeadColumnValue(d, s.Key))).ToList()).ToList();

            ViewBag.Headers = headers;
            ViewBag.Rows = rows;
            ViewBag.Title = "Leads Export";

            return View("ExportPrintable");
        }

        [HttpGet]
        public IActionResult SearchAjax(string search = "", int page = 1, int pageSize = 7,
            string stage = "", string status = "", int? executiveId = null,
            string source = "", string type = "", string propertyType = "", DateTime? from = null, DateTime? to = null,
            string groupName = "", string preferredLocation = "",
            string sortBy = "", string sortDir = "desc")
        {
            var q = BuildFilteredQuery(search, stage, status, executiveId, source, type, propertyType, from, to, groupName, preferredLocation);
            var (userId, role) = GetUserFromToken();
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;

            // apply sorting
            if (!string.IsNullOrWhiteSpace(sortBy))
            {
                var dir = (sortDir ?? "desc").ToLower();
                q = sortBy.ToLower() switch
                {
                    "name" => dir == "asc" ? q.OrderBy(x => x.Name) : q.OrderByDescending(x => x.Name),
                    "followupdate" => dir == "asc" ? q.OrderBy(x => x.FollowUpDate) : q.OrderByDescending(x => x.FollowUpDate),
                    "status" => dir == "asc" ? q.OrderBy(x => x.Status) : q.OrderByDescending(x => x.Status),
                    _ => dir == "asc" ? q.OrderBy(x => x.CreatedOn) : q.OrderByDescending(x => x.CreatedOn),
                };
            }
            else
            {
                q = q.OrderByDescending(x => x.CreatedOn);
            }

            var total = q.Count();
            var leads = q.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var rows = leads.Select(item => new {
                leadId = item.LeadId,
                name = item.Name,
                stage = item.Stage,
                status = item.Status,
                groupName = item.GroupName,
                source = item.Source,
                preferredLocation = item.PreferredLocation,
                type = item.Type,
                propertyType = item.PropertyType,
                executive = _db.Users.FirstOrDefault(u => u.UserId ==
                    ((channelPartnerId.HasValue && item.PartnerAssignedAgentUserId.HasValue)
                        ? item.PartnerAssignedAgentUserId
                        : item.ExecutiveId))?.Username ?? "-",
                followUpDate = item.FollowUpDate.HasValue ? item.FollowUpDate.Value.ToString("dd-MMM-yyyy") : ""
            }).ToList();

            return Json(new { rows = rows, total = total, page = page, pageSize = pageSize });
        }
        [HttpPost]
        //[PermissionAuthorize("BulkUpload")]
        public async Task<IActionResult> UploadLeadFile(IFormFile file)
        {
            // Always return JSON for AJAX requests
            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";
            
            try
            {
                if (file == null || file.Length == 0)
                {
                    var message = "Please select a file to upload";
                    if (isAjax) return Json(new { success = false, message });
                    TempData["Error"] = message;
                    return RedirectToAction("Index");
                }
                
                // Check file size (limit to 10MB)
                if (file.Length > 10 * 1024 * 1024)
                {
                    var message = "File size must be less than 10MB";
                    if (isAjax) return Json(new { success = false, message });
                    TempData["Error"] = message;
                    return RedirectToAction("Index");
                }
                
                // Check file extension
                var allowedExtensions = new[] { ".xlsx", ".xls" };
                var fileExtension = Path.GetExtension(file.FileName).ToLower();
                if (!allowedExtensions.Contains(fileExtension))
                {
                    var message = "Please upload only Excel files (.xlsx or .xls)";
                    if (isAjax) return Json(new { success = false, message });
                    TempData["Error"] = message;
                    return RedirectToAction("Index");
                }

                var (UserId, Role) = GetUserFromToken();
                var currentUser = _db.Users.FirstOrDefault(u => u.UserId == UserId);
                var uploaderName = currentUser?.Username ?? "System";

                // Check subscription limits for partners
                if (currentUser?.ChannelPartnerId != null)
                {
                    var activeSubscription = await _subscriptionService.GetActiveSubscriptionAsync(currentUser.ChannelPartnerId.Value);
                    if (activeSubscription?.Plan == null)
                    {
                        var message = "No active subscription found. Please subscribe to upload leads.";
                        if (isAjax) return Json(new { success = false, message });
                        TempData["Error"] = message;
                        return RedirectToAction("Index");
                    }
                }

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            ms.Position = 0;

            using var workbook = new XLWorkbook(ms);
            var sheet = workbook.Worksheets.First();
            var lastRow = sheet.LastRowUsed().RowNumber();
            
            // Count valid leads first
            int validLeadCount = 0;
            for (int r = 2; r <= lastRow; r++)
            {
                var name = sheet.Cell(r, 1).GetString().Trim();
                if (!string.IsNullOrEmpty(name)) validLeadCount++;
            }
            
            Console.WriteLine($"DEBUG: Found {validLeadCount} valid leads in file");

            // Check subscription limits upfront for the entire batch
            if (currentUser?.ChannelPartnerId != null && validLeadCount > 0)
            {
                var activeSubscription = await _subscriptionService.GetActiveSubscriptionAsync(currentUser.ChannelPartnerId.Value);
                if (activeSubscription?.Plan != null)
                {
                    var currentMonth = IndianTime.Now.Month;
                    var currentYear = IndianTime.Now.Year;
                    var currentLeadCount = await _db.Leads
                        .Where(l => l.ChannelPartnerId == currentUser.ChannelPartnerId &&
                                   l.CreatedOn.Month == currentMonth &&
                                   l.CreatedOn.Year == currentYear)
                        .CountAsync();
                    
                    var remainingLeads = activeSubscription.Plan.MaxLeadsPerMonth - currentLeadCount;
                    Console.WriteLine($"DEBUG: Current leads: {currentLeadCount}, Max allowed: {activeSubscription.Plan.MaxLeadsPerMonth}, Remaining: {remainingLeads}");
                    
                    if (remainingLeads <= 0)
                    {
                        Console.WriteLine($"DEBUG: Monthly lead limit already reached");
                        if (isAjax)
                        {
                            return Json(new { success = false, message = $"Monthly lead limit of {activeSubscription.Plan.MaxLeadsPerMonth} already reached. Please upgrade your plan." });
                        }
                        TempData["Error"] = $"Monthly lead limit of {activeSubscription.Plan.MaxLeadsPerMonth} already reached. Please upgrade your plan.";
                        return RedirectToAction("Index");
                    }
                    
                    if (validLeadCount > remainingLeads)
                    {
                        Console.WriteLine($"DEBUG: Bulk upload would exceed limit. Leads to upload: {validLeadCount}, Remaining: {remainingLeads}");
                        if (isAjax)
                        {
                            return Json(new { success = false, message = $"Cannot upload {validLeadCount} leads. Only {remainingLeads} leads remaining in your monthly limit of {activeSubscription.Plan.MaxLeadsPerMonth}. Please upgrade your plan." });
                        }
                        TempData["Error"] = $"Cannot upload {validLeadCount} leads. Only {remainingLeads} leads remaining in your monthly limit of {activeSubscription.Plan.MaxLeadsPerMonth}. Please upgrade your plan.";
                        return RedirectToAction("Index");
                    }
                }
            }

            var leadsToNotify = new List<(int leadId, string leadName, int executiveId)>();
            int processedCount = 0;

            for (int r = 2; r <= lastRow; r++) // Skip header
            {
                var name = sheet.Cell(r, 1).GetString().Trim();
                if (string.IsNullOrEmpty(name)) continue;

                var lead = new LeadModel
                {
                    Name = name,
                    Contact = sheet.Cell(r, 2).GetString().Trim(),
                    Email = sheet.Cell(r, 3).GetString().Trim(),
                    Stage = sheet.Cell(r, 4).GetString().Trim(),
                    Status = sheet.Cell(r, 5).GetString().Trim(),
                    GroupName = string.IsNullOrWhiteSpace(sheet.Cell(r, 6).GetString()) ? null : sheet.Cell(r, 6).GetString().Trim(),
                    Source = string.IsNullOrWhiteSpace(sheet.Cell(r, 7).GetString()) ? null : sheet.Cell(r, 7).GetString().Trim(),
                    PreferredLocation = string.IsNullOrWhiteSpace(sheet.Cell(r, 8).GetString()) ? null : sheet.Cell(r, 8).GetString().Trim(),
                    Sqft = string.IsNullOrWhiteSpace(sheet.Cell(r, 9).GetString()) ? null : sheet.Cell(r, 9).GetString().Trim(),
                    Facing = string.IsNullOrWhiteSpace(sheet.Cell(r, 10).GetString()) ? null : sheet.Cell(r, 10).GetString().Trim(),
                    Type = string.IsNullOrWhiteSpace(sheet.Cell(r, 11).GetString()) ? null : sheet.Cell(r, 11).GetString().Trim(),
                    PropertyType = string.IsNullOrWhiteSpace(sheet.Cell(r, 12).GetString()) ? null : sheet.Cell(r, 12).GetString().Trim(),
                    BHK = string.IsNullOrWhiteSpace(sheet.Cell(r, 13).GetString()) ? null : sheet.Cell(r, 13).GetString().Trim(),
                    LocationDistance = string.IsNullOrWhiteSpace(sheet.Cell(r, 14).GetString()) ? null : sheet.Cell(r, 14).GetString().Trim(),
                    Requirement = string.IsNullOrWhiteSpace(sheet.Cell(r, 15).GetString()) ? null : sheet.Cell(r, 15).GetString().Trim(),
                    ExecutiveId = int.TryParse(sheet.Cell(r, 16).GetString(), out var execId) ? execId : (int?)null,
                    FollowUpDate = DateTime.TryParse(sheet.Cell(r, 17).GetString(), out var fDate) ? fDate : (DateTime?)null,
                    CreatedBy = UserId,
                    CreatedOn = IndianTime.Now,
                    ChannelPartnerId = currentUser?.ChannelPartnerId
                };

                // Set HandoverStatus based on user role
                if (Role?.ToLower() == "partner" || currentUser?.ChannelPartnerId != null)
                {
                    lead.HandoverStatus = "Partner";
                    lead.IsReadyToBook = false;
                    lead.PartnerAssignedAgentUserId = lead.ExecutiveId;
                }
                else
                {
                    lead.HandoverStatus = "Admin";
                }

                _db.Leads.Add(lead);
                await _db.SaveChangesAsync(); // Save to get LeadId
                processedCount++;

                // Track leads with assignments for notifications
                if (lead.ExecutiveId.HasValue && lead.ExecutiveId > 0)
                {
                    leadsToNotify.Add((lead.LeadId, lead.Name ?? "Unknown Lead", lead.ExecutiveId.Value));
                }
            }

            // Send notifications for assigned leads
            foreach (var (leadId, leadName, executiveId) in leadsToNotify)
            {
                var assignedUser = await _db.Users.FindAsync(executiveId);
                if (assignedUser != null)
                {
                    Console.WriteLine($"DEBUG: Bulk upload - Lead {leadId} assigned to ExecutiveId {executiveId} by {uploaderName} (Role: {Role})");
                    
                    await _notificationService.NotifyLeadAssignedAsync(
                        leadId,
                        leadName,
                        executiveId,
                        uploaderName
                    );
                    
                    Console.WriteLine($"DEBUG: Bulk upload notification sent to UserId {executiveId}");
                }
            }

            Console.WriteLine($"DEBUG: Bulk upload completed - Processed: {processedCount}");
            
            // Create success message
            var successMessage = $"Bulk upload completed: {processedCount} leads processed successfully";
            
            if (isAjax)
            {
                return Json(new { success = true, message = successMessage });
            }
            
            TempData["Success"] = successMessage;
            return RedirectToAction("Index");
        }
            catch (Exception ex)
            {
                Console.WriteLine($"DEBUG: Bulk upload error: {ex.Message}");
                
                if (isAjax)
                {
                    return Json(new { success = false, message = $"Bulk upload failed: {ex.Message}" });
                }
                TempData["Error"] = $"Bulk upload failed: {ex.Message}";
                return RedirectToAction("Index");
            }
        }
        [HttpGet]
        public IActionResult DownloadFile(int uploadId)
        {
            var file = _db.LeadUploads.FirstOrDefault(x => x.UploadId == uploadId);
            if (file == null)
                return NotFound();

            return File(file.FileBytes ?? Array.Empty<byte>(), file.ContentType ?? "application/octet-stream", file.FileName);
        }


        // Save log
        //[HttpPost]
        //public IActionResult SaveLog(int leadId, string text)
        //{
        //    var log = new LeadLogModel { LeadId = leadId, LogText = text, ExecutiveId = _getCurrentUserId() };
        //    _db.LeadLogs.Add(log);
        //    _db.SaveChanges();
        //    return Json(new { success = true });
        //}
        [HttpPost]
        public IActionResult SaveLog(int leadId, string text)
        {
            if (leadId == 0 || string.IsNullOrWhiteSpace(text))
                return Json(new { success = false, message = "Invalid data" });

            var execId = _getCurrentUserId();

            var log = new LeadLogModel
            {
                LeadId = leadId,
                LogText = text,
                ExecutiveId = execId,
                LogDate = IndianTime.Now   // ✅ REQUIRED (Fix null error)
            };

            _db.LeadLogs.Add(log);

            _db.LeadHistory.Add(new LeadHistoryModel
            {
                LeadId = leadId,
                Activity = "Log added",
                ExecutiveId = execId,
                ActivityDate = IndianTime.Now
            });

            _db.SaveChanges();

            return Json(new { success = true });
        }


        // Get history
        [HttpGet]
        public IActionResult GetHistory(int leadId)
        {
            var h = _db.LeadHistory.Where(x => x.LeadId == leadId).OrderByDescending(x => x.ActivityDate).ToList();
            
            var (UserId, Role) = GetUserFromToken();
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == UserId);
            var channelPartnerId = currentUser?.ChannelPartnerId;
            
            // Filter executives based on current user's role
            var executivesQuery = _db.Users.Where(u => u.Role == "Sales" || u.Role == "Agent");
            if (Role?.ToLower() == "sales" || Role?.ToLower() == "agent")
            {
                executivesQuery = executivesQuery.Where(u => u.UserId == UserId);
            }
            else if (Role?.ToLower() == "partner")
            {
                executivesQuery = executivesQuery.Where(u => u.ChannelPartnerId == channelPartnerId);
            }
            else if (Role?.ToLower() == "admin")
            {
                executivesQuery = executivesQuery.Where(u => u.ChannelPartnerId == null);
            }
            
            var execList = executivesQuery.ToList();
            if ((Role?.ToLower() == "admin" || Role?.ToLower() == "partner") && !execList.Any(u => u.UserId == UserId))
            {
                var self = _db.Users.FirstOrDefault(u => u.UserId == UserId);
                if (self != null) execList.Insert(0, self);
            }
            ViewBag.Executives = execList;
            return PartialView("_HistoryPartial", h);
        }

        [HttpGet]
        public IActionResult AddFollowUp(int leadId)
        {
            var model = new FollowUpModel();
            model.LeadId = leadId;

            var (UserId, Role) = GetUserFromToken();
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == UserId);
            var channelPartnerId = currentUser?.ChannelPartnerId;
            
            // Filter executives based on current user's role
            var executivesQuery = _db.Users.Where(u => u.Role == "Sales" || u.Role == "Agent");
            if (Role?.ToLower() == "sales" || Role?.ToLower() == "agent")
            {
                executivesQuery = executivesQuery.Where(u => u.UserId == UserId);
            }
            else if (Role?.ToLower() == "partner")
            {
                executivesQuery = executivesQuery.Where(u => u.ChannelPartnerId == channelPartnerId);
            }
            else if (Role?.ToLower() == "admin")
            {
                executivesQuery = executivesQuery.Where(u => u.ChannelPartnerId == null);
            }
            
            var execList = executivesQuery.ToList();
            if ((Role?.ToLower() == "admin" || Role?.ToLower() == "partner") && !execList.Any(u => u.UserId == UserId))
            {
                var self = _db.Users.FirstOrDefault(u => u.UserId == UserId);
                if (self != null) execList.Insert(0, self);
            }
            ViewBag.Executives = execList;

            return PartialView("_AddFollowUpPartial", model);
        }

        // Same for GetUploads, GetHistory, GetLogs
        public IActionResult GetLogs(int leadId)
        {
            ViewBag.LeadId = leadId;

            var logs = _db.LeadLogs
                .Where(x => x.LeadId == leadId)
                .OrderByDescending(x => x.LogDate)
                .ToList();

            var (UserId, Role) = GetUserFromToken();
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == UserId);
            var channelPartnerId = currentUser?.ChannelPartnerId;
            
            // Filter executives based on current user's role
            var executivesQuery = _db.Users.Where(u => u.Role == "Sales" || u.Role == "Agent");
            if (Role?.ToLower() == "sales" || Role?.ToLower() == "agent")
            {
                executivesQuery = executivesQuery.Where(u => u.UserId == UserId);
            }
            else if (Role?.ToLower() == "partner")
            {
                executivesQuery = executivesQuery.Where(u => u.ChannelPartnerId == channelPartnerId);
            }
            else if (Role?.ToLower() == "admin")
            {
                executivesQuery = executivesQuery.Where(u => u.ChannelPartnerId == null);
            }
            
            var execList = executivesQuery.ToList();
            if ((Role?.ToLower() == "admin" || Role?.ToLower() == "partner") && !execList.Any(u => u.UserId == UserId))
            {
                var self = _db.Users.FirstOrDefault(u => u.UserId == UserId);
                if (self != null) execList.Insert(0, self);
            }
            ViewBag.Executives = execList;

            return PartialView("_LogsPartial", logs);
        }

        [HttpPost]
        public IActionResult UpdateNote(int noteId, string noteText)
        {
            if (string.IsNullOrWhiteSpace(noteText))
                return Json(new { success = false, message = "Note cannot be empty" });

            var note = _db.LeadNotes.FirstOrDefault(n => n.NoteId == noteId);
            if (note == null) return Json(new { success = false, message = "Note not found" });

            note.NoteText = noteText;
            _db.SaveChanges();

            return Json(new { success = true });
        }

        // POST: Delete note
        [HttpPost]
        public IActionResult DeleteNote(int noteId)
        {
            var note = _db.LeadNotes.FirstOrDefault(n => n.NoteId == noteId);
            if (note == null) return Json(new { success = false, message = "Note not found" });

            _db.LeadNotes.Remove(note);
            _db.SaveChanges();

            return Json(new { success = true });
        }

        [HttpPost]
        public IActionResult SaveLeadUpload(int leadId, IFormFile file)
        {
            if (file == null || file.Length == 0)
                return Json(new { success = false, message = "No file selected" });
            var (UserId, Role) = GetUserFromToken();

            try
            {
                // Save file to wwwroot/uploads
                var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                var filePath = Path.Combine(uploadsFolder, file.FileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    file.CopyTo(stream);
                }

                // Save record to DB
                var upload = new LeadUploadModel
                {
                    LeadId = leadId,
                    FileName = file.FileName,
                    FilePath = "/uploads/" + file.FileName,
                    FileType = Path.GetExtension(file.FileName).ToLower(),
                    UploadedBy = UserId, // Set to current user id
                    UploadedOn = IndianTime.Now
                };

                _db.LeadUploads.Add(upload);
                _db.SaveChanges();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
        [HttpPost]
        [PermissionAuthorize("Delete")]
        public IActionResult Delete(int id)
        {
            try
            {
                var lead = _db.Leads.FirstOrDefault(x => x.LeadId == id);

                if (lead == null)
                    return Json(new { success = false, message = "Lead not found" });

                _db.Leads.Remove(lead);
                _db.SaveChanges();

                // Add audit log for deletion
                _db.AuditLogs.Add(new AuditLogModel
                {
                    UserId = _getCurrentUserId(),
                    Action = "Delete",
                    EntityType = "Lead",
                    EntityId = id,
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = Request.Headers["User-Agent"].ToString(),
                    Timestamp = IndianTime.Now
                });
                _db.SaveChanges();

                return Json(new { success = true, message = "Lead deleted successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        public IActionResult land()
        {
            return View();
        }

        // Get Site Visit Activities for a lead
        [HttpGet]
        public IActionResult GetSiteVisits(int leadId)
        {
            try
            {
                // Get all follow-ups with "Site Visited" stage for this lead
                var siteVisits = (from f in _db.LeadFollowUps.AsQueryable()
                                  join p in _db.Properties.AsQueryable() on f.PropertyId equals p.PropertyId into propertyGroup
                                  from property in propertyGroup.DefaultIfEmpty()
                                  where f.LeadId == leadId && 
                                        (f.Stage == "Site Visited" || f.Stage == "Site Visit Requested")
                                  orderby f.FollowUpDate descending
                                  select new
                                  {
                                      FollowUpId = f.FollowUpId,
                                      PropertyName = property != null ? property.PropertyName : "Unknown Property",
                                      VisitDate = f.FollowUpDate.HasValue ? f.FollowUpDate.Value.ToString("MMM dd, yyyy") : "N/A",
                                      InterestStatus = f.InterestStatus ?? ""
                                  }).ToList();

                // Convert to dynamic list
                var dynamicList = siteVisits.Select(s => (dynamic)new
                {
                    s.FollowUpId,
                    s.PropertyName,
                    s.VisitDate,
                    s.InterestStatus
                }).ToList();

                return PartialView("_SiteVisitActivitiesPartial", dynamicList);
            }
            catch (Exception ex)
            {
                return Content($"<div class='alert alert-danger'>Error loading site visits: {ex.Message}</div>");
            }
        }

        // Update Interest Status for a site visit
        [HttpPost]
        public IActionResult UpdateInterestStatus(int followUpId, string interestStatus)
        {
            try
            {
                var followUp = _db.LeadFollowUps.FirstOrDefault(f => f.FollowUpId == followUpId);
                if (followUp == null)
                {
                    return Json(new { success = false, message = "Follow-up not found" });
                }

                // Validate interest status
                if (interestStatus != "Interested" && interestStatus != "Not Interested" && interestStatus != "Cold")
                {
                    return Json(new { success = false, message = "Invalid interest status" });
                }

                // Update the interest status
                followUp.InterestStatus = interestStatus;
                _db.SaveChanges();

                // Add history entry
                var lead = _db.Leads.FirstOrDefault(l => l.LeadId == followUp.LeadId);
                if (lead != null)
                {
                    var property = _db.Properties.FirstOrDefault(p => p.PropertyId == followUp.PropertyId);
                    var propertyName = property != null ? property.PropertyName : "Unknown Property";

                    _db.LeadHistory.Add(new LeadHistoryModel
                    {
                        LeadId = followUp.LeadId,
                        Activity = $"Interest status updated to '{interestStatus}' for property: {propertyName}",
                        ExecutiveId = _getCurrentUserId(),
                        ActivityDate = IndianTime.Now
                    });
                    _db.SaveChanges();
                }

                return Json(new { success = true, message = "Interest status updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }
        // GET: /Leads/GetQuotations?leadId=123
        [HttpGet]
        public IActionResult GetQuotations(int leadId)
        {
            var quotations = _db.Quotations
                .Where(q => q.LeadId == leadId)
                .Select(q => new {
                    q.QuotationId,
                    q.Status,
                    q.TotalAmount,
                    q.CreatedOn
                })
                .OrderByDescending(q => q.CreatedOn)
                .ToList();
            return Json(quotations);
        }

        // GET: /Leads/GetBookingDocuments?leadId=123
        [HttpGet]
        public IActionResult GetBookingDocuments(int leadId)
        {
            var bookings = _db.Bookings
                .Where(b => b.LeadId == leadId)
                .Select(b => new {
                    b.BookingId,
                    b.Status,
                    b.BookingAmount,
                    b.CreatedOn
                })
                .OrderByDescending(b => b.CreatedOn)
                .ToList();
            return Json(bookings);
        }

        // GET: /Leads/GetInvoiceDocuments?leadId=123
        [HttpGet]
        public IActionResult GetInvoiceDocuments(int leadId)
        {
            var bookingIds = _db.Bookings
                .Where(b => b.LeadId == leadId)
                .Select(b => b.BookingId)
                .ToList();

            var invoices = _db.Invoices
                .Where(i => bookingIds.Contains(i.BookingId))
                .Select(i => new {
                    i.InvoiceId,
                    i.BookingId,
                    i.Status,
                    i.Amount,
                    i.CreatedOn
                })
                .OrderByDescending(i => i.CreatedOn)
                .ToList();
            return Json(invoices);
        }
        [HttpGet]
        public IActionResult GetPayments(int leadId)
        {
            var bookingIds = _db.Bookings
                .Where(b => b.LeadId == leadId)
                .Select(b => b.BookingId)
                .ToList();

            var payments = _db.Payments
                .Where(p => bookingIds.Contains(p.BookingId))
                .Select(p => new {
                    p.PaymentId,
                    p.InvoiceId,
                    p.BookingId,
                    p.PaymentDate,
                    p.ReceiptNumber,
                    p.Amount,
                    p.PaymentMethod,
                    p.TransactionReference
                })
                .OrderByDescending(p => p.PaymentDate)
                .ToList();

            return Json(payments);
        }

        // Partner Handover System Methods
        [HttpPost]
        public async Task<IActionResult> MarkReadyToBook(int leadId)
        {
            try
            {
                var (userId, role) = GetUserFromToken();
                var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
                var isPartnerTeam = role?.ToLower() == "partner" || currentUser?.ChannelPartnerId != null;
                
                if (!isPartnerTeam)
                {
                    return Json(new { success = false, message = "Only partners and partner team members can mark leads as ready to book" });
                }

                // P0-L2: Check subscription status for handover
                if (currentUser?.ChannelPartnerId != null)
                {
                    var subscription = await _db.PartnerSubscriptions
                        .Where(s => s.ChannelPartnerId == currentUser.ChannelPartnerId && 
                                   s.Status == "Active")
                        .OrderByDescending(s => s.StartDate)
                        .FirstOrDefaultAsync();
                    
                    if (subscription == null)
                    {
                        return Json(new { success = false, message = "No active subscription found. Please subscribe to handover leads." });
                    }
                    
                    // Check if subscription is expired
                    if (subscription.EndDate < IndianTime.Now)
                    {
                        return Json(new { success = false, message = "Your subscription has expired. Please renew to continue." });
                    }
                }

                var lead = _db.Leads.FirstOrDefault(l => l.LeadId == leadId);
                if (lead == null)
                {
                    return Json(new { success = false, message = "Lead not found" });
                }

                // Update lead status
                lead.HandoverStatus = "ReadyToBook";
                lead.IsReadyToBook = true;
                lead.HandoverDate = IndianTime.Now;
                lead.Source = "Partner"; // Mark source as Partner
                
                _db.Leads.Update(lead);

                // Create audit record
                _db.LeadHandoverAudit.Add(new LeadHandoverAuditModel
                {
                    LeadId = leadId,
                    FromStatus = "Partner",
                    ToStatus = "ReadyToBook",
                    HandedOverBy = userId ?? 0,
                    Notes = "Lead marked as ready to book by partner"
                });

                // Add history entry
                _db.LeadHistory.Add(new LeadHistoryModel
                {
                    LeadId = leadId,
                    Activity = "Lead marked as Ready to Book - Handed over to Admin",
                    ExecutiveId = userId,
                    ActivityDate = IndianTime.Now
                });

                await _db.SaveChangesAsync();

                // Send notification to admins about partner handover
                await _notificationService.NotifyPartnerHandoverAsync(
                    leadId,
                    lead.Name ?? "Unknown Lead"
                );

                return Json(new { success = true, message = "Lead marked as ready to book and handed over to Admin" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> AssignToAgent(int leadId, int agentId)
        {
            try
            {
                var (userId, role) = GetUserFromToken();
                if (role?.ToLower() != "admin")
                {
                    return Json(new { success = false, message = "Only admin can assign leads to agents" });
                }

                var lead = _db.Leads.FirstOrDefault(l => l.LeadId == leadId);
                if (lead == null)
                {
                    return Json(new { success = false, message = "Lead not found" });
                }

                var agent = _db.Users.FirstOrDefault(u => u.UserId == agentId && (u.Role == "Sales" || u.Role == "Agent") && u.ChannelPartnerId == null);
                if (agent == null)
                {
                    return Json(new { success = false, message = "Agent not found" });
                }

                // Update lead
                lead.HandoverStatus = "HandedOver";
                lead.AdminAssignedTo = agentId;
                lead.ExecutiveId = agentId;
                
                _db.Leads.Update(lead);

                // Create audit record
                _db.LeadHandoverAudit.Add(new LeadHandoverAuditModel
                {
                    LeadId = leadId,
                    FromStatus = "ReadyToBook",
                    ToStatus = "HandedOver",
                    HandedOverBy = userId ?? 0,
                    AssignedTo = agentId,
                    Notes = $"Lead assigned to {agent.Username} by Admin"
                });

                // Add history entry
                _db.LeadHistory.Add(new LeadHistoryModel
                {
                    LeadId = leadId,
                    Activity = $"Lead assigned to {agent.Username} by Admin",
                    ExecutiveId = userId,
                    ActivityDate = IndianTime.Now
                });

                await _db.SaveChangesAsync();

                // Notify assigned agent
                await _notificationService.NotifyLeadAssignedAsync(
                    leadId,
                    lead.Name ?? "Unknown Lead",
                    agentId,
                    "Admin"
                );

                // Notify partner and partner team members
                if (lead.ChannelPartnerId.HasValue)
                {
                    var partnerUsers = _db.Users.Where(u => u.ChannelPartnerId == lead.ChannelPartnerId).ToList();
                    foreach (var partnerUser in partnerUsers)
                    {
                        await _notificationService.CreateNotificationAsync(
                            "Lead Assigned to Agent",
                            $"Your lead '{lead.Name}' has been assigned to {agent.Username}",
                            "LeadAssignment",
                            partnerUser.UserId,
                            //$"/leaddetails/{leadId}",
                            $"/leaddetails/{IdObfuscator.Encode(leadId)}#scrollspyFollowups",
                            leadId,
                            "Lead",
                            "Medium"
                        );
                    }
                }

                return Json(new { success = true, message = $"Lead assigned to {agent.Username}" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpGet]
        public IActionResult GetHandoverStatus(int leadId)
        {
            var lead = _db.Leads.FirstOrDefault(l => l.LeadId == leadId);
            if (lead == null)
            {
                return Json(new { success = false, message = "Lead not found" });
            }

            var (userId, role) = GetUserFromToken();
            var canEdit = true;
            
            // Partners can't edit after handover
            if (role?.ToLower() == "partner" && lead.HandoverStatus != "Partner")
            {
                canEdit = false;
            }

            return Json(new {
                success = true,
                handoverStatus = lead.HandoverStatus,
                isReadyToBook = lead.IsReadyToBook,
                handoverDate = lead.HandoverDate?.ToString("MMM dd, yyyy hh:mm tt"),
                canEdit = canEdit,
                canMarkReady = role?.ToLower() == "partner" && lead.HandoverStatus == "Partner"
            });
        }

        // ============================================
        // LEAD IMPORT WIZARD - P1 FEATURE
        // ============================================
        [HttpGet]
        [PermissionAuthorize("Add")]
        public IActionResult ImportWizard()
        {
            return View();
        }

        [HttpPost]
        [PermissionAuthorize("Add")]
        public async Task<IActionResult> DownloadTemplate()
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Leads Template");

                // Headers
                worksheet.Cell(1, 1).Value = "Name*";
                worksheet.Cell(1, 2).Value = "Email";
                worksheet.Cell(1, 3).Value = "Phone*";
                worksheet.Cell(1, 4).Value = "Source";
                worksheet.Cell(1, 5).Value = "Stage";
                worksheet.Cell(1, 6).Value = "Status";
                worksheet.Cell(1, 7).Value = "Budget";
                worksheet.Cell(1, 8).Value = "PropertyType";
                worksheet.Cell(1, 9).Value = "Location";
                worksheet.Cell(1, 10).Value = "Notes";

                // Format headers
                var headerRange = worksheet.Range(1, 1, 1, 10);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Sample data
                worksheet.Cell(2, 1).Value = "John Doe";
                worksheet.Cell(2, 2).Value = "john@example.com";
                worksheet.Cell(2, 3).Value = "9876543210";
                worksheet.Cell(2, 4).Value = "Website";
                worksheet.Cell(2, 5).Value = "New";
                worksheet.Cell(2, 6).Value = "Active";
                worksheet.Cell(2, 7).Value = "5000000";
                worksheet.Cell(2, 8).Value = "Residential";
                worksheet.Cell(2, 9).Value = "Bangalore";
                worksheet.Cell(2, 10).Value = "Interested in 3BHK";

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "LeadsImportTemplate.xlsx");
                }
            }
        }

        [HttpPost]
        [PermissionAuthorize("Add")]
        public async Task<IActionResult> ValidateImportFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return Json(new { success = false, message = "Please select a file" });
            }

            var extension = Path.GetExtension(file.FileName).ToLower();
            if (extension != ".xlsx" && extension != ".xls" && extension != ".csv")
            {
                return Json(new { success = false, message = "Only Excel (.xlsx, .xls) or CSV files are allowed" });
            }

            try
            {
                var leads = new List<object>();
                var errors = new List<string>();
                int rowNumber = 2; // Start from row 2 (after header)

                using (var stream = new MemoryStream())
                {
                    await file.CopyToAsync(stream);
                    stream.Position = 0;

                    if (extension == ".csv")
                    {
                        // CSV parsing
                        using (var reader = new StreamReader(stream))
                        {
                            var headerLine = await reader.ReadLineAsync();
                            while (!reader.EndOfStream)
                            {
                                var line = await reader.ReadLineAsync();
                                if (string.IsNullOrWhiteSpace(line)) continue;

                                var values = line.Split(',');
                                if (values.Length < 3)
                                {
                                    errors.Add($"Row {rowNumber}: Insufficient columns");
                                    rowNumber++;
                                    continue;
                                }

                                var lead = ParseLeadRow(values, rowNumber, errors);
                                if (lead != null) leads.Add(lead);
                                rowNumber++;
                            }
                        }
                    }
                    else
                    {
                        // Excel parsing
                        using (var workbook = new XLWorkbook(stream))
                        {
                            var worksheet = workbook.Worksheet(1);
                            var rangeUsed = worksheet.RangeUsed();
                            if (rangeUsed != null)
                            {
                                var rows = rangeUsed.RowsUsed().Skip(1); // Skip header

                                foreach (var row in rows)
                                {
                                    var values = new string[10];
                                    for (int i = 0; i < 10; i++)
                                    {
                                        values[i] = row.Cell(i + 1).GetString();
                                    }

                                    var lead = ParseLeadRow(values, rowNumber, errors);
                                    if (lead != null) leads.Add(lead);
                                    rowNumber++;
                                }
                            }
                        }
                    }
                }

                // Check subscription limit
                if (leads.Count > 0)
                {
                    var (userId, role) = GetUserFromToken();
                    var currentUser = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId);
                    
                    if (role?.ToLower() == "partner" && currentUser?.ChannelPartnerId.HasValue == true)
                    {
                        var (canAdd, message) = await _subscriptionService.CanAddLeadAsync(currentUser.ChannelPartnerId.Value);
                        if (!canAdd)
                        {
                            return Json(new { success = false, message = $"Subscription limit: {message}" });
                        }
                    }
                }

                return Json(new
                {
                    success = true,
                    totalRows = leads.Count,
                    validRows = leads.Count,
                    errorCount = errors.Count,
                    errors = errors.Take(50).ToList(), // Limit to first 50 errors
                    preview = leads.Take(10).ToList() // Preview first 10 rows
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error reading file: " + ex.Message });
            }
        }

        [HttpPost]
        [PermissionAuthorize("Add")]
        public async Task<IActionResult> ExecuteImport(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return Json(new { success = false, message = "Please select a file" });
            }

            try
            {
                var (userId, role) = GetUserFromToken();
                var currentUser = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId);
                int? channelPartnerId = currentUser?.ChannelPartnerId;

                var importedCount = 0;
                var skippedCount = 0;
                var errorMessages = new List<string>();

                using (var stream = new MemoryStream())
                {
                    await file.CopyToAsync(stream);
                    stream.Position = 0;

                    var extension = Path.GetExtension(file.FileName).ToLower();
                    
                    if (extension == ".csv")
                    {
                        using (var reader = new StreamReader(stream))
                        {
                            await reader.ReadLineAsync(); // Skip header
                            int rowNumber = 2;

                            while (!reader.EndOfStream)
                            {
                                var line = await reader.ReadLineAsync();
                                if (string.IsNullOrWhiteSpace(line)) continue;

                                var values = line.Split(',');
                                var result = await ImportLeadRow(values, rowNumber, userId ?? 0, channelPartnerId);
                                
                                if (result.success) importedCount++;
                                else
                                {
                                    skippedCount++;
                                    errorMessages.Add(result.error);
                                }
                                rowNumber++;
                            }
                        }
                    }
                    else
                    {
                        using (var workbook = new XLWorkbook(stream))
                        {
                            var worksheet = workbook.Worksheet(1);
                            var rangeUsed = worksheet.RangeUsed();
                            if (rangeUsed != null)
                            {
                                var rows = rangeUsed.RowsUsed().Skip(1);
                                int rowNumber = 2;

                                foreach (var row in rows)
                                {
                                    var values = new string[10];
                                    for (int i = 0; i < 10; i++)
                                    {
                                        values[i] = row.Cell(i + 1).GetString();
                                    }

                                    var result = await ImportLeadRow(values, rowNumber, userId ?? 0, channelPartnerId);
                                    
                                    if (result.success) importedCount++;
                                    else
                                    {
                                        skippedCount++;
                                        errorMessages.Add(result.error);
                                    }
                                    rowNumber++;
                                }
                            }
                        }
                    }
                }

                return Json(new
                {
                    success = true,
                    imported = importedCount,
                    skipped = skippedCount,
                    errors = errorMessages.Take(50).ToList(),
                    message = $"Successfully imported {importedCount} leads. {skippedCount} rows skipped."
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Import failed: " + ex.Message });
            }
        }

        private object? ParseLeadRow(string[] values, int rowNumber, List<string> errors)
        {
            var name = values[0]?.Trim();
            var email = values.Length > 1 ? values[1]?.Trim() : "";
            var phone = values.Length > 2 ? values[2]?.Trim() : "";

            if (string.IsNullOrEmpty(name))
            {
                errors.Add($"Row {rowNumber}: Name is required");
                return null;
            }

            if (string.IsNullOrEmpty(phone))
            {
                errors.Add($"Row {rowNumber}: Phone is required");
                return null;
            }

            return new
            {
                rowNumber,
                name,
                email,
                contact = phone,
                source = values.Length > 3 ? values[3]?.Trim() : "Import",
                stage = values.Length > 4 ? values[4]?.Trim() : "New",
                status = values.Length > 5 ? values[5]?.Trim() : "Active",
                requirement = values.Length > 6 ? values[6]?.Trim() : "",
                propertyType = values.Length > 7 ? values[7]?.Trim() : "",
                preferredLocation = values.Length > 8 ? values[8]?.Trim() : "",
                comments = values.Length > 9 ? values[9]?.Trim() : ""
            };
        }

        private async Task<(bool success, string error)> ImportLeadRow(string[] values, int rowNumber, int userId, int? channelPartnerId)
        {
            try
            {
                var name = values[0]?.Trim();
                var email = values.Length > 1 ? values[1]?.Trim() : "";
                var contact = values.Length > 2 ? values[2]?.Trim() : "";

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(contact))
                {
                    return (false, $"Row {rowNumber}: Missing required fields");
                }

                // Check for duplicate
                var exists = await _db.Leads.AnyAsync(l => l.Contact == contact || (l.Email == email && !string.IsNullOrEmpty(email)));
                if (exists)
                {
                    return (false, $"Row {rowNumber}: Duplicate lead (contact or email already exists)");
                }

                var lead = new LeadModel
                {
                    Name = name,
                    Email = email,
                    Contact = contact,
                    Source = values.Length > 3 && !string.IsNullOrEmpty(values[3]) ? values[3].Trim() : "Import",
                    Stage = values.Length > 4 && !string.IsNullOrEmpty(values[4]) ? values[4].Trim() : "New",
                    Status = values.Length > 5 && !string.IsNullOrEmpty(values[5]) ? values[5].Trim() : "Active",
                    Requirement = values.Length > 6 ? values[6]?.Trim() : "",
                    PropertyType = values.Length > 7 ? values[7]?.Trim() : "",
                    PreferredLocation = values.Length > 8 ? values[8]?.Trim() : "",
                    Comments = values.Length > 9 ? values[9]?.Trim() : "",
                    CreatedOn = IndianTime.Now,
                    ExecutiveId = userId,
                    ChannelPartnerId = channelPartnerId,
                    HandoverStatus = channelPartnerId.HasValue ? "Partner" : "Admin",
                    PartnerAssignedAgentUserId = channelPartnerId.HasValue ? userId : (int?)null
                };

                _db.Leads.Add(lead);
                await _db.SaveChangesAsync();

                // Create initial follow-up
                var followUp = new FollowUpModel
                {
                    LeadId = lead.LeadId,
                    FollowUpDate = IndianTime.Now.AddDays(1),
                    Comments = "Initial follow-up for imported lead",
                    ExecutiveId = userId,
                    Status = "Pending",
                    Stage = "New",
                    CreatedOn = IndianTime.Now
                };
                _db.LeadFollowUps.Add(followUp);
                await _db.SaveChangesAsync();

                return (true, "");
            }
            catch (Exception ex)
            {
                return (false, $"Row {rowNumber}: {ex.Message}");
            }
        }

        // ==================== P1-L3: BULK LEAD OPERATIONS ====================

        [HttpPost]
        public async Task<IActionResult> BulkDelete([FromForm] string leadIds)
        {
            try
            {
                if (string.IsNullOrEmpty(leadIds))
                    return Json(new { success = false, message = "No leads selected" });

                var ids = leadIds.Split(',').Select(int.Parse).ToList();
                var userId = _getCurrentUserId();
                var role = _getCurrentUserRole();
                var currentUser = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId);
                var isPartnerTeamUser = role?.ToLower() == "partner" || currentUser?.ChannelPartnerId != null;

                // Get leads based on role permissions
                var leadsQuery = _db.Leads.AsQueryable();
                if (role == "Sales")
                    leadsQuery = leadsQuery.Where(l => l.ExecutiveId == userId);
                else if (role == "Partner")
                {
                    var partner = await _db.ChannelPartners.FirstOrDefaultAsync(p => p.UserId == userId);
                    if (partner != null)
                        leadsQuery = leadsQuery.Where(l => l.ChannelPartnerId == partner.PartnerId);
                }

                var leads = await leadsQuery.Where(l => ids.Contains(l.LeadId)).ToListAsync();
                
                if (!leads.Any())
                    return Json(new { success = false, message = "No leads found or insufficient permissions" });

                _db.Leads.RemoveRange(leads);
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = $"{leads.Count} lead(s) deleted successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> BulkAssign([FromForm] string leadIds, [FromForm] int executiveId)
        {
            try
            {
                if (string.IsNullOrEmpty(leadIds))
                    return Json(new { success = false, message = "No leads selected" });

                if (executiveId <= 0)
                    return Json(new { success = false, message = "Please select an executive" });

                var ids = leadIds.Split(',').Select(int.Parse).ToList();
                var userId = _getCurrentUserId();
                var role = _getCurrentUserRole();
                var currentUser = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId);
                var isPartnerTeamUser = role?.ToLower() == "partner" || currentUser?.ChannelPartnerId != null;

                // Verify executive exists
                var executive = await _db.Users.FindAsync(executiveId);
                if (executive == null)
                    return Json(new { success = false, message = "Executive not found" });

                // Get leads based on role permissions
                var leadsQuery = _db.Leads.AsQueryable();
                if (role == "Sales")
                    leadsQuery = leadsQuery.Where(l => l.ExecutiveId == userId);
                else if (role == "Partner")
                {
                    var partner = await _db.ChannelPartners.FirstOrDefaultAsync(p => p.UserId == userId);
                    if (partner != null)
                        leadsQuery = leadsQuery.Where(l => l.ChannelPartnerId == partner.PartnerId);
                }

                var leads = await leadsQuery.Where(l => ids.Contains(l.LeadId)).ToListAsync();
                
                if (!leads.Any())
                    return Json(new { success = false, message = "No leads found or insufficient permissions" });

                foreach (var lead in leads)
                {
                    var oldExecutiveId = lead.ExecutiveId;
                    lead.ExecutiveId = executiveId;

                    if (isPartnerTeamUser && lead.ChannelPartnerId == currentUser?.ChannelPartnerId)
                    {
                        lead.PartnerAssignedAgentUserId = executiveId;
                    }

                    // Log handover
                    var handoverAudit = new LeadHandoverAuditModel
                    {
                        LeadId = lead.LeadId,
                        HandedOverBy = oldExecutiveId ?? userId,
                        AssignedTo = executiveId,
                        HandoverDate = IndianTime.Now,
                        FromStatus = lead.HandoverStatus ?? "Admin",
                        ToStatus = lead.HandoverStatus ?? "Admin",
                        Notes = "Bulk assignment"
                    };
                    _db.LeadHandoverAudit.Add(handoverAudit);
                }

                await _db.SaveChangesAsync();

                return Json(new { success = true, message = $"{leads.Count} lead(s) assigned to {executive.Username}" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> BulkUpdateStatus([FromForm] string leadIds, [FromForm] string status, [FromForm] string? stage)
        {
            try
            {
                if (string.IsNullOrEmpty(leadIds))
                    return Json(new { success = false, message = "No leads selected" });

                if (string.IsNullOrEmpty(status))
                    return Json(new { success = false, message = "Please select a status" });

                var ids = leadIds.Split(',').Select(int.Parse).ToList();
                var userId = _getCurrentUserId();
                var role = _getCurrentUserRole();

                // Get leads based on role permissions
                var leadsQuery = _db.Leads.AsQueryable();
                if (role == "Sales")
                    leadsQuery = leadsQuery.Where(l => l.ExecutiveId == userId);
                else if (role == "Partner")
                {
                    var partner = await _db.ChannelPartners.FirstOrDefaultAsync(p => p.UserId == userId);
                    if (partner != null)
                        leadsQuery = leadsQuery.Where(l => l.ChannelPartnerId == partner.PartnerId);
                }

                var leads = await leadsQuery.Where(l => ids.Contains(l.LeadId)).ToListAsync();
                
                if (!leads.Any())
                    return Json(new { success = false, message = "No leads found or insufficient permissions" });

                foreach (var lead in leads)
                {
                    lead.Status = status;
                    if (!string.IsNullOrEmpty(stage))
                        lead.Stage = stage;
                    _db.Leads.Update(lead);

                    // Log status change
                    var history = new LeadHistoryModel
                    {
                        LeadId = lead.LeadId,
                        Activity = $"Bulk status update: {status}" + (string.IsNullOrEmpty(stage) ? "" : $", Stage: {stage}"),
                        ActivityDate = IndianTime.Now,
                        ExecutiveId = userId
                    };
                    _db.LeadHistory.Add(history);
                }

                await _db.SaveChangesAsync();

                return Json(new { success = true, message = $"{leads.Count} lead(s) updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> BulkExport([FromForm] string leadIds)
        {
            try
            {
                if (string.IsNullOrEmpty(leadIds))
                    return Json(new { success = false, message = "No leads selected" });

                var ids = leadIds.Split(',').Select(int.Parse).ToList();
                var userId = _getCurrentUserId();
                var role = _getCurrentUserRole();

                // Get leads based on role permissions
                var leadsQuery = _db.Leads.AsQueryable();

                if (role == "Sales")
                    leadsQuery = leadsQuery.Where(l => l.ExecutiveId == userId);
                else if (role == "Partner")
                {
                    var partner = await _db.ChannelPartners.FirstOrDefaultAsync(p => p.UserId == userId);
                    if (partner != null)
                        leadsQuery = leadsQuery.Where(l => l.ChannelPartnerId == partner.PartnerId);
                }

                var leads = await leadsQuery.Where(l => ids.Contains(l.LeadId)).ToListAsync();
                
                if (!leads.Any())
                    return Json(new { success = false, message = "No leads found or insufficient permissions" });

                // Create Excel file
                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("Leads Export");
                    
                    // Headers
                    worksheet.Cell(1, 1).Value = "Lead ID";
                    worksheet.Cell(1, 2).Value = "Name";
                    worksheet.Cell(1, 3).Value = "Email";
                    worksheet.Cell(1, 4).Value = "Contact";
                    worksheet.Cell(1, 5).Value = "Source";
                    worksheet.Cell(1, 6).Value = "Stage";
                    worksheet.Cell(1, 7).Value = "Status";
                    worksheet.Cell(1, 8).Value = "Requirement";
                    worksheet.Cell(1, 9).Value = "Property Type";
                    worksheet.Cell(1, 10).Value = "Location";
                    worksheet.Cell(1, 11).Value = "Created Date";

                    // Style headers
                    var headerRange = worksheet.Range(1, 1, 1, 11);
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

                    // Data
                    int row = 2;
                    foreach (var lead in leads)
                    {
                        worksheet.Cell(row, 1).Value = lead.LeadId;
                        worksheet.Cell(row, 2).Value = lead.Name;
                        worksheet.Cell(row, 3).Value = lead.Email;
                        worksheet.Cell(row, 4).Value = lead.Contact;
                        worksheet.Cell(row, 5).Value = lead.Source;
                        worksheet.Cell(row, 6).Value = lead.Stage;
                        worksheet.Cell(row, 7).Value = lead.Status;
                        worksheet.Cell(row, 8).Value = lead.Requirement;
                        worksheet.Cell(row, 9).Value = lead.PropertyType;
                        worksheet.Cell(row, 10).Value = lead.PreferredLocation;
                        worksheet.Cell(row, 11).Value = lead.CreatedOn.ToString("yyyy-MM-dd");
                        row++;
                    }

                    // Auto-fit columns
                    worksheet.Columns().AdjustToContents();

                    using (var stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);
                        stream.Position = 0;
                        
                        var fileName = $"Leads_Export_{IndianTime.Now:yyyyMMdd_HHmmss}.xlsx";
                        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }
    }
}

