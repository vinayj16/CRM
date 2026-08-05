using CRM.Models;

namespace CRM.Services
{
    public class PayoutService
    {
        private readonly AppDbContext _context;

        public PayoutService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<decimal> CalculateAgentCommission(int agentId, decimal saleAmount)
        {
            var agent = await _context.Agents.FindAsync(agentId);
            if (agent == null || string.IsNullOrEmpty(agent.CommissionRules))
                return 0;

            // Agent commission can be fixed amount or percentage
            var commissionText = agent.CommissionRules.Replace("%", "").Replace("₹", "").Trim();
            if (decimal.TryParse(commissionText, out decimal amount))
            {
                // If CommissionRules contains %, it's percentage-based
                if (agent.CommissionRules.Contains("%"))
                {
                    return (saleAmount * amount) / 100;
                }
                else
                {
                    // Fixed amount commission
                    return amount;
                }
            }
            return 0;
        }

        public async Task<decimal> CalculateAttendanceDeduction(int agentId, string month, int year)
        {
            var agent = await _context.Agents.FindAsync(agentId);
            if (agent == null || agent.Salary == null) return 0;

            var startDate = new DateTime(year, DateTime.ParseExact(month, "MMM", null).Month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            var actualUserId = await ResolveAttendanceUserId(agentId, agent.Email);

            var totalWorkingDays = GetWorkingDaysInMonth(startDate, endDate);
            var attendanceRows = await _context.AgentAttendance
                .Where(a => a.AgentId == actualUserId && 
                           a.Date >= startDate && 
                           a.Date <= endDate)
                .ToListAsync();

            // Use one row per date and calculate full-day equivalent.
            var perDayRows = attendanceRows
                .GroupBy(a => a.Date.Date)
                .Select(g => g.OrderByDescending(x => x.AttendanceId).First())
                .ToList();

            var presentEquivalentDays = perDayRows.Sum(GetDayCredit);

            var absentDays = Math.Max(0m, totalWorkingDays - presentEquivalentDays);
            var dailySalary = agent.Salary.Value / totalWorkingDays;
            
            // Debug logging
            System.IO.File.AppendAllText("PayoutDebug.txt", $"Agent {agentId}: WorkingDays={totalWorkingDays}, PresentEquivalentDays={presentEquivalentDays:0.##}, AbsentDays={absentDays:0.##}, DailySalary={dailySalary:F2}, Deduction={absentDays * dailySalary:F2}\n");
            
            return absentDays * dailySalary;
        }

        public async Task ProcessMonthlyPayouts(string month, int year)
        {
            // Normalize month to 3-letter format for consistent processing
            month = NormalizeMonthFormat(month);

            // Process Agent Payouts
            await ProcessAgentPayouts(month, year);
            
            // Process Channel Partner Payouts
            await ProcessChannelPartnerPayouts(month, year);
        }

        private static string NormalizeMonthFormat(string month)
        {
            if (string.IsNullOrEmpty(month)) return DateTime.Now.ToString("MMM");
            if (month.Length <= 3) return month;
            try
            {
                return DateTime.ParseExact(month, "MMMM", null).ToString("MMM");
            }
            catch
            {
                return DateTime.Now.ToString("MMM");
            }
        }

        private static DateTime SafeParseMonth(string month, int year)
        {
            try
            {
                if (month.Length <= 3)
                    return new DateTime(year, DateTime.ParseExact(month, "MMM", null).Month, 1);
                return new DateTime(year, DateTime.ParseExact(month, "MMMM", null).Month, 1);
            }
            catch
            {
                return new DateTime(year, 1, 1);
            }
        }

        private async Task ProcessAgentPayouts(string month, int year)
        {
            var agents = await _context.Agents.Where(a => a.Status == "Approved").ToListAsync();
            var startDate = SafeParseMonth(month, year);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            // PROCESS MISSING COMMISSIONS FIRST
            var fullMonthName = startDate.ToString("MMMM");
            await ProcessMissingCommissions(startDate, endDate, fullMonthName, year);

            foreach (var agent in agents)
            {
                // Check if payout already exists
                var existingPayout = await _context.AgentPayouts
                    .FirstOrDefaultAsync(p => p.AgentId == agent.AgentId && p.Month == month && p.Year == year);
                
                if (existingPayout != null) continue;

                var baseSalary = agent.Salary ?? 0;
                var attendanceDeduction = await CalculateAttendanceDeduction(agent.AgentId, month, year);
                
                // Get existing commission logs for the month (use full month name)
                var totalCommission = await _context.AgentCommissionLogs
                    .Where(c => c.AgentId == agent.AgentId && c.Month == fullMonthName && c.Year == year)
                    .SumAsync(c => c.CommissionAmount);
                    
                var totalSales = await _context.AgentCommissionLogs
                    .Where(c => c.AgentId == agent.AgentId && c.Month == fullMonthName && c.Year == year)
                    .CountAsync();

                // Debug agent type
                System.IO.File.AppendAllText("PayoutDebug.txt", $"Agent {agent.AgentId}: AgentType='{agent.AgentType}', BaseSalary={baseSalary}, Deduction={attendanceDeduction}\n");
                
                // Calculate final payout based on agent type
                var correctFinalPayout = CalculateFinalPayout(agent.AgentType, baseSalary, attendanceDeduction, totalCommission);
                
                var payout = new AgentPayoutModel
                {
                    AgentId = agent.AgentId,
                    Month = month,
                    Year = year,
                    Period = $"{month}-{year}",
                    BaseSalary = baseSalary,
                    AttendanceDeduction = attendanceDeduction,
                    CommissionAmount = totalCommission,
                    FinalPayout = correctFinalPayout,
                    Amount = correctFinalPayout,
                    TotalSales = totalSales,
                    WorkingDays = GetWorkingDaysInMonth(startDate, endDate),
                    PresentDays = await GetPresentDays(agent.AgentId, startDate, endDate),
                    Status = "Processed"
                };
                
                // Debug final values
                System.IO.File.AppendAllText("PayoutDebug.txt", $"Final Payout Object: BaseSalary={payout.BaseSalary}, Deduction={payout.AttendanceDeduction}, FinalPayout={payout.FinalPayout}, Amount={payout.Amount}\n");

                _context.AgentPayouts.Add(payout);
            }

            await _context.SaveChangesAsync();
            
            // Fix any records with FinalPayout = 0 but should have positive value
            var zeroPayouts = await _context.AgentPayouts
                .Where(p => p.Month == month && p.Year == year && p.FinalPayout == 0 && p.BaseSalary > 0)
                .ToListAsync();
                
            foreach (var zeroPayout in zeroPayouts)
            {
                var correctAmount = zeroPayout.BaseSalary - zeroPayout.AttendanceDeduction;
                if (correctAmount > 0)
                {
                    zeroPayout.FinalPayout = correctAmount;
                    zeroPayout.Amount = correctAmount;
                    System.IO.File.AppendAllText("PayoutDebug.txt", $"Fixed zero payout for agent {zeroPayout.AgentId}: {correctAmount}\n");
                }
            }
            
            if (zeroPayouts.Any())
            {
                await _context.SaveChangesAsync();
            }
            
            // Update existing payouts with commission amounts from logs
            var existingPayouts = await _context.AgentPayouts
                .Where(p => p.Month == month && p.Year == year)
                .ToListAsync();
                
            foreach (var payout in existingPayouts)
            {
                var commissionTotal = await _context.AgentCommissionLogs
                    .Where(c => c.AgentId == payout.AgentId && c.Month == fullMonthName && c.Year == year)
                    .SumAsync(c => c.CommissionAmount);
                    
                var salesCount = await _context.AgentCommissionLogs
                    .Where(c => c.AgentId == payout.AgentId && c.Month == fullMonthName && c.Year == year)
                    .CountAsync();
                    
                payout.CommissionAmount = commissionTotal;
                payout.TotalSales = salesCount;
                
                var agent = await _context.Agents.FindAsync(payout.AgentId);
                if (agent != null)
                {
                    payout.FinalPayout = CalculateFinalPayout(agent.AgentType, payout.BaseSalary, payout.AttendanceDeduction, commissionTotal);
                    payout.Amount = payout.FinalPayout;
                }
            }
            
            await _context.SaveChangesAsync();
            System.IO.File.AppendAllText("PayoutDebug.txt", $"Updated existing payouts with commission data\n");
        }

        private async Task ProcessChannelPartnerPayouts(string month, int year)
        {
            var partners = await _context.ChannelPartners.Where(p => p.Status == "Approved").ToListAsync();
            var startDate = SafeParseMonth(month, year);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            foreach (var partner in partners)
            {
                // Check if payout already exists
                var existingPayout = await _context.PartnerPayouts
                    .FirstOrDefaultAsync(p => p.PartnerId == partner.PartnerId && p.Month == month && p.Year == year);
                
                if (existingPayout != null) continue;

                // Get partner sales for the month
                var sales = await GetPartnerSalesForMonth(partner.PartnerId, startDate, endDate);
                var commissionPercentage = GetPartnerCommissionPercentage(partner.CommissionScheme);
                var totalCommission = 0m;

                foreach (var sale in sales)
                {
                    // Calculate percentage-based commission
                    var commissionAmount = (sale.TotalAmount * commissionPercentage) / 100;
                    totalCommission += commissionAmount;

                    // Log commission
                    var commissionLog = new ChannelPartnerCommissionLogModel
                    {
                        PartnerId = partner.PartnerId,
                        BookingId = sale.BookingId,
                        FixedCommissionAmount = commissionAmount,
                        SaleDate = sale.BookingDate,
                        Month = month,
                        Year = year
                    };
                    _context.ChannelPartnerCommissionLogs.Add(commissionLog);
                }

                var payout = new PartnerPayoutModel
                {
                    PartnerId = partner.PartnerId,
                    Month = month,
                    Year = year,
                    FixedCommissionPerSale = commissionPercentage,
                    TotalSales = sales.Count,
                    TotalCommission = totalCommission,
                    Amount = totalCommission,
                    ConvertedLeads = sales.Count,
                    Status = "Processed"
                };

                _context.PartnerPayouts.Add(payout);
            }

            await _context.SaveChangesAsync();
        }

        private decimal CalculateFinalPayout(string agentType, decimal baseSalary, decimal deduction, decimal commission)
        {
            var normalizedType = agentType?.Trim().ToLower();
            
            var result = normalizedType switch
            {
                "salary" => baseSalary - deduction,
                "hybrid" => (baseSalary - deduction) + commission,
                "commission" => commission,
                _ => baseSalary - deduction
            };
            
            System.IO.File.AppendAllText("PayoutDebug.txt", $"CalculateFinalPayout: Original='{agentType}', Normalized='{normalizedType}', BaseSalary={baseSalary}, Deduction={deduction}, Commission={commission}, Result={result}\n");
            
            return result;
        }

        private async Task<List<BookingModel>> GetAgentSalesForMonth(int agentId, DateTime startDate, DateTime endDate)
        {
            // Find user by agent email mapping
            var agent = await _context.Agents.FindAsync(agentId);
            if (agent == null) return new List<BookingModel>();
            
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == agent.Email);
            if (user == null) return new List<BookingModel>();
            
            // Get bookings where Lead.ExecutiveId matches the user
            return await _context.Bookings
                
                            .Join(_context.Leads, (BookingModel b) => b.LeadId, (LeadModel l) => l.LeadId, (b, l) => new { Booking = b, Lead = l })
                .Where(bl => bl.Lead.ExecutiveId == user.UserId &&
                           bl.Booking.BookingDate >= startDate && 
                           bl.Booking.BookingDate <= endDate &&
                           bl.Booking.Status == "Confirmed")
                .Select(bl => bl.Booking)
                .ToListAsync();
        }

