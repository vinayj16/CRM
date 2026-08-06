using CRM.Helpers;
using CRM.Attributes;
using CRM.Models;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CRM.Controllers
{
    // [RoleAuthorize("Admin")] // Allow all authenticated users
    public class AttendanceController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<AttendanceController> _logger;
        public AttendanceController(AppDbContext context, ILogger<AttendanceController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return RedirectToAction("AgentList");
        }

        [HttpGet]
        public IActionResult AgentList()
        {
            var role = User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid, out int userId);
            var currentUser = _context.Users.FirstOrDefault(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;

            var agentsQuery = _context.Users.Where(u => u.Role == "Sales" || u.Role == "Agent");
            if (role?.ToLower() == "partner")
                agentsQuery = agentsQuery.Where(u => u.ChannelPartnerId == channelPartnerId);
            else if (role?.ToLower() == "admin")
                agentsQuery = agentsQuery.Where(u => u.ChannelPartnerId == null);

            var agents = agentsQuery.OrderBy(u => u.Username).ToList();

            // Calculate attendance stats for each agent for current month
            var currentMonth = IndianTime.Now;
            var firstDay = new DateTime(currentMonth.Year, currentMonth.Month, 1);
            var lastDay = firstDay.AddMonths(1).AddDays(-1);
            var daysInMonth = DateTime.DaysInMonth(currentMonth.Year, currentMonth.Month);
            var workingDaysCount = Enumerable.Range(1, daysInMonth)
                .Select(day => new DateTime(currentMonth.Year, currentMonth.Month, day))
                .Count(date => date.DayOfWeek != DayOfWeek.Sunday && date <= IndianTime.Today);

            var agentStats = new List<dynamic>();
            
            foreach (var agent in agents)
            {
                var attendanceRecords = _context.AgentAttendance
                    .Where(a => a.AgentId == agent.UserId && 
                               a.Date >= firstDay && 
                               a.Date <= lastDay &&
                               a.Date <= IndianTime.Today)
                    .ToList();

                var presentDays = attendanceRecords.Count(a => a.Status == "Present");
                var absentDays = workingDaysCount - presentDays;
                var attendancePercentage = workingDaysCount > 0 ? (double)presentDays / workingDaysCount * 100 : 0;

                // Get user profile for image
                var userProfile = _context.UserProfiles.FirstOrDefault(p => p.Username == agent.Username);
                string profileImageSrc = null;
                if (userProfile?.ProfileImage != null)
                {
                    profileImageSrc = $"data:image/png;base64,{Convert.ToBase64String(userProfile.ProfileImage)}";
                }

                agentStats.Add(new
                {
                    Agent = agent,
                    PresentDays = presentDays,
                    AbsentDays = absentDays,
                    WorkingDays = workingDaysCount,
                    AttendancePercentage = attendancePercentage,
                    ProfileImage = profileImageSrc
                });
            }

            ViewBag.AgentStats = agentStats;
            ViewBag.CurrentMonth = currentMonth;
            ViewBag.WorkingDays = workingDaysCount;
            
            return View();
        }


        public IActionResult Calendar(string? agentId = null, int? year = null, int? month = null)
        {
            var decodedId = IdObfuscator.Decode(agentId);

            ViewBag.EncodedId = agentId;

            var role = User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid, out int userId);

            var currentUser = _context.Users.FirstOrDefault(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;

            var now = IndianTime.Now;
            int y = year ?? now.Year;
            int m = month ?? now.Month;

            var allUsersQuery = _context.Users.Where(u => u.Role == "Sales" || u.Role == "Agent");

            if (role?.ToLower() == "partner")
                allUsersQuery = allUsersQuery.Where(u => u.ChannelPartnerId == channelPartnerId);
            else if (role?.ToLower() == "admin")
                allUsersQuery = allUsersQuery.Where(u => u.ChannelPartnerId == null);

            var allUsers = allUsersQuery.ToList();

            // ✅ FIX: fallback BEFORE using decodedId
            //if (!decodedId.HasValue || decodedId.Value == 0)
            //{
            //    var userIdCookie = Request.Cookies["UserId"];
            //    if (!string.IsNullOrEmpty(userIdCookie) && int.TryParse(userIdCookie, out int cookieUserId))
            //    {
            //        agentId = cookieUserId.ToString();
            //    }
            //    else
            //    {
            //        var userIdClaim = User.Claims.FirstOrDefault(c =>
            //            c.Type == "sub" || c.Type == "userid" || c.Type == "UserId");

            //        if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int claimId))
            //        {
            //            agentId = claimId.ToString();
            //        }
            //        else if (User.Identity != null &&
            //                 !string.IsNullOrEmpty(User.Identity.Name) &&
            //                 int.TryParse(User.Identity.Name, out int nameId))
            //        {
            //            agentId = nameId.ToString();
            //        }
            //    }

            //    // Admin fallback
            //    if (User.IsInRole("Admin") && !string.IsNullOrEmpty(agentId) == false)
            //    {
            //        var firstUser = allUsers.FirstOrDefault();
            //        if (firstUser != null)
            //            agentId = firstUser.UserId.ToString();
            //    }

            //    // ✅ IMPORTANT FIX: re-decode after updating agentId
            //    decodedId = IdObfuscator.Decode(agentId);
            //}
            if (!decodedId.HasValue || decodedId.Value == 0)
            {
                var userIdCookie = Request.Cookies["UserId"];

                if (!string.IsNullOrEmpty(userIdCookie) &&
                    int.TryParse(userIdCookie, out int cookieUserId))
                {
                    decodedId = cookieUserId;
                }
                else
                {
                    var userIdClaim = User.Claims.FirstOrDefault(c =>
                        c.Type == "sub" ||
                        c.Type == "userid" ||
                        c.Type == "UserId");

                    if (userIdClaim != null &&
                        int.TryParse(userIdClaim.Value, out int claimId))
                    {
                        decodedId = claimId;
                    }
                    else if (User.Identity != null &&
                             !string.IsNullOrEmpty(User.Identity.Name) &&
                             int.TryParse(User.Identity.Name, out int nameId))
                    {
                        decodedId = nameId;
                    }
                }

                if (User.IsInRole("Admin") && !decodedId.HasValue)
                {
                    var firstUser = allUsers.FirstOrDefault();

                    if (firstUser != null)
                        decodedId = firstUser.UserId;
                }
            }

            // ✅ FINAL guard
            if (!decodedId.HasValue)
                return NotFound();

            int actualUserId = decodedId.Value;

            // Latest month logic
            if (!year.HasValue && !month.HasValue)
            {
                var latestRecord = _context.AgentAttendance
                    .Where(a => a.AgentId == actualUserId)
                    .OrderByDescending(a => a.Date)
                    .FirstOrDefault();

                if (latestRecord != null)
                {
                    y = latestRecord.Date.Year;
                    m = latestRecord.Date.Month;
                }
            }

            int daysInMonth = DateTime.DaysInMonth(y, m);

            var attendance = _context.AgentAttendance
                .Where(a => a.AgentId == actualUserId && a.Date.Year == y && a.Date.Month == m)
                .ToList();

            // ✅ FIX: load logs once (avoid N+1)
            var logs = _context.AttendanceLog
                .Where(l => l.AgentId == actualUserId && l.Timestamp.Year == y && l.Timestamp.Month == m)
                .OrderBy(l => l.Timestamp)
                .ToList();

            foreach (var att in attendance)
            {
                var dayLogs = logs
                    .Where(l => l.Timestamp.Date == att.Date.Date)
                    .ToList();

                var intervals = new List<(DateTime login, DateTime? logout)>();
                DateTime? currentLogin = null;

                foreach (var log in dayLogs)
                {
                    if (log.Type == "Login")
                        currentLogin = log.Timestamp;

                    else if (log.Type == "Logout" && currentLogin != null)
                    {
                        intervals.Add((currentLogin.Value, log.Timestamp));
                        currentLogin = null;
                    }
                }

                double totalHours = intervals.Sum(i => (i.logout.Value - i.login).TotalHours);

                if (intervals.Any())
                {
                    att.LoginTime = intervals.First().login;
                    att.LogoutTime = intervals.Last().logout;

                    var dbRecord = _context.AgentAttendance.Find(att.AttendanceId);
                    if (dbRecord != null)
                    {
                        dbRecord.LoginTime = att.LoginTime;
                        dbRecord.LogoutTime = att.LogoutTime;
                        dbRecord.Status = "Present";
                        _context.AgentAttendance.Update(dbRecord);
                    }
                }

                if (totalHours > 0)
                    att.Status = "Present";
            }

            // ✅ FIX: save once instead of inside loop
            _context.SaveChanges();

            var attendanceDict = attendance
                .GroupBy(a => a.Date.Date)
                .ToDictionary(g => g.Key, g => g.First());

            var completeAttendance = new List<AgentAttendanceModel>();

            for (int day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(y, m, day);

                if (attendanceDict.ContainsKey(date.Date))
                {
                    completeAttendance.Add(attendanceDict[date.Date]);
                }
                else if (date <= IndianTime.Today)
                {
                    completeAttendance.Add(new AgentAttendanceModel
                    {
                        AgentId = actualUserId,
                        Date = date,
                        Status = "Absent",
                        CorrectionRequested = false,
                        CorrectionStatus = string.Empty
                    });
                }
            }

            attendance = completeAttendance.OrderBy(a => a.Date).ToList();

            var today = IndianTime.Today;

            var todayLogs = logs
                .Where(l => l.Timestamp.Date == today)
                .ToList();

            var todayIntervals = new List<(DateTime login, DateTime? logout)>();
            DateTime? lastLogin = null;

            foreach (var log in todayLogs)
            {
                if (log.Type == "Login")
                    lastLogin = log.Timestamp;

                else if (log.Type == "Logout" && lastLogin != null)
                {
                    todayIntervals.Add((lastLogin.Value, log.Timestamp));
                    lastLogin = null;
                }
            }

            if (lastLogin != null)
                todayIntervals.Add((lastLogin.Value, null));

            ViewBag.TodayLogIntervals = todayIntervals;
            ViewBag.TodayLogs = todayLogs;

            var isViewingCurrentMonth = (y == IndianTime.Now.Year && m == IndianTime.Now.Month);

            if (isViewingCurrentMonth)
            {
                var lastActivityToday = todayLogs
                    .OrderByDescending(l => l.Timestamp)
                    .FirstOrDefault();

                ViewBag.LastActivityIsLogin = lastActivityToday?.Type == "Login";
            }
            else
            {
                ViewBag.LastActivityIsLogin = false;
            }

            ViewBag.AgentId = actualUserId;
            ViewBag.Year = y;
            ViewBag.Month = m;
            ViewBag.DaysInMonth = daysInMonth;
            ViewBag.Attendance = attendance;
            ViewBag.AllUsers = allUsers;

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(int attendanceId, int? agentId = null, DateTime? date = null)
        {
            AgentAttendanceModel? record = null;
            int resolvedAgentId = agentId ?? 0;
            if (resolvedAgentId == 0)
            {
                // Try to get user id from JWT token/claims
                var userIdClaim = User.Claims.FirstOrDefault(c => c.Type == "sub" || c.Type == "userid" || c.Type == "UserId");
                if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int claimId))
                {
                    resolvedAgentId = claimId;
                }
                else if (User.Identity != null && !string.IsNullOrEmpty(User.Identity.Name) && int.TryParse(User.Identity.Name, out int nameId))
                {
                    resolvedAgentId = nameId;
                }
            }
            if (attendanceId > 0)
            {
                record = _context.AgentAttendance.Find(attendanceId);
            }
            else if (resolvedAgentId > 0)
            {
                var currentDate = IndianTime.Today;
                record = _context.AgentAttendance.FirstOrDefault(a => a.AgentId == resolvedAgentId && a.Date.Date == currentDate);
                if (record == null)
                {
                    record = new AgentAttendanceModel
                    {
                        AgentId = resolvedAgentId,
                        Date = currentDate,
                        Status = "Absent"
                    };
                    _context.AgentAttendance.Add(record);
                    await _context.SaveChangesAsync(); // Ensure AttendanceId is set
                }
            }
            if (record != null)
            {
                record.Status = "Present";
                try
                {
                    // Ensure AttendanceId exists in AgentAttendance
                    var attendanceExists = _context.AgentAttendance.Any(a => a.AttendanceId == record.AttendanceId);
                    if (!attendanceExists)
                    {
                        return RedirectToAction("Calendar", new { agentId = record.AgentId });
                    }
                    // Insert new AttendanceLog for login
                    var log = new AttendanceLogModel
                    {
                        AttendanceId = record.AttendanceId,
                        AgentId = record.AgentId,
                        Timestamp = IndianTime.Now,
                        Type = "Login"
                    };
                    _context.AttendanceLog.Add(log);
                    await _context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    // Login failed silently - user can retry
                }
            }
            //return RedirectToAction("Calendar", new { agentId = record?.AgentId ?? agentId });
            return RedirectToAction("Calendar", new
            {
                agentId = IdObfuscator.Encode(record?.AgentId ?? agentId.Value)
            });
        }

        [HttpPost]
        public async Task<IActionResult> Logout(int attendanceId)
        {
            var record = _context.AgentAttendance.Find(attendanceId);
            if (record != null)
            {
                record.Status = "Present";
                _context.AgentAttendance.Update(record);
                try
                {
                    // Ensure AttendanceId exists in AgentAttendance
                    var attendanceExists = _context.AgentAttendance.Any(a => a.AttendanceId == record.AttendanceId);
                    if (!attendanceExists)
                    {
                        return RedirectToAction("Calendar", new { agentId = record.AgentId });
                    }
                    // Insert new AttendanceLog for logout
                    var log = new AttendanceLogModel
                    {
                        AttendanceId = record.AttendanceId,
                        AgentId = record.AgentId,
                        Timestamp = IndianTime.Now,
                        Type = "Logout"
                    };
                    _context.AttendanceLog.Add(log);
                    await _context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    // Logout failed silently - user can retry
                }
            }
            //return RedirectToAction("Calendar", new { agentId = record?.AgentId });
            return RedirectToAction("Calendar", new
            {
                agentId = IdObfuscator.Encode(record.AgentId)
            });
        }

        [HttpPost]
        public async Task<IActionResult> RequestCorrection(int attendanceId, string reason, int? agentId = null, DateTime? date = null)
        {
            var record = _context.AgentAttendance.Find(attendanceId);
            if (record != null)
            {
                record.CorrectionRequested = true;
                record.CorrectionReason = reason;
                record.CorrectionStatus = "Pending";
                await _context.SaveChangesAsync();
                return Ok();
            }
            // If no record, try to create one if agentId and date are provided
            if (agentId.HasValue && date.HasValue)
            {
                var newRecord = new AgentAttendanceModel
                {
                    AgentId = agentId.Value,
                    Date = date.Value,
                    Status = "Absent",
                    CorrectionRequested = true,
                    CorrectionReason = reason,
                    CorrectionStatus = "Pending"
                };
                _context.AgentAttendance.Add(newRecord);
                await _context.SaveChangesAsync();
                return Ok();
            }
            return BadRequest("Attendance record not found and insufficient data to create.");
        }

        [HttpPost]
        public async Task<IActionResult> ApproveCorrection(int attendanceId, double? approvedHours = null)
        {
            var record = _context.AgentAttendance.Find(attendanceId);
            if (record != null && record.CorrectionRequested && record.CorrectionStatus == "Pending")
            {
                var hoursToApply = approvedHours ?? 9.0;
                if (hoursToApply <= 0 || hoursToApply > 24)
                {
                    return BadRequest("Approved hours must be between 0 and 24.");
                }

                record.CorrectionStatus = "Approved";
                record.Status = "Present";

                // Set correction hours using a fixed 09:00 baseline for consistency.
                var date = record.Date;
                record.LoginTime = new DateTime(date.Year, date.Month, date.Day, 9, 0, 0);
                record.LogoutTime = record.LoginTime.Value.AddHours(hoursToApply);

                _context.AgentAttendance.Update(record);
                await _context.SaveChangesAsync();
            }
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> RejectCorrection(int attendanceId)
        {
            var record = _context.AgentAttendance.Find(attendanceId);
            if (record != null && record.CorrectionRequested && record.CorrectionStatus == "Pending")
            {
                record.CorrectionStatus = "Rejected";
                await _context.SaveChangesAsync();
            }
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> RequestLogCorrection(int attendanceLogId, string reason)
        {
            var log = _context.AttendanceLog.Find(attendanceLogId);
            if (log != null)
            {
                log.CorrectionRequested = true;
                log.CorrectionReason = reason;
                log.CorrectionStatus = "Pending";
                await _context.SaveChangesAsync();
                return Ok();
            }
            return BadRequest("Attendance log not found.");
        }

        [HttpPost]
        public async Task<IActionResult> ApproveLogCorrection(int attendanceLogId)
        {
            var log = _context.AttendanceLog.Find(attendanceLogId);
            if (log != null && log.CorrectionRequested && log.CorrectionStatus == "Pending")
            {
                log.CorrectionStatus = "Approved";
                await _context.SaveChangesAsync();
            }
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> RejectLogCorrection(int attendanceLogId)
        {
            var log = _context.AttendanceLog.Find(attendanceLogId);
            if (log != null && log.CorrectionRequested && log.CorrectionStatus == "Pending")
            {
                log.CorrectionStatus = "Rejected";
                await _context.SaveChangesAsync();
            }
            return Ok();
        }

        [HttpGet]
        public IActionResult GetDateIntervals(int agentId, DateTime date)
        {
            var logs = _context.AttendanceLog
                .Where(l => l.AgentId == agentId && l.Timestamp.Date == date.Date)
                .OrderBy(l => l.Timestamp)
                .ToList();

            var intervals = new List<object>();
            DateTime? currentLogin = null;
            bool hasActiveSession = false;

            foreach (var log in logs)
            {
                if (log.Type == "Login")
                {
                    currentLogin = log.Timestamp;
                }
                else if (log.Type == "Logout" && currentLogin != null)
                {
                    var duration = (log.Timestamp - currentLogin.Value).TotalMinutes;
                    intervals.Add(new
                    {
                        login = currentLogin.Value.ToString("HH:mm:ss"),
                        logout = log.Timestamp.ToString("HH:mm:ss"),
                        duration = duration.ToString("0") + "m"
                    });
                    currentLogin = null;
                }
            }

            // If there's an active session (login without logout)
            if (currentLogin != null)
            {
                intervals.Add(new
                {
                    login = currentLogin.Value.ToString("HH:mm:ss"),
                    logout = (string)null,
                    duration = "Active"
                });
                hasActiveSession = true;
            }

            // Get attendance record ID for the date
            var attendance = _context.AgentAttendance
                .FirstOrDefault(a => a.AgentId == agentId && a.Date.Date == date.Date);

            return Json(new { 
                intervals, 
                hasActiveSession, 
                attendanceId = attendance?.AttendanceId ?? 0 
            });
        }
    }
}
