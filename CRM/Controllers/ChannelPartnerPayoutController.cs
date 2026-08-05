using CRM.Helpers;
using CRM.Models;
using CRM.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CRM.Controllers
{
    public class ChannelPartnerPayoutController : Controller
    {
        private readonly AppDbContext _context;
        private readonly PayoutService _payoutService;
        private readonly PayslipService _payslipService;
        private readonly ILogger<ChannelPartnerPayoutController> _logger;

        public ChannelPartnerPayoutController(AppDbContext context, PayoutService payoutService, PayslipService payslipService, ILogger<ChannelPartnerPayoutController> logger)
        {
            _context = context;
            _payoutService = payoutService;
            _payslipService = payslipService;
            _logger = logger;
        }

        public async Task<IActionResult> Index(string month = null, int? year = null, string search = null, int? partnerId = null)
        {
            var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            
            // Default to current month/year
            month ??= IndianTime.Now.ToString("MMMM");
            year ??= IndianTime.Now.Year;

            // Handle both month formats in Index as well
            var fullMonth = month.Length == 3 ? DateTime.ParseExact(month, "MMM", null).ToString("MMMM") : month;
            var shortMonth = month.Length > 3 ? DateTime.ParseExact(month, "MMMM", null).ToString("MMM") : month;
            
            var query = _context.PartnerPayouts
                
                .Where(p => p.Year == year && (p.Month == month || p.Month == fullMonth || p.Month == shortMonth));

            // Non-admin users can only see their own payouts
            if (userRole != "Admin")
            {
                var userPartner = await _context.ChannelPartners.FirstOrDefaultAsync(p => p.PartnerId == currentUserId);
                if (userPartner != null)
                {
                    query = query.Where(p => p.PartnerId == userPartner.PartnerId);
                }
            }

            // Apply filters
            if (partnerId.HasValue)
                query = query.Where(p => p.PartnerId == partnerId.Value);

            if (!string.IsNullOrEmpty(search))
                query = query.Where(p => p.Partner.CompanyName.Contains(search) || p.Partner.ContactPerson.Contains(search));

            var payouts = await query.OrderByDescending(p => p.TotalCommission).ToListAsync();

            // Calculate summary stats
            ViewBag.TotalPayouts = payouts.Sum(p => p.TotalCommission);
            ViewBag.TotalSales = payouts.Sum(p => p.TotalSales);
            ViewBag.TotalPartners = payouts.Count;
            ViewBag.AverageCommission = payouts.Any() ? payouts.Average(p => p.FixedCommissionPerSale) : 0;

            // Filter data
            ViewBag.Month = month;
            ViewBag.Year = year;
            ViewBag.Search = search;
            ViewBag.PartnerId = partnerId;
            ViewBag.Partners = await _context.ChannelPartners.Where(p => p.Status == "Approved").ToListAsync();

            return View(payouts);
        }

        public async Task<IActionResult> Details(int id, string month = null, int? year = null)
        {
            month ??= IndianTime.Now.ToString("MMMM");
            year ??= IndianTime.Now.Year;

            // Handle both "Dec" and "December" formats for payout lookup
            var payout = await _context.PartnerPayouts
                
                .FirstOrDefaultAsync(p => p.PartnerId == id && p.Year == year && 
                    (p.Month == month || 
                     (month.Length == 3 && p.Month == DateTime.ParseExact(month, "MMM", null).ToString("MMMM")) ||
                     (month.Length > 3 && p.Month == DateTime.ParseExact(month, "MMMM", null).ToString("MMM"))));

            if (payout == null)
            {
                return NotFound();
            }

            // Convert to full month name for commission logs
            var fullMonth = month.Length == 3 ? DateTime.ParseExact(month, "MMM", null).ToString("MMMM") : month;
            
            // Get commission logs for this partner and month
            var commissionLogs = await _context.ChannelPartnerCommissionLogs
                .Include(c => c.Booking)
                .Where(c => c.PartnerId == id && c.Year == year && 
                    (c.Month == fullMonth || c.Month == month))
                .ToListAsync();

            // Get leads summary for this partner
            var startDate = new DateTime(year.Value, DateTime.ParseExact(fullMonth, "MMMM", null).Month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);
            
            var partnerLeads = await _context.PartnerLeads
                .Where(pl => pl.PartnerId == id)
                .ToListAsync();

            // Update payout with actual commission data if it's missing
            if (payout.TotalCommission == 0 && commissionLogs.Any())
            {
                payout.TotalCommission = commissionLogs.Sum(c => c.FixedCommissionAmount);
                payout.TotalSales = commissionLogs.Count;
                payout.Amount = payout.TotalCommission;
                
                // Save the updated payout
                _context.PartnerPayouts.Update(payout);
                await _context.SaveChangesAsync();
            }

            ViewBag.CommissionLogs = commissionLogs;
            ViewBag.PartnerLeads = new List<PartnerLeadModel>();
            ViewBag.Month = fullMonth;
            ViewBag.Year = year;

            return View(payout);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStatus(int payoutId, string status)
        {
            try
            {
                var payout = await _context.PartnerPayouts.FindAsync(payoutId);
                if (payout == null)
                    return Json(new { success = false, message = "Payout not found" });

                payout.Status = status;
                if (status == "Paid")
                    payout.ProcessedOn = IndianTime.Now;

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Status updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // PAYSLIP GENERATION FOR CHANNEL PARTNERS
        public async Task<IActionResult> Payslip(int partnerId, string month = null, int? year = null)
        {
            month ??= IndianTime.Now.ToString("MMMM");
            year ??= IndianTime.Now.Year;

            var partner = await _context.ChannelPartners.FindAsync(partnerId);
            if (partner == null)
                return NotFound();

            var payslip = await _payslipService.GeneratePartnerPayslip(partner, month, year.Value);
            return View(payslip);
        }

        [HttpPost]
        public async Task<IActionResult> ProcessPayouts(string month, int year)
        {
            try
            {
                await _payoutService.ProcessMonthlyPayouts(month, year);
                return Json(new { success = true, message = "Partner payouts processed successfully" });
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
                var existingPayouts = await _context.PartnerPayouts
                    .Where(p => p.Month == month && p.Year == year)
                    .ToListAsync();
                _context.PartnerPayouts.RemoveRange(existingPayouts);
                
                // Delete existing commission logs for the month
                var existingLogs = await _context.ChannelPartnerCommissionLogs
                    .Where(c => c.Month == month && c.Year == year)
                    .ToListAsync();
                _context.ChannelPartnerCommissionLogs.RemoveRange(existingLogs);
                
                await _context.SaveChangesAsync();
                
                // Recalculate from bookings
                await RecalculatePartnerCommissions(month, year);
                
                return Json(new { success = true, message = "Partner payouts recalculated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private async Task RecalculatePartnerCommissions(string month, int year)
        {
            var startDate = new DateTime(year, DateTime.ParseExact(month, "MMMM", null).Month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            // Get all confirmed bookings for the month that came from partner leads
            var partnerLeadIds = await _context.PartnerLeads.Select(pl => pl.LeadId).ToListAsync();
            
            var bookings = await _context.Bookings
                .Where(b => partnerLeadIds.Contains(b.LeadId) &&
                           b.BookingDate >= startDate && 
                           b.BookingDate <= endDate && 
                           b.Status == "Confirmed")
                .ToListAsync();

            foreach (var booking in bookings)
            {
                // Find which partner this lead belongs to
                var partnerLead = await _context.PartnerLeads
                    .FirstOrDefaultAsync(pl => pl.LeadId == booking.LeadId);
                
                if (partnerLead != null)
                {
                    var partner = await _context.ChannelPartners.FindAsync(partnerLead.PartnerId);
                    if (partner != null && partner.Status == "Approved")
                    {
                        // Calculate percentage-based commission
                        var commissionPercentage = GetPartnerCommissionPercentage(partner.CommissionScheme);
                        var commissionAmount = (booking.TotalAmount * commissionPercentage) / 100;

                        var commissionLog = new ChannelPartnerCommissionLogModel
                        {
                            PartnerId = partner.PartnerId,
                            BookingId = booking.BookingId,
                            FixedCommissionAmount = commissionAmount,
                            SaleDate = booking.BookingDate,
                            Month = month,
                            Year = year
                        };
                        _context.ChannelPartnerCommissionLogs.Add(commissionLog);
                    }
                }
            }

            await _context.SaveChangesAsync();
            
            // Now process payouts
            await _payoutService.ProcessMonthlyPayouts(month, year);
        }

        private decimal GetPartnerCommissionPercentage(string commissionScheme)
        {
            if (string.IsNullOrEmpty(commissionScheme)) return 0;
            var commissionText = commissionScheme.Replace("%", "").Trim();
            return decimal.TryParse(commissionText, out decimal percentage) ? percentage : 0;
        }
    }
}