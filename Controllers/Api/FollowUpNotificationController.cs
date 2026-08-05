using CRM.Helpers;
using CRM.Models;
using CRM.Services;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Controllers.Api
{
    [Route("api/[controller]")]
    [ApiController]
    public class FollowUpNotificationController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly INotificationService _notificationService;

        public FollowUpNotificationController(AppDbContext db, INotificationService notificationService)
        {
            _db = db;
            _notificationService = notificationService;
        }

        // Send daily follow-up notifications (called by background service)
        [HttpPost("send-daily-notifications")]
        public async Task<IActionResult> SendDailyNotifications()
        {
            try
            {
                var today = IndianTime.Today;
                var notificationsSent = 0;

                // Get all follow-ups scheduled for today
                var todayFollowUps = await _db.LeadFollowUps
                    
                            .Join(_db.Leads, f => f.LeadId, l => l.LeadId, (FollowUpModel f, LeadModel l) => new { f, l })
                    .Where(x => x.f.FollowUpDate.HasValue &&
                               x.f.FollowUpDate.Value.Date == today)
                    .ToListAsync();

                // Group by executive to avoid multiple notifications per person
                var followUpsByExecutive = todayFollowUps
                    .GroupBy(x => x.f.ExecutiveId)
                    .ToList();

                foreach (var group in followUpsByExecutive)
                {
                    var executiveId = group.Key;
                    var followUps = group.ToList();

                    if (followUps.Count == 1)
                    {
                        // Single follow-up notification
                        var followUp = followUps.First();
                        await _notificationService.CreateNotificationAsync(
                            "Follow-Up Scheduled for Today",
                            $"You have a follow-up today at {followUp.f.FollowUpTime ?? "Not specified"} with {followUp.l?.Name ?? "Lead"}. Stage: {followUp.f.Stage}, Status: {followUp.f.Status}",
                            "FollowUp",
                            executiveId,
                            $"/Leads/Details/{followUp.f.LeadId}",
                            followUp.f.LeadId,
                            "Lead",
                            "High"
                        );
                    }
                    else
                    {
                        // Multiple follow-ups notification
                        var leadNames = followUps.Select(f => f.l?.Name ?? "Lead").ToList();
                        var leadNamesText = leadNames.Count <= 3
                            ? string
                            .Join(", ", leadNames)
                            : $"{string
                            .Join(", ", leadNames.Take(3))} and {leadNames.Count - 3} more";

                        await _notificationService.CreateNotificationAsync(
                            "Multiple Follow-Ups Today",
                            $"You have {followUps.Count} follow-ups scheduled today: {leadNamesText}",
                            "FollowUp",
                            executiveId,
                            "/Leads/Index?followUpToday=true",
                            null,
                            "Lead",
                            "High"
                        );
                    }

                    notificationsSent++;
                }

                // Send overdue follow-up notifications
                var overdueCount = await SendOverdueNotifications();

                return Ok(new
                {
                    success = true,
                    message = $"Sent {notificationsSent} daily notifications and {overdueCount} overdue notifications",
                    dailyNotifications = notificationsSent,
                    overdueNotifications = overdueCount
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        // Send overdue follow-up notifications
        private async Task<int> SendOverdueNotifications()
        {
            var today = IndianTime.Today;
            var notificationsSent = 0;

            // Get overdue follow-ups (follow-up date before today and not completed)
            var overdueFollowUps = await _db.LeadFollowUps
                
                            .Join(_db.Leads, f => f.LeadId, l => l.LeadId, (FollowUpModel f, LeadModel l) => new { f, l })
                .Where(x => x.f.FollowUpDate.HasValue &&
                           x.f.FollowUpDate.Value.Date < today &&
                           (x.f.Status == null || x.f.Status != "Completed"))
                .ToListAsync();

            // Group by executive
            var overdueByExecutive = overdueFollowUps
                .GroupBy(x => x.f.ExecutiveId)
                .ToList();

            foreach (var group in overdueByExecutive)
            {
                var executiveId = group.Key;
                var overdueFollowUpsList = group.ToList();

                if (overdueFollowUpsList.Count == 1)
                {
                    var followUp = overdueFollowUpsList.First();
                    var daysOverdue = (today - followUp.f.FollowUpDate.Value.Date).Days;

                    await _notificationService.CreateNotificationAsync(
                        "Overdue Follow-Up",
                        $"Follow-up with {followUp.l?.Name ?? "Lead"} is {daysOverdue} days overdue! Stage: {followUp.f.Stage}",
                        "Overdue",
                        executiveId,
                        $"/Leads/Details/{followUp.f.LeadId}",
                        followUp.f.LeadId,
                        "Lead",
                        "Urgent"
                    );
                }
                else
                {
                    var leadNames = overdueFollowUpsList.Select(f => f.l?.Name ?? "Lead").ToList();
                    var leadNamesText = leadNames.Count <= 3
                        ? string
                            .Join(", ", leadNames)
                        : $"{string
                            .Join(", ", leadNames.Take(3))} and {leadNames.Count - 3} more";

                    await _notificationService.CreateNotificationAsync(
                        "Multiple Overdue Follow-Ups",
                        $"You have {overdueFollowUpsList.Count} overdue follow-ups that need attention: {leadNamesText}",
                        "Overdue",
                        executiveId,
                        "/Leads/Index?overdue=true",
                        null,
                        "Lead",
                        "Urgent"
                    );
                }

                notificationsSent++;
            }

            return notificationsSent;
        }

        // Get today's follow-ups for an executive
        [HttpGet("today-followups/{executiveId}")]
        public async Task<IActionResult> GetTodayFollowUps(int executiveId)
        {
            try
            {
                var today = IndianTime.Today;
                var todayFollowUps = await _db.LeadFollowUps
                    
                            .Join(_db.Leads, f => f.LeadId, l => l.LeadId, (FollowUpModel f, LeadModel l) => new { f, l })
                    .Where(x => x.f.ExecutiveId == executiveId &&
                               x.f.FollowUpDate.HasValue &&
                               x.f.FollowUpDate.Value.Date == today)
                    .Select(x => new
                    {
                        x.f.FollowUpId,
                        x.f.LeadId,
                        LeadName = x.l.Name,
                        x.f.Stage,
                        x.f.Status,
                        x.f.FollowUpDate,
                        x.f.FollowUpTime,
                        x.f.Comments
                    })
                    .OrderBy(x => x.FollowUpDate)
                    .ToListAsync();

                return Ok(new { success = true, followUps = todayFollowUps });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        // Get overdue follow-ups for an executive
        [HttpGet("overdue-followups/{executiveId}")]
        public async Task<IActionResult> GetOverdueFollowUps(int executiveId)
        {
            try
            {
                var today = IndianTime.Today;
                var overdueFollowUps = await _db.LeadFollowUps
                    
                            .Join(_db.Leads, f => f.LeadId, l => l.LeadId, (FollowUpModel f, LeadModel l) => new { f, l })
                    .Where(x => x.f.ExecutiveId == executiveId &&
                               x.f.FollowUpDate.HasValue &&
                               x.f.FollowUpDate.Value.Date < today &&
                               (x.f.Status == null || x.f.Status != "Completed"))
                    .Select(x => new
                    {
                        x.f.FollowUpId,
                        x.f.LeadId,
                        LeadName = x.l.Name,
                        x.f.Stage,
                        x.f.Status,
                        x.f.FollowUpDate,
                        x.f.FollowUpTime,
                        x.f.Comments,
                        DaysOverdue = (today - x.f.FollowUpDate.Value.Date).Days
                    })
                    .OrderBy(x => x.FollowUpDate)
                    .ToListAsync();

                return Ok(new { success = true, followUps = overdueFollowUps });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }
    }
}
