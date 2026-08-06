using CRM.Helpers;
using CRM.Models;
using CRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CRM.Controllers
{
    public class AgentPayoutController : Controller
    {
        private readonly AppDbContext _context;
        private readonly PayoutService _payoutService;
        private readonly PayslipService _payslipService;
        private readonly ILogger<AgentPayoutController> _logger;

        public AgentPayoutController(AppDbContext context, PayoutService payoutService, PayslipService payslipService, ILogger<AgentPayoutController> logger)
        {
            _context = context;
            _payoutService = payoutService;
            _payslipService = payslipService;
            _logger = logger;
        }
        public async Task<IActionResult> Index(string month = null, int? year = null, string search = null, int? agentId = null)
        {
            try
            {
                var role = User.FindFirst(ClaimTypes.Role)?.Value;
                var uid = User.FindFirst("UserId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(uid, out int userId);
                var currentUser = _context.Users.FirstOrDefault(u => u.UserId == userId);
                var channelPartnerId = currentUser?.ChannelPartnerId;
                
                month ??= IndianTime.Now.ToString("MMM");
                year ??= IndianTime.Now.Year;

                System.IO.File.AppendAllText("PayoutDebug.txt", $"Loading payouts for {month} {year}\n");

                var agentsQuery = _context.Agents.AsQueryable();
                if (role?.ToLower() == "partner")
                    agentsQuery = agentsQuery.Where(a => a.ChannelPartnerId == channelPartnerId);
                else if (role?.ToLower() == "admin")
                    agentsQuery = agentsQuery.Where(a => a.ChannelPartnerId == null);
                else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
                {
                    var userAgent = await _context.Agents.FirstOrDefaultAsync(a => a.AgentId == userId);
                    if (userAgent != null)
                        agentsQuery = agentsQuery.Where(a => a.AgentId == userAgent.AgentId);
                }

                var agentIds = agentsQuery.Select(a => a.AgentId).ToList();
                var query = _context.AgentPayouts
                    
                    .Where(p => p.Month == month && p.Year == year && agentIds.Contains(p.AgentId));

                // Apply filters
                if (agentId.HasValue)
                    query = query.Where(p => p.AgentId == agentId.Value);

                if (!string.IsNullOrEmpty(search))
                    query = query.Where(p => p.Agent.FullName.Contains(search));

                var payouts = await query.OrderByDescending(p => p.FinalPayout).ToListAsync();

                System.IO.File.AppendAllText("PayoutDebug.txt", $"Found {payouts.Count} payouts\n");

                // Calculate summary stats
                ViewBag.TotalPayouts = payouts.Sum(p => p.FinalPayout);
                ViewBag.TotalSales = payouts.Sum(p => p.TotalSales);
                ViewBag.TotalCommission = payouts.Sum(p => p.CommissionAmount);
                ViewBag.TotalAgents = payouts.Count;

                // Filter data
                ViewBag.Month = month;
                ViewBag.Year = year;
                ViewBag.Search = search;
                ViewBag.AgentId = agentId;
                ViewBag.Agents = await _context.Agents.Where(a => a.Status == "Approved").ToListAsync();

                return View(payouts);
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText("PayoutDebug.txt", $"Error in Index: {ex.Message}\n{ex.StackTrace}\n");
                ViewBag.ErrorMessage = ex.Message;
                return View(new List<AgentPayoutModel>());
            }
        }
    
        public async Task<IActionResult> Details(int id, string month = null, int? year = null)
        {
            //var decodedId = IdObfuscator.Decode(id.ToString());
            //if (decodedId == null)
            //{
            //    return NotFound();
            //}

            //ViewBag.EncodedId = id;
            month ??= IndianTime.Now.ToString("MMMM");
            year ??= IndianTime.Now.Year;

            // Handle both "Dec" and "December" formats for payout lookup
            var payout = await _context.AgentPayouts
                
                .FirstOrDefaultAsync(p => p.AgentId == id && p.Year == year &&
                    (p.Month == month ||
                     (month.Length == 3 && p.Month == DateTime.ParseExact(month, "MMM", null).ToString("MMMM")) ||
                     (month.Length > 3 && p.Month == DateTime.ParseExact(month, "MMMM", null).ToString("MMM"))));

            if (payout == null)
            {
                return NotFound();
            }

            // Convert month parameter to both formats for lookup
            var shortMonth = month.Length == 3 ? month : DateTime.ParseExact(month, "MMMM", null).ToString("MMM");
            var fullMonth = month.Length > 3 ? month : DateTime.ParseExact(month, "MMM", null).ToString("MMMM");

            // Get commission logs for this agent and month (check both formats)
            var commissionLogs = await _context.AgentCommissionLogs
                .Include(c => c.Booking)
                .Where(c => c.AgentId == id && c.Year == year &&
                    (c.Month == fullMonth || c.Month == shortMonth || c.Month == month))
                .ToListAsync();

            // Get attendance summary using full month name
            var startDate = new DateTime(year.Value, DateTime.ParseExact(fullMonth, "MMMM", null).Month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            // Attendance table uses UserId mapping; resolve from Agent email first.
            var attendanceUserId = id;
            if (!string.IsNullOrWhiteSpace(payout.Agent?.Email))
            {
                var mappedUser = await _context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Email == payout.Agent.Email);
                if (mappedUser != null)
                {
                    attendanceUserId = mappedUser.UserId;
                }
            }

            var rawAttendanceRecords = await _context.AgentAttendance
                .Where(a => a.AgentId == attendanceUserId && a.Date >= startDate && a.Date <= endDate)
                .OrderBy(a => a.Date)
                .ToListAsync();

            // Build complete working-day records so missing rows are treated as absent.
            var attendanceByDate = rawAttendanceRecords
                .GroupBy(a => a.Date.Date)
                .ToDictionary(g => g.Key, g => g.First());

            var attendanceRecords = new List<AgentAttendanceModel>();
            for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
            {
                if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                {
                    continue;
                }

                if (attendanceByDate.TryGetValue(date, out var existingRecord))
                {
                    attendanceRecords.Add(existingRecord);
                }
                else
                {
                    attendanceRecords.Add(new AgentAttendanceModel
                    {
                        AgentId = attendanceUserId,
                        Date = date,
                        Status = "Absent",
                        CorrectionRequested = false,
                        CorrectionStatus = string.Empty
                    });
                }
            }

            var liveWorkingDays = attendanceRecords.Count;
            var livePresentDays = attendanceRecords.Count(a => a.Status == "Present");
            var liveAttendanceDeduction = await _payoutService.CalculateAttendanceDeduction(id, shortMonth, year.Value);

            var normalizedType = payout.Agent?.AgentType?.Trim().ToLower();
            var liveFinalPayout = normalizedType switch
            {
                "salary" => payout.BaseSalary - liveAttendanceDeduction,
                "hybrid" => (payout.BaseSalary - liveAttendanceDeduction) + payout.CommissionAmount,
                "commission" => payout.CommissionAmount,
                _ => payout.BaseSalary - liveAttendanceDeduction
            };

            if (payout.WorkingDays != liveWorkingDays ||
                payout.PresentDays != livePresentDays ||
                payout.AttendanceDeduction != liveAttendanceDeduction ||
                payout.FinalPayout != liveFinalPayout ||
                payout.Amount != liveFinalPayout)
            {
                payout.WorkingDays = liveWorkingDays;
                payout.PresentDays = livePresentDays;
                payout.AttendanceDeduction = liveAttendanceDeduction;
                payout.FinalPayout = liveFinalPayout;
                payout.Amount = liveFinalPayout;

                _context.AgentPayouts.Update(payout);
                await _context.SaveChangesAsync();
            }

            // Update payout with actual commission data if it's missing
            if (payout.CommissionAmount == 0 && commissionLogs.Any())
            {
                payout.CommissionAmount = commissionLogs.Sum(c => c.CommissionAmount);
                payout.TotalSales = commissionLogs.Count;

                // Recalculate final payout based on agent type
                if (payout.Agent?.AgentType?.ToLower() == "hybrid")
                {
                    payout.FinalPayout = payout.BaseSalary - payout.AttendanceDeduction + payout.CommissionAmount;
                }
                else if (payout.Agent?.AgentType?.ToLower() == "commission")
                {
                    payout.FinalPayout = payout.CommissionAmount;
                }

                payout.Amount = payout.FinalPayout;

                // Save the updated payout
                _context.AgentPayouts.Update(payout);
                await _context.SaveChangesAsync();
            }

            ViewBag.CommissionLogs = commissionLogs;
            ViewBag.AttendanceRecords = attendanceRecords;
            ViewBag.Month = fullMonth;
            ViewBag.Year = year;

            return View(payout);
        }

        [HttpPost]
        public async Task<IActionResult> ProcessPayouts(string month, int year)
        {
            try
            {
                await _payoutService.ProcessMonthlyPayouts(month, year);
                return Json(new { success = true, message = "Payouts processed successfully" });
            }
            catch (Exception ex)
            {
                var innerMessage = ex.InnerException?.Message ?? "No inner exception";
                var fullMessage = $"{ex.Message} | Inner: {innerMessage}";
                System.IO.File.AppendAllText("PayoutError.txt", $"Error: {fullMessage}\nStack: {ex.StackTrace}\n\n");
                return Json(new { success = false, message = fullMessage });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStatus(int payoutId, string status)
        {
            try
            {
                var payout = await _context.AgentPayouts.FindAsync(payoutId);
                if (payout == null)
                    return Json(new { success = false, message = "Payout not found" });

                payout.Status = status;
                if (status == "Paid")
                    payout.ProcessedOn = IndianTime.Now;

                _context.AgentPayouts.Update(payout);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Status updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // PAYSLIP GENERATION
        public async Task<IActionResult> Payslip(int agentId, string month = null, int? year = null)
        {
            month ??= IndianTime.Now.ToString("MMMM");
            year ??= IndianTime.Now.Year;

            var agent = await _context.Agents.FindAsync(agentId);
            if (agent == null)
                return NotFound();

            var payslip = await _payslipService.GenerateAgentPayslip(agent, month, year.Value);
            return View(payslip);
        }

        [HttpPost]
        public async Task<IActionResult> GeneratePayslips(string month, int year)
        {
            try
            {
                await _payslipService.GenerateMonthlyPayslips(month, year);
                return Json(new { success = true, message = "Payslips generated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // MANUAL PAYOUT PROCESSING FOR ADMIN
        [HttpGet]
        public async Task<IActionResult> CleanDecember()
        {
            try
            {
                // Delete December 2025 payouts
                var payouts = await _context.AgentPayouts
                    .Where(p => p.Month == "Dec" && p.Year == 2025)
                    .ToListAsync();
                _context.AgentPayouts.RemoveRange(payouts);
                
                // Delete December 2025 commission logs
                var logs = await _context.AgentCommissionLogs
                    .Where(c => c.Year == 2025 && (c.Month == "Dec" || c.Month == "December"))
                    .ToListAsync();
                _context.AgentCommissionLogs.RemoveRange(logs);
                
                await _context.SaveChangesAsync();
                
                return Json(new { success = true, message = $"Deleted {payouts.Count} payouts and {logs.Count} commission logs for December 2025" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        [HttpGet]
        public async Task<IActionResult> FixPayout()
        {
            try
            {
                // Get the payout record for agent 7, Dec 2025
                var payout = await _context.AgentPayouts
                    .FirstOrDefaultAsync(p => p.AgentId == 7 && p.Month == "Dec" && p.Year == 2025);
                    
                if (payout == null)
                    return Json(new { success = false, message = "Payout not found" });
                
                // Get commission logs (they use "December" not "Dec")
                var commissionTotal = await _context.AgentCommissionLogs
                    .Where(c => c.AgentId == 7 && (c.Month == "Dec" || c.Month == "December") && c.Year == 2025)
                    .SumAsync(c => c.CommissionAmount);
                    
                var salesCount = await _context.AgentCommissionLogs
                    .Where(c => c.AgentId == 7 && (c.Month == "Dec" || c.Month == "December") && c.Year == 2025)
                    .CountAsync();
                
                // Update payout with commission data
                payout.CommissionAmount = commissionTotal;
                payout.TotalSales = salesCount;
                
                // Recalculate final payout for Hybrid agent: BaseSalary - Deduction + Commission
                payout.FinalPayout = payout.BaseSalary - payout.AttendanceDeduction + commissionTotal;
                payout.Amount = payout.FinalPayout;
                
                await _context.SaveChangesAsync();
                
                return Json(new { 
                    success = true, 
                    message = $"Updated payout: Commission=₹{commissionTotal:N0}, FinalPayout=₹{payout.FinalPayout:N0}"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        [HttpGet]
        public async Task<IActionResult> TestPayoutFix()
        {
            try
            {
                await _payoutService.ProcessMonthlyPayouts("Dec", 2025);
                return Json(new { success = true, message = "December 2025 payouts recalculated with commission data" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        [HttpGet]
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> DebugPayouts()
        {
            try
            {
                var allPayouts = await _context.AgentPayouts.ToListAsync();
                var debug = allPayouts.Select(p => new
                {
                    p.PayoutId,
                    p.AgentId,
                    p.Month,
                    p.Year,
                    p.BaseSalary,
                    p.CommissionAmount,
                    p.Amount,
                    p.FinalPayout,
                    p.Status
                }).ToList();
                var agents = await _context.Agents
                    .Select(a => new { a.AgentId, a.FullName, a.ChannelPartnerId })
                    .ToListAsync();
                return Json(new { payouts = debug, agents = agents });
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> DebugData()
        {
            try
            {
                var startDate = new DateTime(2025, 12, 1);
                var endDate = new DateTime(2025, 12, 31);
                
                var bookings = await _context.Bookings
                    .Where(b => b.BookingDate >= startDate && b.BookingDate <= endDate)
                    .Select(b => new { b.BookingNumber, b.Status, b.LeadId, b.TotalAmount })
                    .ToListAsync();
                    
                var agents = await _context.Agents
                    .Where(a => a.Status == "Approved")
                    .Select(a => new { a.AgentId, a.FullName, a.Email, a.AgentType })
                    .ToListAsync();
                    
                var users = await _context.Users
                    .Select(u => new { u.UserId, u.Username, u.Email })
                    .ToListAsync();
                    
                var leads = await _context.Leads
                    .Where(l => bookings.Select(b => b.LeadId).Contains(l.LeadId))
                    .Select(l => new { l.LeadId, l.ExecutiveId })
                    .ToListAsync();
                
                return Json(new { 
                    bookings = bookings,
                    agents = agents,
                    users = users,
                    leads = leads
                });
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }
        
        [HttpGet]
        public async Task<IActionResult> TestRecalculation()
        {
            try
            {
                // Use month number directly to avoid parsing issues
                var startDate = new DateTime(2025, 12, 1);
                var endDate = new DateTime(2025, 12, 31);
                
                System.IO.File.AppendAllText("CommissionDebug.txt", $"\n=== Direct Recalculation Dec 2025 ===\n");
                
                // Get completed bookings for December 2025
                var bookings = await _context.Bookings
                    .Where(b => b.BookingDate >= startDate && 
                               b.BookingDate <= endDate && 
                               (b.Status == "Confirmed" || b.Status == "Completed"))
                    .ToListAsync();
                
                System.IO.File.AppendAllText("CommissionDebug.txt", $"Found {bookings.Count} bookings\n");
                
                foreach (var booking in bookings)
                {
                    System.IO.File.AppendAllText("CommissionDebug.txt", $"Processing {booking.BookingNumber}\n");
                    
                    var lead = await _context.Leads.FindAsync(booking.LeadId);
                    System.IO.File.AppendAllText("CommissionDebug.txt", $"Lead found: {lead != null}, ExecutiveId: {lead?.ExecutiveId}\n");
                    
                    if (lead?.ExecutiveId != null)
                    {
                        var user = await _context.Users.FindAsync(lead.ExecutiveId.Value);
                        System.IO.File.AppendAllText("CommissionDebug.txt", $"User found: {user != null}, Username: '{user?.Username}', Email: '{user?.Email}'\n");
                        
                        if (user != null)
                        {
                            var agent = await _context.Agents.FirstOrDefaultAsync(a => a.FullName == user.Username && a.Email == user.Email && a.Status == "Approved");
                            System.IO.File.AppendAllText("CommissionDebug.txt", $"Agent found: {agent != null}, FullName: '{agent?.FullName}', AgentType: '{agent?.AgentType}'\n");
                            
                            if (agent != null && agent.AgentType != "Salary")
                            {
                                System.IO.File.AppendAllText("CommissionDebug.txt", $"Creating commission for {agent.FullName}\n");
                                
                                // Calculate commission using agent's rules
                                var commissionPercentage = GetCommissionPercentage(agent.CommissionRules);
                                var commissionAmount = booking.TotalAmount * (commissionPercentage / 100);
                                
                                var commissionLog = new AgentCommissionLogModel
                                {
                                    AgentId = agent.AgentId,
                                    BookingId = booking.BookingId,
                                    SaleAmount = booking.TotalAmount,
                                    CommissionPercentage = commissionPercentage,
                                    CommissionAmount = commissionAmount,
                                    SaleDate = booking.BookingDate,
                                    Month = "December",
                                    Year = 2025
                                };
                                _context.AgentCommissionLogs.Add(commissionLog);
                                System.IO.File.AppendAllText("CommissionDebug.txt", $"Commission log added to context\n");
                            }
                            else
                            {
                                System.IO.File.AppendAllText("CommissionDebug.txt", $"Skipped: Agent is null or Salary type\n");
                            }
                        }
                        else
                        {
                            System.IO.File.AppendAllText("CommissionDebug.txt", $"User not found for ExecutiveId {lead.ExecutiveId}\n");
                        }
                    }
                    else
                    {
                        System.IO.File.AppendAllText("CommissionDebug.txt", $"Lead not found or ExecutiveId is null\n");
                    }
                }
                
                await _context.SaveChangesAsync();
                
                return Json(new { success = true, message = "Commission logs created. Check AgentCommissionLogs table." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> RecalculatePayouts(string month, int year)
        {
            try
            {
                // Delete existing payouts for the month
                var existingPayouts = await _context.AgentPayouts
                    .Where(p => p.Month == month && p.Year == year)
                    .ToListAsync();
                _context.AgentPayouts.RemoveRange(existingPayouts);
                
                // Delete existing commission logs for the month
                var existingLogs = await _context.AgentCommissionLogs
                    .Where(c => c.Month == month && c.Year == year)
                    .ToListAsync();
                _context.AgentCommissionLogs.RemoveRange(existingLogs);
                
                await _context.SaveChangesAsync();
                
                // Recalculate from bookings
                await RecalculateFromBookings(month, year);
                
                return Json(new { success = true, message = "Payouts recalculated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private async Task RecalculateFromBookings(string month, int year)
        {
            int monthNumber = DateTime.ParseExact(month, "MMMM", System.Globalization.CultureInfo.InvariantCulture).Month;
            var startDate = new DateTime(year, monthNumber, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            System.IO.File.AppendAllText("CommissionDebug.txt", $"\n=== Recalculating {month} {year} ===\n");
            System.IO.File.AppendAllText("CommissionDebug.txt", $"Date range: {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}\n");

            // Get all confirmed bookings for the month
            var bookings = await _context.Bookings
                .Where(b => b.BookingDate >= startDate && 
                           b.BookingDate <= endDate && 
                           b.Status == "Confirmed")
                .ToListAsync();

            System.IO.File.AppendAllText("CommissionDebug.txt", $"Found {bookings.Count} confirmed bookings\n");

            foreach (var booking in bookings)
            {
                System.IO.File.AppendAllText("CommissionDebug.txt", $"\nProcessing booking {booking.BookingNumber} (LeadId: {booking.LeadId})\n");
                
                // Get the lead to find ExecutiveId
                var lead = await _context.Leads.FindAsync(booking.LeadId);
                System.IO.File.AppendAllText("CommissionDebug.txt", $"Lead found: {lead != null}, ExecutiveId: {lead?.ExecutiveId}\n");
                
                if (lead?.ExecutiveId != null)
                {
                    // Find user by ExecutiveId, then find matching agent by email
                    var user = await _context.Users.FindAsync(lead.ExecutiveId.Value);
                    System.IO.File.AppendAllText("CommissionDebug.txt", $"User found: {user != null}, Username: {user?.Username}, Email: {user?.Email}\n");
                    
                    if (user != null)
                    {
                        // First try to find agent with matching username and email
                        var agent = await _context.Agents.FirstOrDefaultAsync(a => a.FullName == user.Username && a.Email == user.Email && a.Status == "Approved");
                        System.IO.File.AppendAllText("CommissionDebug.txt", $"Agent by name+email: {agent?.FullName} (Type: {agent?.AgentType})\n");
                        
                        // If not found, fall back to email match
                        if (agent == null)
                        {
                            agent = await _context.Agents.FirstOrDefaultAsync(a => a.Email == user.Email && a.Status == "Approved");
                            System.IO.File.AppendAllText("CommissionDebug.txt", $"Agent by email only: {agent?.FullName} (Type: {agent?.AgentType})\n");
                        }
                        
                        if (agent != null && agent.AgentType != "Salary")
                        {
                            System.IO.File.AppendAllText("CommissionDebug.txt", $"Creating commission for Agent {agent.FullName} (ID: {agent.AgentId})\n");
                            
                            var commissionLog = new AgentCommissionLogModel
                            {
                                AgentId = agent.AgentId,
                                BookingId = booking.BookingId,
                                SaleAmount = booking.TotalAmount,
                                CommissionPercentage = 10,
                                CommissionAmount = booking.TotalAmount * 0.10m,
                                SaleDate = booking.BookingDate,
                                Month = "December",
                                Year = 2025
                            };
                            _context.AgentCommissionLogs.Add(commissionLog);
                        }
                    }
                }
            }
            
            await _context.SaveChangesAsync();
        }
        
        private decimal GetCommissionPercentage(string commissionRules)
        {
            if (string.IsNullOrEmpty(commissionRules)) return 0;
            
            // Handle "20% of sale" or "10%" formats
            var commissionText = commissionRules.Replace("% of sale", "").Replace("%", "").Trim();
            return decimal.TryParse(commissionText, out decimal percentage) ? percentage : 0;
        }
    }
}