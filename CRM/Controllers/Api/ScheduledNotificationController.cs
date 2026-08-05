using CRM.Helpers;
using CRM.Models;
using CRM.Services;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Controllers.Api
{
    [Route("api/[controller]")]
    [ApiController]
    public class ScheduledNotificationController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly INotificationService _notificationService;
        private static Dictionary<int, System.Timers.Timer> _activeTimers = new Dictionary<int, System.Timers.Timer>();
        private static object _lockObject = new object();

        public ScheduledNotificationController(AppDbContext db, INotificationService notificationService)
        {
            _db = db;
            _notificationService = notificationService;
        }

        // Schedule a notification for a specific follow-up time
        [HttpPost("schedule-followup-notification")]
        public async Task<IActionResult> ScheduleFollowUpNotification([FromBody] FollowUpModel followUp)
        {
            try
            {
                if (followUp?.FollowUpDate == null || string.IsNullOrEmpty(followUp.FollowUpTime))
                {
                    return BadRequest(new { success = false, message = "Follow-up date and time are required" });
                }

                // Cancel existing timer for this follow-up if it exists
                CancelExistingNotification(followUp.FollowUpId);

                // Parse the date and time
                var followUpDateTime = ParseFollowUpDateTime(followUp.FollowUpDate.Value, followUp.FollowUpTime);

                if (followUpDateTime <= IndianTime.Now)
                {
                    return BadRequest(new { success = false, message = "Follow-up time must be in the future" });
                }

                // Calculate delay until follow-up time
                var delay = followUpDateTime - IndianTime.Now;

                // Create and configure timer
                var timer = new System.Timers.Timer(delay.TotalMilliseconds);
                timer.AutoReset = false; // Fire only once
                timer.Elapsed += (sender, e) => OnFollowUpTimeReached(followUp);

                lock (_lockObject)
                {
                    _activeTimers[followUp.FollowUpId] = timer;
                }

                timer.Start();

                return Ok(new
                {
                    success = true,
                    message = $"Notification scheduled for {followUpDateTime:yyyy-MM-dd HH:mm}",
                    scheduledTime = followUpDateTime
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        // Cancel a scheduled notification
        [HttpPost("cancel-notification/{followUpId}")]
        public IActionResult CancelNotification(int followUpId)
        {
            try
            {
                CancelExistingNotification(followUpId);
                return Ok(new { success = true, message = "Notification cancelled" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        // Get all active scheduled notifications
        [HttpGet("active-notifications")]
        public IActionResult GetActiveNotifications()
        {
            lock (_lockObject)
            {
                var activeNotifications = _activeTimers.Select(kvp => new
                {
                    FollowUpId = kvp.Key,
                    IsEnabled = kvp.Value.Enabled,
                    Interval = kvp.Value.Interval
                }).ToList();

                return Ok(new { success = true, notifications = activeNotifications });
            }
        }

        // Reschedule all today's follow-ups (called on application start)
        [HttpPost("reschedule-today-followups")]
        public async Task<IActionResult> RescheduleTodayFollowUps()
        {
            try
            {
                var today = IndianTime.Today;
                var now = IndianTime.Now;

                // Get all follow-ups for today that haven't happened yet
                var todayFollowUps = await _db.LeadFollowUps
                    
                            .Join(_db.Leads, (FollowUpModel f) => f.LeadId, (LeadModel l) => l.LeadId, (f, l) => new { f, l })
                    .Where(x => x.f.FollowUpDate.HasValue &&
                               x.f.FollowUpDate.Value.Date == today &&
                               !string.IsNullOrEmpty(x.f.FollowUpTime) &&
                               ParseFollowUpDateTime(x.f.FollowUpDate.Value, x.f.FollowUpTime) > now)
                    .ToListAsync();

                var rescheduledCount = 0;

                foreach (var followUp in todayFollowUps)
                {
                    var followUpModel = new FollowUpModel
                    {
                        FollowUpId = followUp.f.FollowUpId,
                        LeadId = followUp.f.LeadId,
                        Stage = followUp.f.Stage,
                        Status = followUp.f.Status,
                        FollowUpDate = followUp.f.FollowUpDate,
                        FollowUpTime = followUp.f.FollowUpTime,
                        Comments = followUp.f.Comments,
                        ExecutiveId = followUp.f.ExecutiveId
                    };

                    var result = await ScheduleFollowUpNotification(followUpModel);
                    if (result is OkObjectResult okResult &&
                        okResult.Value is Dictionary<string, object> dict &&
                        dict.ContainsKey("success") && (bool)dict["success"])
                    {
                        rescheduledCount++;
                    }
                }

                return Ok(new
                {
                    success = true,
                    message = $"Rescheduled {rescheduledCount} follow-up notifications for today",
                    count = rescheduledCount
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        // Timer callback when follow-up time is reached
        private async void OnFollowUpTimeReached(FollowUpModel followUp)
        {
            try
            {
                // Remove timer from active timers
                lock (_lockObject)
                {
                    if (_activeTimers.ContainsKey(followUp.FollowUpId))
                    {
                        _activeTimers.Remove(followUp.FollowUpId);
                    }
                }

                // Get lead information
                var lead = await _db.Leads.FirstOrDefaultAsync(l => l.LeadId == followUp.LeadId);

                // Send notification to assigned executive
                await _notificationService.CreateNotificationAsync(
                    "Follow-Up Reminder",
                    $"It's time for your follow-up with {lead?.Name ?? "Lead"}! Stage: {followUp.Stage}, Status: {followUp.Status}",
                    "FollowUpReminder",
                    followUp.ExecutiveId,
                    $"/Leads/Details/{followUp.LeadId}",
                    followUp.LeadId,
                    "Lead",
                    "Urgent"
                );

                // Send notification to all admins
                var admins = await _db.Users
                    .Where(u => u.Role == "Admin" && u.ChannelPartnerId == null && u.UserId != followUp.ExecutiveId)
                    .ToListAsync();
                foreach (var admin in admins)
                {
                    await _notificationService.CreateNotificationAsync(
                        "Follow-Up Reminder",
                        $"Follow-up time for {lead?.Name ?? "Lead"} with {followUp.Stage}! Status: {followUp.Status}",
                        "FollowUpReminder",
                        admin.UserId,
                        $"/Leads/Details/{followUp.LeadId}",
                        followUp.LeadId,
                        "Lead",
                        "High"
                    );
                }

                Console.WriteLine($"Follow-up notification sent for FollowUpId: {followUp.FollowUpId} at {IndianTime.Now}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending follow-up notification: {ex.Message}");
            }
        }

        // Cancel existing notification timer
        private void CancelExistingNotification(int followUpId)
        {
            lock (_lockObject)
            {
                if (_activeTimers.ContainsKey(followUpId))
                {
                    _activeTimers[followUpId].Stop();
                    _activeTimers[followUpId].Dispose();
                    _activeTimers.Remove(followUpId);
                }
            }
        }

        // Parse follow-up date and time into DateTime
        private DateTime ParseFollowUpDateTime(DateTime date, string time)
        {
            if (TimeSpan.TryParse(time, out TimeSpan timeSpan))
            {
                return date.Date.Add(timeSpan);
            }
            throw new ArgumentException($"Invalid time format: {time}");
        }
    }
}
