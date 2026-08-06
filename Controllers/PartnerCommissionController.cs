using CRM.Attributes;
using CRM.Models;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace CRM.Controllers
{
    [RoleAuthorize("Admin")]
    public class PartnerCommissionController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<PartnerCommissionController> _logger;

        public PartnerCommissionController(AppDbContext context, ILogger<PartnerCommissionController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: PartnerCommission/Index - Partner Payout Page
        [Route("partnerpayouts")]
        public IActionResult Index(string month = null, int? year = null, string status = null)
        {
            // Get all partner payouts with booking amounts
            var payoutsQuery = _context.PartnerPayouts
                
                .AsQueryable();

            // Apply filters
            if (!string.IsNullOrEmpty(month))
                payoutsQuery = payoutsQuery.Where(p => p.Month == month);
            
            if (year.HasValue)
                payoutsQuery = payoutsQuery.Where(p => p.Year == year.Value);
            
            if (!string.IsNullOrEmpty(status))
                payoutsQuery = payoutsQuery.Where(p => p.Status == status);

            var payouts = payoutsQuery
                .OrderByDescending(p => p.Year)
                .ThenByDescending(p => p.Month)
                .Select(p => new
                {
                    p.PayoutId,
                    p.PartnerId,
                    p.Month,
                    p.Year,
                    TotalSales = _context.ChannelPartnerCommissionLogs
                        .Where(c => c.PartnerId == p.PartnerId && c.Month == p.Month && c.Year == p.Year).Count(),
                    FixedCommissionPerSale = p.FixedCommissionPerSale > 0
                        ? p.FixedCommissionPerSale
                        : (p.Partner != null ? p.Partner.CommissionPercentage : 0m),
                    TotalCommission = _context.ChannelPartnerCommissionLogs
                        .Where(c => c.PartnerId == p.PartnerId && c.Month == p.Month && c.Year == p.Year)
                        .Select(c => (decimal?)c.FixedCommissionAmount)
                        .Sum() ?? 0m,
                    p.Status,
                    Partner = p.Partner,
                    TotalBookingAmount = _context.ChannelPartnerCommissionLogs
                        .Where(c => c.PartnerId == p.PartnerId && c.Month == p.Month && c.Year == p.Year)
                        .ToList()
                        .Join(_context.Bookings.ToList(), c => c.BookingId, b => b.BookingId, (c, b) => (decimal?)b.TotalAmount)
                        .Sum() ?? 0m
                })
                .ToList();

            // Calculate summary stats
            ViewBag.TotalPayouts = payouts.Sum(p => p.TotalCommission);
            ViewBag.TotalSales = payouts.Sum(p => p.TotalSales);
            ViewBag.TotalCommission = payouts.Sum(p => p.TotalCommission);
            ViewBag.TotalPartners = payouts.Select(p => p.PartnerId).Distinct().Count();
            
            // Pass current filter values to view
            ViewBag.SelectedMonth = month;
            ViewBag.SelectedYear = year;
            ViewBag.SelectedStatus = status;

            return View(payouts);
        }

        public string GetBookingAmountCalculation(int partnerId, string month, int year)
        {
            var commissionLogs = _context.ChannelPartnerCommissionLogs
                .Where(c => c.PartnerId == partnerId && c.Month == month && c.Year == year)
                .ToList();

            if (!commissionLogs.Any()) return "No bookings";

            var totalBookingAmount = commissionLogs
                .Join(_context.Bookings.ToList(), c => c.BookingId, b => b.BookingId, (c, b) => b.TotalAmount)
                .Sum();

            var partner = _context.ChannelPartners.Find(partnerId);
            var commissionRate = partner?.CommissionPercentage ?? 0;

            return $"(₹{totalBookingAmount:N0} × {commissionRate}%)";
        }

        // POST: Process Payments - Calculate partner commissions
        [HttpPost]
        public async Task<IActionResult> ProcessPayments()
        {
            try
            {
                // Debug: Get all bookings first
                var allBookings = await _context.Bookings.ToListAsync();
                var confirmedBookings = allBookings.Where(b => b.Status == "Confirmed").ToList();
                
                // Check multiple ways partner bookings might be linked
                var partnerBookingsViaLead = confirmedBookings.Where(b => b.Lead?.ChannelPartnerId.HasValue == true).ToList();
                var partnerBookingsViaBooking = confirmedBookings.Where(b => b.ChannelPartnerId.HasValue).ToList();
                var partnerBookingsViaSource = confirmedBookings.Where(b => b.Lead?.Source == "Partner").ToList();
                
                // Get all confirmed bookings from partner leads that don't have commission logs yet
                // Try multiple approaches to find partner bookings
                var partnerBookings = await _context.Bookings
                    
                    .Where(b => b.Status == "Confirmed" && 
                               (b.Lead.ChannelPartnerId.HasValue || 
                                b.ChannelPartnerId.HasValue || 
                                b.Lead.Source == "Partner") &&
                               !_context.ChannelPartnerCommissionLogs.Any(log => log.BookingId == b.BookingId))
                    .ToListAsync();

                int processedCount = 0;
                decimal totalCommission = 0;
                var debugInfo = new List<string>();
                
                debugInfo.Add($"Total bookings: {allBookings.Count}");
                debugInfo.Add($"Confirmed bookings: {confirmedBookings.Count}");
                debugInfo.Add($"Partner bookings via Lead.ChannelPartnerId: {partnerBookingsViaLead.Count}");
                debugInfo.Add($"Partner bookings via Booking.ChannelPartnerId: {partnerBookingsViaBooking.Count}");
                debugInfo.Add($"Partner bookings via Source=Partner: {partnerBookingsViaSource.Count}");
                debugInfo.Add($"Partner bookings (unprocessed): {partnerBookings.Count}");

                foreach (var booking in partnerBookings)
                {
                    // Try to find partner via multiple methods
                    ChannelPartnerModel partner = null;
                    
                    if (booking.Lead?.ChannelPartnerId.HasValue == true)
                    {
                        partner = await _context.ChannelPartners
                            .FirstOrDefaultAsync(cp => cp.PartnerId == booking.Lead.ChannelPartnerId.Value);
                    }
                    else if (booking.ChannelPartnerId.HasValue)
                    {
                        partner = await _context.ChannelPartners
                            .FirstOrDefaultAsync(cp => cp.PartnerId == booking.ChannelPartnerId.Value);
                    }
                    else if (booking.Lead?.Source == "Partner")
                    {
                        // Find partner by company name or other identifier
                        partner = await _context.ChannelPartners
                            .FirstOrDefaultAsync(cp => cp.Status == "Approved");
                    }

                    debugInfo.Add($"Booking {booking.BookingNumber}: Lead.ChannelPartnerId={booking.Lead?.ChannelPartnerId}, Booking.ChannelPartnerId={booking.ChannelPartnerId}, Lead.Source={booking.Lead?.Source}, Partner found = {partner != null}, Status = {partner?.Status}");
                    
                    if (partner == null || partner.Status != "Approved") continue;

                    // Use booking date for month/year calculation
                    var bookingMonth = booking.BookingDate.ToString("MMM");
                    var bookingYear = booking.BookingDate.Year;

                    // Calculate commission
                    var commissionPercentage = partner.CommissionPercentage;
                    var commissionAmount = (booking.TotalAmount * commissionPercentage) / 100;
                    
                    debugInfo.Add($"Commission calc: {booking.TotalAmount} * {commissionPercentage}% = {commissionAmount}");

                    // Create commission log
                    var commissionLog = new ChannelPartnerCommissionLogModel
                    {
                        PartnerId = partner.PartnerId,
                        BookingId = booking.BookingId,
                        FixedCommissionAmount = commissionAmount,
                        SaleDate = booking.BookingDate,
                        Month = bookingMonth,
                        Year = bookingYear
                    };
                    _context.ChannelPartnerCommissionLogs.Add(commissionLog);
                    
                    debugInfo.Add($"Commission log created for Partner {partner.PartnerId}");

                    processedCount++;
                    totalCommission += commissionAmount;
                }

                await _context.SaveChangesAsync();

                // Update payouts after commission logs are saved
                if (processedCount > 0)
                {
                    var processedPartners = partnerBookings
                        .Where(b => b.Lead?.ChannelPartnerId.HasValue == true || b.ChannelPartnerId.HasValue)
                        .Select(b => new { 
                            PartnerId = b.Lead?.ChannelPartnerId ?? b.ChannelPartnerId.Value,
                            Month = b.BookingDate.ToString("MMM"),
                            Year = b.BookingDate.Year
                        })
                        .Distinct()
                        .ToList();

                    foreach (var partnerMonth in processedPartners)
                    {
                        await UpdatePartnerPayout(partnerMonth.PartnerId, partnerMonth.Month, partnerMonth.Year);
                    }

                    await _context.SaveChangesAsync();
                }

                var message = processedCount > 0 
                    ? $"Payment calculation completed! Processed {processedCount} bookings with total commission of ₹{totalCommission:N2}. Please review the calculations and approve for payment."
                    : $"No new bookings to process";
                    
                return Json(new { 
                    success = true, 
                    message = message,
                    processedCount = processedCount,
                    totalCommission = totalCommission,
                    debug = debugInfo
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // GET: Fix existing payout data (for browser access)
        [HttpGet]
        public async Task<IActionResult> FixPayoutData()
        {
            return await FixPayoutDataInternal();
        }

        // POST: Fix existing payout data
        [HttpPost]
        public async Task<IActionResult> FixPayoutDataPost()
        {
            return await FixPayoutDataInternal();
        }

        private async Task<IActionResult> FixPayoutDataInternal()
        {
            try
            {
                // Get all commission logs and recalculate payouts
                var commissionLogs = await _context.ChannelPartnerCommissionLogs.ToListAsync();
                var payouts = await _context.PartnerPayouts.ToListAsync();

                foreach (var payout in payouts)
                {
                    var logs = commissionLogs.Where(c => c.PartnerId == payout.PartnerId && 
                                                        c.Month == payout.Month && 
                                                        c.Year == payout.Year).ToList();
                    
                    payout.TotalSales = logs.Count;
                    payout.TotalCommission = logs.Sum(c => c.FixedCommissionAmount);
                    payout.ConvertedLeads = logs.Count;
                }

                await _context.SaveChangesAsync();
                
                return Json(new { success = true, message = "Payout data fixed successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        private async Task UpdatePartnerPayout(int partnerId, string month, int year)
        {
            var existingPayout = await _context.PartnerPayouts
                .FirstOrDefaultAsync(p => p.PartnerId == partnerId && p.Month == month && p.Year == year);

            var commissionLogs = await _context.ChannelPartnerCommissionLogs
                .Where(c => c.PartnerId == partnerId && c.Month == month && c.Year == year)
                .ToListAsync();

            var totalCommission = commissionLogs.Sum(c => c.FixedCommissionAmount);
            var totalSales = commissionLogs.Count;

            if (existingPayout != null)
            {
                // Only update amounts, keep existing status unless it's empty
                var partner = await _context.ChannelPartners.FindAsync(partnerId);
                existingPayout.TotalCommission = totalCommission;
                existingPayout.TotalSales = totalSales;
                existingPayout.ConvertedLeads = totalSales;
                if (existingPayout.FixedCommissionPerSale <= 0)
                {
                    existingPayout.FixedCommissionPerSale = partner?.CommissionPercentage ?? 0m;
                }
                if (string.IsNullOrEmpty(existingPayout.Status))
                {
                    existingPayout.Status = "Pending";
                }
                _context.PartnerPayouts.Update(existingPayout);
            }
            else
            {
                var partner = await _context.ChannelPartners.FindAsync(partnerId);
                var commissionPercentage = partner?.CommissionPercentage ?? 0m;
                
                var payout = new PartnerPayoutModel
                {
                    PartnerId = partnerId,
                    Month = month,
                    Year = year,
                    FixedCommissionPerSale = commissionPercentage,
                    TotalSales = totalSales,
                    TotalCommission = totalCommission,
                    ConvertedLeads = totalSales,
                    Status = "Pending"  // Always start with Pending status
                };
                _context.PartnerPayouts.Add(payout);
            }
        }

        // POST: Approve payout
        [HttpPost]
        public async Task<IActionResult> ApprovePayout([FromBody] JsonElement request)
        {
            try
            {
                int payoutId = request.GetProperty("payoutId").GetInt32();
                var payout = await _context.PartnerPayouts.FindAsync(payoutId);
                if (payout == null)
                {
                    return Json(new { success = false, message = "Payout not found" });
                }

                payout.Status = "Approved";
                _context.PartnerPayouts.Update(payout);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Payout approved successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // GET: Debug booking and partner data
        [HttpGet]
        public async Task<IActionResult> DebugCommissionData()
        {
            try
            {
                var allBookings = await _context.Bookings.ToListAsync();
                var confirmedBookings = allBookings.Where(b => b.Status == "Confirmed").ToList();
                var partners = await _context.ChannelPartners.ToListAsync();
                var commissionLogs = await _context.ChannelPartnerCommissionLogs.ToListAsync();
                var payouts = await _context.PartnerPayouts.ToListAsync();

                var debugData = new
                {
                    TotalBookings = allBookings.Count,
                    ConfirmedBookings = confirmedBookings.Count,
                    Partners = partners.Select(p => new { p.PartnerId, p.CompanyName, p.Status, p.CommissionPercentage }),
                    ExistingCommissionLogs = commissionLogs.Select(c => new { c.PartnerId, c.BookingId, c.FixedCommissionAmount, c.Month, c.Year }),
                    ExistingPayouts = payouts.Select(p => new { p.PartnerId, p.Month, p.Year, p.TotalSales, p.TotalCommission, p.Status }),
                    CommissionLogs = commissionLogs.Count,
                    Payouts = payouts.Count,
                    BookingDetails = confirmedBookings.Select(b => new {
                        b.BookingId,
                        b.BookingNumber,
                        b.Status,
                        b.TotalAmount,
                        b.BookingDate,
                        LeadChannelPartnerId = b.Lead?.ChannelPartnerId,
                        BookingChannelPartnerId = b.ChannelPartnerId,
                        LeadSource = b.Lead?.Source
                    })
                };

                return Json(debugData);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        // POST: Reprocess all payments (clears existing and recalculates)
        [HttpPost]
        public async Task<IActionResult> ReprocessPayments()
        {
            try
            {
                // Clear existing commission logs and payouts
                var existingLogs = await _context.ChannelPartnerCommissionLogs.ToListAsync();
                var existingPayouts = await _context.PartnerPayouts.ToListAsync();
                
                _context.ChannelPartnerCommissionLogs.RemoveRange(existingLogs);
                _context.PartnerPayouts.RemoveRange(existingPayouts);
                await _context.SaveChangesAsync();

                // Now run normal ProcessPayments logic
                return await ProcessPayments();
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // POST: Mark payout as paid
        [HttpPost]
        public async Task<IActionResult> MarkAsPaid([FromBody] JsonElement request)
        {
            try
            {
                int payoutId = request.GetProperty("payoutId").GetInt32();
                var payout = await _context.PartnerPayouts.FindAsync(payoutId);
                if (payout == null)
                {
                    return Json(new { success = false, message = "Payout not found" });
                }

                payout.Status = "Paid";
                _context.PartnerPayouts.Update(payout);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Payout marked as paid" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // POST: Reset payout statuses to Pending (for fixing existing data)
        [HttpPost]
        public async Task<IActionResult> ResetPayoutStatuses()
        {
            try
            {
                var payouts = await _context.PartnerPayouts
                    .Where(p => p.Status != "Paid")
                    .ToListAsync();
                
                foreach (var payout in payouts)
                {
                    payout.Status = "Pending";
                }
                
                await _context.SaveChangesAsync();
                
                return Json(new { 
                    success = true, 
                    message = $"Reset {payouts.Count} payouts to Pending status" 
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // GET: Show calculation details for a specific payout
        [HttpGet]
        public async Task<IActionResult> GetCalculationDetails(int payoutId)
        {
            try
            {
                if (payoutId <= 0)
                {
                    return Json(new { success = false, message = "Invalid payout ID" });
                }

                var payout = await _context.PartnerPayouts.FindAsync(payoutId);
                if (payout == null)
                {
                    return Json(new { success = false, message = $"Payout with ID {payoutId} not found" });
                }

                var partner = await _context.ChannelPartners.FindAsync(payout.PartnerId);
                
                // Get all commission logs for debugging
                var allLogsForPartner = await _context.ChannelPartnerCommissionLogs
                    .Where(c => c.PartnerId == payout.PartnerId)
                    .ToListAsync();
                
                var commissionLogs = await _context.ChannelPartnerCommissionLogs
                    .Where(c => c.PartnerId == payout.PartnerId && c.Month == payout.Month && c.Year == payout.Year)
                    .ToListAsync();

                var bookingDetails = new List<object>();
                if (commissionLogs.Any())
                {
                    foreach (var log in commissionLogs)
                    {
                        var booking = await _context.Bookings.FindAsync(log.BookingId);
                        if (booking != null)
                        {
                            bookingDetails.Add(new {
                                BookingNumber = booking.BookingNumber ?? "N/A",
                                BookingAmount = booking.TotalAmount,
                                CommissionAmount = log.FixedCommissionAmount,
                                BookingDate = booking.BookingDate.ToString("dd MMM yyyy")
                            });
                        }
                    }
                }
                else
                {
                    // No commission logs found - check if there are any bookings for this partner
                    var partnerBookings = await _context.Bookings
                        
                        .Where(b => b.Status == "Confirmed" && 
                                   (b.Lead.ChannelPartnerId == payout.PartnerId || b.ChannelPartnerId == payout.PartnerId))
                        .ToListAsync();
                    
                    if (partnerBookings.Any())
                    {
                        return Json(new { 
                            success = false, 
                            message = $"No commission logs found for this payout, but {partnerBookings.Count} confirmed bookings exist for this partner. Please run 'Process Payments' first.",
                            debugInfo = new {
                                AllLogsForPartnerCount = allLogsForPartner.Count,
                                PayoutPartnerId = payout.PartnerId,
                                PayoutMonth = payout.Month,
                                PayoutYear = payout.Year,
                                AllLogsForPartner = allLogsForPartner.Select(l => new {
                                    l.PartnerId,
                                    l.Month,
                                    l.Year,
                                    l.FixedCommissionAmount
                                }).ToList()
                            }
                        });
                    }
                }

                return Json(new {
                    success = true,
                    payout = new {
                        PayoutId = payout.PayoutId,
                        PartnerName = partner?.CompanyName ?? "Unknown Partner",
                        Month = payout.Month ?? "Unknown",
                        Year = payout.Year,
                        TotalSales = commissionLogs.Count,
                        TotalCommission = commissionLogs.Sum(x => x.FixedCommissionAmount),
                        FixedCommissionPerSale = payout.FixedCommissionPerSale > 0
                            ? payout.FixedCommissionPerSale
                            : (partner?.CommissionPercentage ?? 0m),
                        Status = payout.Status ?? "Unknown"
                    },
                    bookingDetails = bookingDetails,
                    debugInfo = new {
                        CommissionLogsCount = commissionLogs.Count,
                        AllLogsForPartnerCount = allLogsForPartner.Count,
                        PayoutPartnerId = payout.PartnerId,
                        PayoutMonth = payout.Month,
                        PayoutYear = payout.Year,
                        AllLogsForPartner = allLogsForPartner.Select(l => new {
                            l.PartnerId,
                            l.Month,
                            l.Year,
                            l.FixedCommissionAmount,
                            l.BookingId
                        }).ToList()
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error loading calculation details: {ex.Message}" });
            }
        }
        // POST: Update Existing Payout
[HttpPost]
        public async Task<IActionResult> CreateTestCommission()
        {
            try
            {
                // Clear existing data and reprocess
                var existingLogs = await _context.ChannelPartnerCommissionLogs.ToListAsync();
                var existingPayouts = await _context.PartnerPayouts.ToListAsync();
                
                _context.ChannelPartnerCommissionLogs.RemoveRange(existingLogs);
                _context.PartnerPayouts.RemoveRange(existingPayouts);
                await _context.SaveChangesAsync();

                // Get all confirmed partner bookings
                var partnerBookings = await _context.Bookings
                    
                    .Where(b => b.Status == "Confirmed" && 
                               (b.Lead.ChannelPartnerId.HasValue || b.Lead.Source == "Partner"))
                    .ToListAsync();

                int processedCount = 0;
                decimal totalCommission = 0;

                foreach (var booking in partnerBookings)
                {
                    var partner = await _context.ChannelPartners
                        .FirstOrDefaultAsync(cp => cp.PartnerId == booking.Lead.ChannelPartnerId.Value && cp.Status == "Approved");

                    if (partner == null) continue;

                    var commissionAmount = (booking.TotalAmount * partner.CommissionPercentage) / 100;
                    var bookingMonth = booking.BookingDate.ToString("MMMM");
                    var bookingYear = booking.BookingDate.Year;

                    var commissionLog = new ChannelPartnerCommissionLogModel
                    {
                        PartnerId = partner.PartnerId,
                        BookingId = booking.BookingId,
                        FixedCommissionAmount = commissionAmount,
                        SaleDate = booking.BookingDate,
                        Month = bookingMonth,
                        Year = bookingYear
                    };
                    _context.ChannelPartnerCommissionLogs.Add(commissionLog);

                    processedCount++;
                    totalCommission += commissionAmount;
                }

                await _context.SaveChangesAsync();

                // Create payouts
                var partners = await _context.ChannelPartners.Where(p => p.Status == "Approved").ToListAsync();
                foreach (var partner in partners)
                {
                    var logs = await _context.ChannelPartnerCommissionLogs
                        .Where(c => c.PartnerId == partner.PartnerId)
                        .GroupBy(c => new { c.Month, c.Year })
                        .ToListAsync();

                    foreach (var monthGroup in logs)
                    {
                        var payout = new PartnerPayoutModel
                        {
                            PartnerId = partner.PartnerId,
                            Month = monthGroup.Key.Month,
                            Year = monthGroup.Key.Year,
                            FixedCommissionPerSale = partner.CommissionPercentage,
                            TotalSales = monthGroup.Count(),
                            TotalCommission = monthGroup.Sum(c => c.FixedCommissionAmount),
                            ConvertedLeads = monthGroup.Count(),
                            Status = "Pending"
                        };
                        _context.PartnerPayouts.Add(payout);
                    }
                }

                await _context.SaveChangesAsync();

                return Json(new { 
                    success = true, 
                    message = $"Reprocessed {processedCount} bookings. Total: ₹{totalCommission:N2}"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }
    }
}