        private async Task<List<BookingModel>> GetPartnerSalesForMonth(int partnerId, DateTime startDate, DateTime endDate)
        {
            // Get leads from this partner that converted to bookings
            var partnerLeadIds = await _context.PartnerLeads
                .Where(pl => pl.PartnerId == partnerId)
                .Select(pl => pl.LeadId)
                .ToListAsync();

            return await _context.Bookings
                .Where(b => partnerLeadIds.Contains(b.LeadId) && 
                           b.BookingDate >= startDate && 
                           b.BookingDate <= endDate &&
                           b.Status == "Confirmed")
                .ToListAsync();
        }

        private decimal GetCommissionPercentage(string commissionRules)
        {
            if (string.IsNullOrEmpty(commissionRules)) return 0;
            var commissionText = commissionRules.Replace("%", "").Trim();
            return decimal.TryParse(commissionText, out decimal percentage) ? percentage : 0;
        }

        private decimal GetPartnerCommissionPercentage(string commissionScheme)
        {
            if (string.IsNullOrEmpty(commissionScheme)) return 0;
            var commissionText = commissionScheme.Replace("%", "").Trim();
            return decimal.TryParse(commissionText, out decimal percentage) ? percentage : 0;
        }

        private int GetWorkingDaysInMonth(DateTime startDate, DateTime endDate)
        {
            int workingDays = 0;
            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                    workingDays++;
            }
            return workingDays;
        }

