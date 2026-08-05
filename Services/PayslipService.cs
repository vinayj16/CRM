using CRM.Helpers;
using CRM.Models;

namespace CRM.Services
{
    public class PayslipService
    {
        private readonly AppDbContext _context;
        private readonly PayoutService _payoutService;

        public PayslipService(AppDbContext context, PayoutService payoutService)
        {
            _context = context;
            _payoutService = payoutService;
        }

        public async Task GenerateMonthlyPayslips(string month, int year)
        {
            // Generate payslips for all approved agents
            await GenerateAgentPayslips(month, year);
            
            // Generate payslips for all approved channel partners
            await GeneratePartnerPayslips(month, year);
        }

        private async Task GenerateAgentPayslips(string month, int year)
        {
            var agents = await _context.Agents.Where(a => a.Status == "Approved").ToListAsync();
            
            foreach (var agent in agents)
            {
                await GenerateAgentPayslip(agent, month, year);
            }
        }

        private async Task GeneratePartnerPayslips(string month, int year)
        {
            var partners = await _context.ChannelPartners.Where(p => p.Status == "Approved").ToListAsync();
            
            foreach (var partner in partners)
            {
                await GeneratePartnerPayslip(partner, month, year);
            }
        }

        public async Task<AgentPayslipViewModel> GenerateAgentPayslip(AgentModel agent, string month, int year)
        {
            var startDate = new DateTime(year, DateTime.ParseExact(month, "MMMM", null).Month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            // Get or create payout record
            var payout = await _context.AgentPayouts
                .FirstOrDefaultAsync(p => p.AgentId == agent.AgentId && p.Month == month && p.Year == year);

            if (payout == null)
            {
                // Create payout if doesn't exist
                await _payoutService.ProcessMonthlyPayouts(month, year);
                payout = await _context.AgentPayouts
                    .FirstOrDefaultAsync(p => p.AgentId == agent.AgentId && p.Month == month && p.Year == year);
            }

            // Get commission details
            var commissionLogs = await _context.AgentCommissionLogs
                .Include(c => c.Booking)
                .Where(c => c.AgentId == agent.AgentId && c.Month == month && c.Year == year)
                .ToListAsync();

            // Get attendance details
            var attendanceRecords = await _context.AgentAttendance
                .Where(a => a.AgentId == agent.AgentId && 
                           a.Date >= startDate && 
                           a.Date <= endDate)
                .ToListAsync();

            var payslip = new AgentPayslipViewModel
            {
                Agent = agent,
                Month = month,
                Year = year,
                Payout = payout ?? new AgentPayoutModel { AgentId = agent.AgentId, Month = month, Year = year },
                CommissionLogs = commissionLogs,
                AttendanceRecords = attendanceRecords,
                WorkingDays = GetWorkingDaysInMonth(startDate, endDate),
                PresentDays = attendanceRecords.Count(a => a.Status == "Present"),
                AbsentDays = attendanceRecords.Count(a => a.Status == "Absent"),
                GeneratedOn = IndianTime.Now
            };

            return payslip;
        }

        public async Task<PartnerPayslipViewModel> GeneratePartnerPayslip(ChannelPartnerModel partner, string month, int year)
        {
            // Get or create payout record
            var payout = await _context.PartnerPayouts
                .FirstOrDefaultAsync(p => p.PartnerId == partner.PartnerId && p.Month == month && p.Year == year);

            if (payout == null)
            {
                // Create payout if doesn't exist
                await _payoutService.ProcessMonthlyPayouts(month, year);
                payout = await _context.PartnerPayouts
                    .FirstOrDefaultAsync(p => p.PartnerId == partner.PartnerId && p.Month == month && p.Year == year);
            }

            // Get commission details
            var commissionLogs = await _context.ChannelPartnerCommissionLogs
                .Include(c => c.Booking)
                .Where(c => c.PartnerId == partner.PartnerId && c.Month == month && c.Year == year)
                .ToListAsync();

            // Get leads submitted by partner
            var partnerLeads = await _context.PartnerLeads
                .Where(pl => pl.PartnerId == partner.PartnerId)
                .ToListAsync();

            var payslip = new PartnerPayslipViewModel
            {
                Partner = partner,
                Month = month,
                Year = year,
                Payout = payout ?? new PartnerPayoutModel { PartnerId = partner.PartnerId, Month = month, Year = year },
                CommissionLogs = commissionLogs,
                PartnerLeads = partnerLeads,
                TotalLeadsSubmitted = partnerLeads.Count,
                ConvertedLeads = commissionLogs.Count,
                ConversionRate = partnerLeads.Count > 0 ? (decimal)commissionLogs.Count / partnerLeads.Count * 100 : 0,
                GeneratedOn = IndianTime.Now
            };

            return payslip;
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
    }

    // ViewModels for Payslips
    public class AgentPayslipViewModel
    {
        public AgentModel Agent { get; set; }
        public string Month { get; set; }
        public int Year { get; set; }
        public AgentPayoutModel Payout { get; set; }
        public List<AgentCommissionLogModel> CommissionLogs { get; set; } = new();
        public List<AgentAttendanceModel> AttendanceRecords { get; set; } = new();
        public int WorkingDays { get; set; }
        public int PresentDays { get; set; }
        public int AbsentDays { get; set; }
        public DateTime GeneratedOn { get; set; }

        // Calculated Properties
        public decimal AttendancePercentage => WorkingDays > 0 ? (decimal)PresentDays / WorkingDays * 100 : 0;
        public decimal PerDaySalary => WorkingDays > 0 && Payout.BaseSalary > 0 ? Payout.BaseSalary / WorkingDays : 0;
        public decimal EarnedSalary => PerDaySalary * PresentDays;
        public string PayslipType => Agent?.AgentType switch
        {
            "FixedSalary" => "Salary Only",
            "SalaryPlusCommission" => "Salary + Commission",
            "CommissionOnly" => "Commission Only",
            _ => "Unknown"
        };
    }

    public class PartnerPayslipViewModel
    {
        public ChannelPartnerModel Partner { get; set; }
        public string Month { get; set; }
        public int Year { get; set; }
        public PartnerPayoutModel Payout { get; set; }
        public List<ChannelPartnerCommissionLogModel> CommissionLogs { get; set; } = new();
        public List<PartnerLeadModel> PartnerLeads { get; set; } = new();
        public int TotalLeadsSubmitted { get; set; }
        public int ConvertedLeads { get; set; }
        public decimal ConversionRate { get; set; }
        public DateTime GeneratedOn { get; set; }

        // Calculated Properties
        public decimal AverageCommissionPerSale => ConvertedLeads > 0 ? Payout.TotalCommission / ConvertedLeads : 0;
        public decimal TotalSalesValue => CommissionLogs.Sum(c => c.Booking?.TotalAmount ?? 0);
    }
}