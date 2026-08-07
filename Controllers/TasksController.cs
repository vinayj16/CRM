using CRM.Attributes;
using CRM.Helpers;
using CRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CRM.Controllers
{
    [Authorize]
    public class TasksController : Controller
    {
        private readonly AppDbContext _db;
        private readonly INotificationService _notificationService;
        private readonly ILogger<TasksController> _logger;

        public TasksController(AppDbContext db, INotificationService notificationService, ILogger<TasksController> logger)
        {
            _db = db;
            _notificationService = notificationService;
            _logger = logger;
        }
        [PermissionAuthorize("View")]
        [Route("tasks")]
        [Route("Tasks/Index")]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult GetTasksByDate(DateTime? weekStart = null)
        {
            var role = User?.FindFirst(ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid, out int userId);
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;

            var startDate = weekStart ?? IndianTime.Today.AddDays(-(int)IndianTime.Today.DayOfWeek);
            var endDate = startDate.AddDays(6);

            // Get all leads first, then filter by date
            var query = _db.Leads.AsQueryable();

            if (role?.ToLower() == "partner")
                query = query.Where(l => l.ChannelPartnerId == channelPartnerId);
            else if (role?.ToLower() == "admin")
                query = query.Where(l => l.ChannelPartnerId == null);
            else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
                query = query.Where(l => l.ExecutiveId == userId);
            else
            {
                if (channelPartnerId.HasValue)
                {
                    query = query.Where(l => l.ChannelPartnerId == channelPartnerId);
                }
                else
                { query = query.Where(l => l.ChannelPartnerId == null); }
            }

            // Get all leads with follow-up dates (not just current week)
            var allLeadsWithFollowUp = query.Where(l => l.FollowUpDate.HasValue).ToList();

            var allTasks = allLeadsWithFollowUp.Select(l => new
            {
                l.LeadId,
                encodeUrl = IdObfuscator.Url("Lead", l.LeadId),
                Name = string.IsNullOrEmpty(l.Name) ? $"Lead #{l.LeadId}" : l.Name,
                Contact = string.IsNullOrEmpty(l.Contact) ? "No Contact" : l.Contact,
                Stage = string.IsNullOrEmpty(l.Stage) ? "New" : l.Stage,
                Status = string.IsNullOrEmpty(l.Status) ? "Active" : l.Status,
                FollowUpDate = l.FollowUpDate,
                Comments = l.Comments ?? "",
                IsCompleted = l.Stage == "Booked" || l.Status == "Closed" || l.Status == "Completed"
            }).ToList();

            // If no leads have follow-up dates, distribute recent leads across the week
            if (allLeadsWithFollowUp.Count == 0)
            {
                var recentLeads = query.OrderByDescending(l => l.CreatedOn).Take(14).ToList();
                allTasks.Clear();

                for (int i = 0; i < recentLeads.Count; i++)
                {
                    var lead = recentLeads[i];
                    var assignDate = startDate.AddDays(i % 7);

                    allTasks.Add(new
                    {
                        LeadId = lead.LeadId,
                        encodeUrl = IdObfuscator.Url("Lead", lead.LeadId),
                        Name = string.IsNullOrEmpty(lead.Name) ? $"Lead #{lead.LeadId}" : lead.Name,
                        Contact = string.IsNullOrEmpty(lead.Contact) ? "No Contact" : lead.Contact,
                        Stage = string.IsNullOrEmpty(lead.Stage) ? "New" : lead.Stage,
                        Status = string.IsNullOrEmpty(lead.Status) ? "Active" : lead.Status,
                        FollowUpDate = (DateTime?)assignDate,
                        Comments = lead.Comments ?? "Auto-assigned for follow-up",
                        IsCompleted = lead.Stage == "Booked" || lead.Status == "Closed" || lead.Status == "Completed"
                    });
                }
            }

            var tasksByDate = new List<object>();
            for (int i = 0; i < 7; i++)
            {
                var date = startDate.AddDays(i);
                var dateTasks = allTasks.Where(t => t.FollowUpDate?.Date == date).ToList();

                tasksByDate.Add(new
                {
                    date = date.ToString("yyyy-MM-dd"),
                    displayDate = date.ToString("MMM dd"),
                    dayName = date.ToString("dddd"),
                    isToday = date.Date == IndianTime.Today,
                    isTomorrow = date.Date == IndianTime.Today.AddDays(1),
                    tasks = dateTasks
                });
            }

            // Debug info
            var debugInfo = new
            {
                totalLeads = query.Count(),
                leadsWithFollowUp = allLeadsWithFollowUp.Count,
                sampleLead = query.OrderByDescending(l => l.CreatedOn).FirstOrDefault(),
                sampleTask = allTasks.FirstOrDefault(),
                totalTasks = allTasks.Count,
                userRole = role,
                userId = userId,
                weekRange = $"{startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}"
            };

            return Json(new
            {
                weekStart = startDate.ToString("yyyy-MM-dd"),
                weekEnd = endDate.ToString("yyyy-MM-dd"),
                tasksByDate = tasksByDate,
                debug = debugInfo
            });
        }

        [HttpPost]
        public IActionResult UpdateTaskDate([FromBody] UpdateTaskDateRequest request)
        {
            try
            {
                var lead = _db.Leads.FirstOrDefault(l => l.LeadId == request.LeadId);
                if (lead == null)
                    return Json(new { success = false, message = "Lead not found" });

                if (DateTime.TryParse(request.NewDate, out DateTime newDate))
                {
                    var oldDate = lead.FollowUpDate;

                    // Update the lead's main follow-up date
                    lead.FollowUpDate = newDate;

                    // Auto-mark as incomplete when moved to new date
                    if (lead.Status == "Completed")
                    {
                        lead.Status = "Active";
                    }

                    lead.ModifiedOn = IndianTime.Now;

                    _db.Leads.Update(lead);

                    var changes = _db.SaveChanges();

                    // Check if moved to today for notification refresh
                    var movedToToday = newDate.Date == IndianTime.Today;

                    return Json(new
                    {
                        success = true,
                        message = $"Task moved to {newDate:MMM dd} and marked as incomplete",
                        movedToToday = movedToToday
                    });
                }
                else
                {
                    return Json(new { success = false, message = "Invalid date format" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult DebugLead(int leadId)
        {
            var lead = _db.Leads.FirstOrDefault(l => l.LeadId == leadId);
            if (lead == null)
                return Json(new { error = "Lead not found" });

            return Json(new
            {
                leadId = lead.LeadId,
                name = lead.Name,
                followUpDate = lead.FollowUpDate?.ToString("yyyy-MM-dd HH:mm:ss"),
                modifiedOn = lead.ModifiedOn?.ToString("yyyy-MM-dd HH:mm:ss"),
                stage = lead.Stage,
                status = lead.Status
            });
        }

        [HttpPost]
        public IActionResult MarkTaskComplete([FromBody] MarkCompleteRequest request)
        {
            try
            {
                var lead = _db.Leads.FirstOrDefault(l => l.LeadId == request.LeadId);
                if (lead == null)
                    return Json(new { success = false, message = "Lead not found" });

                if (request.IsCompleted)
                {
                    lead.Status = "Completed";
                }
                else
                {
                    lead.Status = lead.Status == "Completed" ? "Active" : lead.Status;
                }

                lead.ModifiedOn = IndianTime.Now;
                _db.Leads.Update(lead);
                _db.SaveChanges();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetTodayTaskNotifications()
        {
            var role = User?.FindFirst(ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid, out int userId);
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;

            var today = IndianTime.Today;

            // Get today's follow-ups that haven't been marked as notification read
            var todayFollowUps = _db.LeadFollowUps
                .Where(f => f.FollowUpDate.HasValue &&
                           f.FollowUpDate.Value.Date == today &&
                           f.ExecutiveId == userId &&
                           (f.IsNotificationRead == false || f.IsNotificationRead == null))
                .ToList()
                .Join(_db.Leads.ToList(), f => f.LeadId, l => l.LeadId, (f, l) => new { FollowUp = f, Lead = l });

            var todayTasks = todayFollowUps.Select(item => new
            {
                leadId = item.FollowUp.LeadId,
                encodedId = IdObfuscator.Encode(item.FollowUp.LeadId),
                followUpId = item.FollowUp.FollowUpId,
                name = string.IsNullOrEmpty(item.Lead.Name) ? $"Lead #{item.FollowUp.LeadId}" : item.Lead.Name,
                contact = item.Lead.Contact ?? "No Contact",
                stage = item.FollowUp.Stage ?? "New",
                status = item.FollowUp.Status ?? "Active",
                followUpTime = item.FollowUp.FollowUpTime ?? "Not specified"
            }).ToList();

            return Json(new
            {
                count = todayTasks.Count,
                tasks = todayTasks
            });
        }

        [HttpPost]
        public async Task<IActionResult> MarkTodayTasksAsRead()
        {
            try
            {
                var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (!int.TryParse(uid, out int userId))
                {
                    return Json(new { success = false, message = "Invalid user" });
                }

                var today = IndianTime.Today;

                // Find today's follow-ups for this user that haven't been marked as read
                var todayFollowUps = await _db.LeadFollowUps
                    .Where(f => f.ExecutiveId == userId &&
                               f.FollowUpDate.HasValue &&
                               f.FollowUpDate.Value.Date == today &&
                               (f.IsNotificationRead == false || f.IsNotificationRead == null))
                    .ToListAsync();

                // Mark them as notification read
                foreach (var followUp in todayFollowUps)
                {
                    followUp.IsNotificationRead = true;
                    _db.LeadFollowUps.Update(followUp);
                }

                await _db.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"Marked {todayFollowUps.Count} follow-up notifications as read"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
        [HttpPost]
        public async Task<IActionResult> ClearScheduledTimers()
        {
            try
            {
                // Clear all active timers
                ScheduledNotificationInitializerService.ClearAllActiveTimers();

                return Json(new
                {
                    success = true,
                    message = "All scheduled timers cleared successfully"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }

    public class UpdateTaskDateRequest
    {
        public int LeadId { get; set; }
        public string NewDate { get; set; }
    }

    public class MarkCompleteRequest
    {
        public int LeadId { get; set; }
        public bool IsCompleted { get; set; }
    }
}