        private async Task<int> GetPresentDays(int agentId, DateTime startDate, DateTime endDate)
        {
            var agent = await _context.Agents.FindAsync(agentId);
            var actualUserId = await ResolveAttendanceUserId(agentId, agent?.Email);

            var attendanceRows = await _context.AgentAttendance
                .Where(a => a.AgentId == actualUserId && 
                           a.Date >= startDate && 
                           a.Date <= endDate)
                .ToListAsync();

            var perDayRows = attendanceRows
                .GroupBy(a => a.Date.Date)
                .Select(g => g.OrderByDescending(x => x.AttendanceId).First())
                .ToList();

            var presentEquivalentDays = perDayRows.Sum(GetDayCredit);
            return (int)Math.Round(presentEquivalentDays, MidpointRounding.AwayFromZero);
        }

        private async Task<int> ResolveAttendanceUserId(int agentId, string? agentEmail)
        {
            var actualUserId = agentId;
            if (!string.IsNullOrWhiteSpace(agentEmail))
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == agentEmail);
                if (user != null)
                {
                    actualUserId = user.UserId;
                }
            }

            return actualUserId;
        }

        private static decimal GetDayCredit(AgentAttendanceModel row)
        {
            if (!string.Equals(row.Status, "Present", StringComparison.OrdinalIgnoreCase))
            {
                return 0m;
            }

            if (row.LoginTime.HasValue && row.LogoutTime.HasValue)
            {
                var hours = (decimal)(row.LogoutTime.Value - row.LoginTime.Value).TotalHours;
                if (hours >= 9m) return 1m;
                if (hours >= 4.5m) return 0.5m;
                return 0m;
            }

            // Legacy present rows without times are treated as full day.
            return 1m;
        }

        private async Task ProcessMissingCommissions(DateTime startDate, DateTime endDate, string month, int year)
        {
            // Get all bookings in the month that don't have commission logs
            var bookingsWithoutCommission = await _context.Bookings
                .Where(b => b.BookingDate >= startDate && b.BookingDate <= endDate && b.Status == "Confirmed")
                .Where(b => !_context.AgentCommissionLogs.Any(c => c.BookingId == b.BookingId))
                .ToListAsync();

            foreach (var booking in bookingsWithoutCommission)
            {
                var lead = await _context.Leads.FindAsync(booking.LeadId);
                var attributedUserId = ResolveCommissionAttributionUserId(lead);
                if (attributedUserId.HasValue)
                {
                    var user = await _context.Users.FindAsync(attributedUserId.Value);
                    if (user != null)
                    {
                        var agent = await _context.Agents.FirstOrDefaultAsync(a => a.Email == user.Email && a.Status == "Approved");
                        if (agent != null && agent.AgentType != "Salary")
                        {
                            // Parse commission percentage from "20% of sale" format
                            var commissionPercentage = 0m;
                            if (!string.IsNullOrEmpty(agent.CommissionRules))
                            {
                                var commissionText = agent.CommissionRules.Replace("% of sale", "").Replace("%", "").Trim();
                                decimal.TryParse(commissionText, out commissionPercentage);
                            }
                            var commissionAmount = (booking.TotalAmount * commissionPercentage) / 100;

                            var commissionLog = new AgentCommissionLogModel
                            {
                                AgentId = agent.AgentId,
                                BookingId = booking.BookingId,
                                SaleAmount = booking.TotalAmount,
                                CommissionPercentage = commissionPercentage,
                                CommissionAmount = commissionAmount,
                                SaleDate = booking.BookingDate,
                                Month = month,
                                Year = year
                            };
                            _context.AgentCommissionLogs.Add(commissionLog);
                        }
                    }
                }
            }
            await _context.SaveChangesAsync();
        }

        private static int? ResolveCommissionAttributionUserId(LeadModel? lead)
        {
            if (lead == null)
            {
                return null;
            }

            // For partner-owned leads, use partner-side assignment so admin handover reassignment does not erase credit.
            if (lead.ChannelPartnerId.HasValue && lead.PartnerAssignedAgentUserId.HasValue)
            {
                return lead.PartnerAssignedAgentUserId;
            }

            return lead.ExecutiveId;
        }
    }
}