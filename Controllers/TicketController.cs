using CRM.Helpers;
using CRM.Models;
using CRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CRM.Controllers
{
    [Authorize]
    public class TicketController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ILogger<TicketController> _logger;
        private readonly INotificationService _notificationService;

        public TicketController(AppDbContext db, ILogger<TicketController> logger, INotificationService notificationService)
        {
            _db = db;
            _logger = logger;
            _notificationService = notificationService;
        }

        // GET: /Ticket/Index
        public IActionResult Index()
        {
            return View();
        }

        // GET: /Ticket/GetTickets
        [HttpGet]
        public async Task<IActionResult> GetTickets(string? status = null, string? priority = null, string? category = null)
        {
            try
            {
                var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(uid ?? "0", out int userId);
                var role = User?.FindFirst(ClaimTypes.Role)?.Value;
                var currentUser = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId);
                var channelPartnerId = currentUser?.ChannelPartnerId;

                var query = _db.Tickets.AsQueryable();

                // Role-based filtering
                if (role?.ToLower() == "admin")
                {
                    // Admin sees all tickets
                }
                else if (role?.ToLower() == "partner")
                {
                    query = query.Where(t => t.ChannelPartnerId == channelPartnerId || t.CreatedBy == userId);
                }
                else
                {
                    // Sales/Agent see only their own tickets
                    query = query.Where(t => t.CreatedBy == userId);
                }

                // Filters
                if (!string.IsNullOrEmpty(status) && status != "all")
                    query = query.Where(t => t.Status == status);
                if (!string.IsNullOrEmpty(priority) && priority != "all")
                    query = query.Where(t => t.Priority == priority);
                if (!string.IsNullOrEmpty(category) && category != "all")
                    query = query.Where(t => t.Category == category);

                // Load tickets first, then project in memory (MongoDB compatibility)
                var allTickets = await query
                    .OrderByDescending(t => t.CreatedOn)
                    .ToListAsync();

                var tickets = allTickets.Select(t => new
                {
                    t.TicketId,
                    t.Subject,
                    t.Category,
                    t.Priority,
                    t.Status,
                    t.CreatedByName,
                    t.AssignedToName,
                    t.CreatedOn,
                    t.ResolvedOn,
                    ReplyCount = t.Replies?.Count ?? 0
                }).ToList();

                return Json(new { success = true, data = tickets });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching tickets");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: /Ticket/Details/{id}
        public async Task<IActionResult> Details(int id)
        {
            var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.TicketId == id);
            if (ticket == null)
                return NotFound();

            return View(ticket);
        }

        // GET: /Ticket/GetTicket/{id}
        [HttpGet]
        public async Task<IActionResult> GetTicket(int id)
        {
            try
            {
                var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.TicketId == id);
                if (ticket == null)
                    return Json(new { success = false, message = "Ticket not found" });

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        ticket.TicketId,
                        ticket.Subject,
                        ticket.Description,
                        ticket.Category,
                        ticket.Priority,
                        ticket.Status,
                        ticket.CreatedByName,
                        ticket.CreatedByEmail,
                        ticket.AssignedToName,
                        ticket.CreatedOn,
                        ticket.ModifiedOn,
                        ticket.ResolvedOn,
                        ticket.ClosedOn,
                        ticket.Resolution,
                        ticket.AdminNotes,
                        ticket.RelatedEntityId,
                        ticket.RelatedEntityType,
                        Replies = (ticket.Replies ?? new List<TicketReplyModel>()).OrderBy(r => r.CreatedOn).Select(r => new
                        {
                            r.ReplyId,
                            r.Message,
                            r.UserName,
                            r.IsStaff,
                            r.CreatedOn
                        })
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching ticket {Id}", id);
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: /Ticket/Create
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SupportTicketModel model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.Subject) || string.IsNullOrWhiteSpace(model.Description))
                    return Json(new { success = false, message = "Subject and description are required" });

                var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(uid ?? "0", out int userId);
                var role = User?.FindFirst(ClaimTypes.Role)?.Value;
                var currentUser = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId);
                var channelPartnerId = currentUser?.ChannelPartnerId;

                // Generate TicketId (max + 1)
                var maxTicketId = 0;
                try {
                    var allTickets = await _db.Tickets.ToListAsync();
                    if (allTickets.Any())
                        maxTicketId = allTickets.Max(t => t.TicketId);
                } catch { }

                var ticket = new SupportTicketModel
                {
                    TicketId = maxTicketId + 1,
                    Subject = model.Subject,
                    Description = model.Description,
                    Category = string.IsNullOrEmpty(model.Category) ? "General" : model.Category,
                    Priority = string.IsNullOrEmpty(model.Priority) ? "Normal" : model.Priority,
                    Status = "Open",
                    CreatedBy = userId,
                    CreatedByName = currentUser?.Username ?? "Unknown",
                    CreatedByEmail = currentUser?.Email ?? "",
                    AssignedTo = null,
                    ChannelPartnerId = channelPartnerId,
                    CreatedOn = IndianTime.Now
                };

                _db.Tickets.Add(ticket);
                await _db.SaveChangesAsync();

                _logger.LogInformation($"Ticket #{ticket.TicketId} created by user {userId}: {ticket.Subject}");

                // Audit log
                try { _db.AuditLogs.Add(new AuditLogModel { UserId = userId, Action = "Create", EntityType = "Ticket", EntityId = ticket.TicketId, Timestamp = IndianTime.Now }); await _db.SaveChangesAsync(); } catch { }

                // Send notification to admins
                try {
                    var adminUsers = await _db.Users.Where(u => u.Role == "Admin").ToListAsync();
                    foreach (var admin in adminUsers)
                    {
                        await _notificationService.CreateNotificationAsync(
                            title: "New Support Ticket",
                            message: $"Ticket #{ticket.TicketId}: {ticket.Subject} created by {currentUser?.Username ?? "Unknown"}",
                            type: "Ticket",
                            userId: admin.UserId,
                            link: $"/Ticket",
                            relatedEntityId: ticket.TicketId,
                            relatedEntityType: "Ticket",
                            priority: ticket.Priority == "Urgent" || ticket.Priority == "High" ? "High" : "Normal"
                        );
                    }
                } catch (Exception notifEx) {
                    _logger.LogWarning(notifEx, "Failed to send ticket notification");
                }

                return Json(new { success = true, message = "Ticket created successfully", ticketId = ticket.TicketId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating ticket");
                return Json(new { success = false, message = "Error creating ticket: " + ex.Message });
            }
        }

        // POST: /Ticket/UpdateStatus
        [HttpPost]
        public async Task<IActionResult> UpdateStatus(int ticketId, string status, string? resolution = null)
        {
            try
            {
                var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.TicketId == ticketId);
                if (ticket == null)
                    return Json(new { success = false, message = "Ticket not found" });

                var role = User?.FindFirst(ClaimTypes.Role)?.Value;
                if (role?.ToLower() != "admin")
                    return Json(new { success = false, message = "Only admins can update ticket status" });

                ticket.Status = status;
                ticket.ModifiedOn = IndianTime.Now;

                if (status == "Resolved" && !string.IsNullOrEmpty(resolution))
                {
                    ticket.Resolution = resolution;
                    ticket.ResolvedOn = IndianTime.Now;
                }
                else if (status == "Closed")
                {
                    ticket.ClosedOn = IndianTime.Now;
                }

                await _db.SaveChangesAsync();

                _logger.LogInformation($"Ticket #{ticketId} status updated to {status}");

                // Audit log
                var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(uid ?? "0", out int logUserId);
                try { _db.AuditLogs.Add(new AuditLogModel { UserId = logUserId, Action = "Update", EntityType = "Ticket", EntityId = ticketId, Timestamp = IndianTime.Now }); await _db.SaveChangesAsync(); } catch { }

                return Json(new { success = true, message = "Status updated" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating ticket status");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: /Ticket/Assign
        [HttpPost]
        public async Task<IActionResult> Assign(int ticketId, int assignedTo)
        {
            try
            {
                var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.TicketId == ticketId);
                if (ticket == null)
                    return Json(new { success = false, message = "Ticket not found" });

                var role = User?.FindFirst(ClaimTypes.Role)?.Value;
                if (role?.ToLower() != "admin")
                    return Json(new { success = false, message = "Only admins can assign tickets" });

                var assignedUser = await _db.Users.FirstOrDefaultAsync(u => u.UserId == assignedTo);
                if (assignedUser == null)
                    return Json(new { success = false, message = "User not found" });

                ticket.AssignedTo = assignedTo;
                ticket.AssignedToName = assignedUser.Username;
                ticket.Status = "InProgress";
                ticket.ModifiedOn = IndianTime.Now;

                _db.Tickets.Update(ticket);
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = $"Ticket assigned to {assignedUser.Username}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning ticket");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: /Ticket/AddReply
        [HttpPost]
        public async Task<IActionResult> AddReply(int ticketId, string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message))
                    return Json(new { success = false, message = "Message is required" });

                var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.TicketId == ticketId);
                if (ticket == null)
                    return Json(new { success = false, message = "Ticket not found" });

                var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(uid ?? "0", out int userId);
                var role = User?.FindFirst(ClaimTypes.Role)?.Value;
                var currentUser = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId);

                var replies = ticket.Replies ?? new List<TicketReplyModel>();
                var nextReplyId = replies.Any() ? replies.Max(r => r.ReplyId) + 1 : 1;

                var reply = new TicketReplyModel
                {
                    ReplyId = nextReplyId,
                    Message = message,
                    UserId = userId,
                    UserName = currentUser?.Username ?? "Unknown",
                    IsStaff = role?.ToLower() == "admin",
                    CreatedOn = IndianTime.Now
                };

                ticket.Replies.Add(reply);
                ticket.ModifiedOn = IndianTime.Now;

                // Re-open if closed
                if (ticket.Status == "Closed" || ticket.Status == "Resolved")
                    ticket.Status = "InProgress";

                _db.Tickets.Update(ticket);
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Reply added" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding reply");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: /Ticket/GetUnassignedCount
        [HttpGet]
        public async Task<IActionResult> GetUnassignedCount()
        {
            try
            {
                var role = User?.FindFirst(ClaimTypes.Role)?.Value;
                if (role?.ToLower() != "admin")
                    return Json(new { count = 0 });

                var count = await _db.Tickets
                    .Where(t => t.Status == "Open" && t.AssignedTo == null)
                    .CountAsync();

                return Json(new { count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error counting unassigned tickets");
                return Json(new { count = 0 });
            }
        }

        // GET: /Ticket/GetAssignedUsers
        [HttpGet]
        public async Task<IActionResult> GetAssignedUsers()
        {
            try
            {
                var users = await _db.Users
                    .Where(u => u.Role == "Admin" || u.Role == "Agent" || u.Role == "Sales")
                    .Select(u => new { u.UserId, u.Username, u.Email, u.Role })
                    .ToListAsync();

                return Json(new { success = true, data = users });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching users");
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}
