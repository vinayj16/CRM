using CRM.Helpers;
using CRM.Models;
using CRM.ViewModels;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using Microsoft.AspNetCore.Authorization;

namespace CRM.Controllers
{
    public class ProfitController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ILogger<ProfitController> _logger;
        public ProfitController(AppDbContext db, ILogger<ProfitController> logger) { _db = db; _logger = logger; }
        [Route("profits")]
        public IActionResult Index()
        {
            var role = User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid, out int userId);
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;

            // Get Expenses
            var expensesQuery = _db.Expenses.AsQueryable();
            if (role?.ToLower() == "partner")
                expensesQuery = expensesQuery.Where(e => e.ChannelPartnerId == channelPartnerId);
            else if (role?.ToLower() == "admin")
                expensesQuery = expensesQuery.Where(e => e.ChannelPartnerId == null);
            else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
                expensesQuery = channelPartnerId.HasValue
                    ? expensesQuery.Where(e => e.ChannelPartnerId == channelPartnerId)
                    : expensesQuery.Where(e => false);
            
            var expenses = expensesQuery.ToList();
            var totalExpenses = expenses.Sum(e => e.Amount);

            // Get Revenues (matching RevenueController logic)
            var revenuesQuery = _db.Revenues.AsQueryable();
            if (role?.ToLower() == "partner")
                revenuesQuery = revenuesQuery.Where(r => r.ChannelPartnerId == channelPartnerId);
            else if (role?.ToLower() == "admin")
                revenuesQuery = revenuesQuery.Where(r => r.ChannelPartnerId == null);
            else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
                revenuesQuery = channelPartnerId.HasValue
                    ? revenuesQuery.Where(r => r.ChannelPartnerId == channelPartnerId)
                    : revenuesQuery.Where(r => false);
            
            var revenues = revenuesQuery
                .Where(r => !(r.Type == "Booking" &&
                              (r.Description ?? "").Contains("Total Booked Amount") &&
                              (r.Description ?? "").Contains("from Bookings")))
                .ToList();
            
            // Get Bookings scope for payment filtering
            var bookingsQuery = _db.Bookings.AsQueryable();
            if (role?.ToLower() == "partner")
                bookingsQuery = bookingsQuery.Where(b => b.ChannelPartnerId == channelPartnerId);
            else if (role?.ToLower() == "admin")
                bookingsQuery = bookingsQuery.Where(b => b.ChannelPartnerId == null);
            else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
            {
                var myLeadIds = _db.Leads.Where(l => l.ExecutiveId == userId).Select(l => l.LeadId).ToList();
                bookingsQuery = bookingsQuery.Where(b => myLeadIds.Contains(b.LeadId));
            }
            
            // Add realized collections only (from received payments)
            var paymentsQuery = _db.Payments.AsQueryable();
            if (role?.ToLower() == "partner")
            {
                var partnerBookingIds = bookingsQuery.Select(b => b.BookingId).ToList();
                paymentsQuery = paymentsQuery.Where(p => partnerBookingIds.Contains(p.BookingId));
            }
            else if (role?.ToLower() == "admin")
            {
                var adminBookingIds = _db.Bookings.Where(b => b.ChannelPartnerId == null).Select(b => b.BookingId).ToList();
                paymentsQuery = paymentsQuery.Where(p => adminBookingIds.Contains(p.BookingId));
            }
            else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
            {
                var myLeadIds = _db.Leads.Where(l => l.ExecutiveId == userId).Select(l => l.LeadId).ToList();
                var myBookingIds = _db.Bookings.Where(b => myLeadIds.Contains(b.LeadId)).Select(b => b.BookingId).ToList();
                paymentsQuery = paymentsQuery.Where(p => myBookingIds.Contains(p.BookingId));
            }
            var payments = paymentsQuery.ToList();
            var totalPayments = payments.Sum(p => p.Amount);
            if (totalPayments > 0)
            {
                revenues.Add(new RevenueModel {
                    Type = "Collection",
                    Description = "Total Realized Collections (from Payments)",
                    Amount = totalPayments,
                    Date = IndianTime.Now
                });
            }

            // Include partner commission earnings in realized revenue for partner-side users.
            var isPartnerTeam = channelPartnerId.HasValue &&
                (role?.ToLower() == "partner" || role?.ToLower() == "sales" || role?.ToLower() == "agent");

            if (isPartnerTeam)
            {
                var totalPartnerCommission = _db.ChannelPartnerCommissionLogs
                    .Where(c => c.PartnerId == channelPartnerId.Value)
                    .Sum(c => (decimal?)c.FixedCommissionAmount) ?? 0m;

                if (totalPartnerCommission > 0)
                {
                    revenues.Add(new RevenueModel
                    {
                        Type = "Partner Commission",
                        Description = "Total Commission Earned (from Partner Sales)",
                        Amount = totalPartnerCommission,
                        Date = IndianTime.Now
                    });
                }
            }

            var totalRevenue = revenues.Sum(r => r.Amount);
            var profit = totalRevenue - totalExpenses;
            
            var vm = new ExpenseRevenueProfitViewModel
            {
                Expenses = expenses,
                Revenues = revenues,
                TotalExpenses = totalExpenses,
                TotalRevenue = totalRevenue,
                Profit = profit
            };
            return View(vm);
        }
    }
}
