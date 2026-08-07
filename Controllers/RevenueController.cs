using CRM.Attributes;
using CRM.Helpers;
using CRM.Models;
using Microsoft.AspNetCore.Mvc;
using System.Linq;

namespace CRM.Controllers
{
    public class RevenueController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ILogger<RevenueController> _logger;
        public RevenueController(AppDbContext db, ILogger<RevenueController> logger) { _db = db; _logger = logger; }
        [PermissionAuthorize("View")]
        public IActionResult Index()
        {
            var role = User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid, out int userId);
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;

            var revenuesQuery = _db.Revenues.AsQueryable();
            if (role?.ToLower() == "partner")
            {
                revenuesQuery = revenuesQuery.Where(r => r.ChannelPartnerId == channelPartnerId);
            }
            else if (role?.ToLower() == "admin")
            {
                revenuesQuery = revenuesQuery.Where(r => r.ChannelPartnerId == null);
            }

            var revenues = revenuesQuery.ToList();

            var bookingsQuery = _db.Bookings.AsQueryable();
            if (role?.ToLower() == "partner")
            {
                bookingsQuery = bookingsQuery.Where(b => b.ChannelPartnerId == channelPartnerId);
            }
            else if (role?.ToLower() == "admin")
            {
                bookingsQuery = bookingsQuery.Where(b => b.ChannelPartnerId == null);
            }
            else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
            {
                var myLeadIds = _db.Leads.Where(l => l.ExecutiveId == userId).Select(l => l.LeadId).ToList();
                bookingsQuery = bookingsQuery.Where(b => myLeadIds.Contains(b.LeadId));
            }

            var bookings = bookingsQuery.ToList();

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
                revenues.Add(new RevenueModel
                {
                    Type = "Collection",
                    Description = "Total Realized Collections (from Payments)",
                    Amount = totalPayments,
                    Date = IndianTime.Now
                });
            }

            // Include Razorpay subscription payments for admin
            if (role?.ToLower() == "admin")
            {
                var razorpayPayments = _db.PaymentTransactions
                    
                    .Where(t => t.Status == "Success" && t.TransactionType != "Refund" && t.TransactionType != "Cancellation")
                    .ToList();

                foreach (var txn in razorpayPayments)
                {
                    revenues.Add(new RevenueModel
                    {
                        Type = "Subscription",
                        Description = $"Razorpay - {txn.PlanName ?? "Subscription"} ({txn.BillingCycle ?? ""}) - {txn.ChannelPartner?.CompanyName ?? "Partner"}",
                        Amount = txn.Amount,
                        Date = txn.TransactionDate
                    });
                }
            }

            // Partner-side users should also see commission earned from partner bookings.
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

            return View(revenues);
        }

        // GET: Revenue/Details/{id}
        [Route("revenuedetails/{id}")]
        public IActionResult Details(string id)
        {
            var decodedId = IdObfuscator.Decode(id);
            if (decodedId == null)
            {
                return NotFound();
            }
            ViewBag.EncodedId = id;
            var revenue = _db.Revenues.FirstOrDefault(r => r.RevenueId == decodedId.Value);
            if (revenue == null)
            {
                return NotFound();
            }
            return View(revenue);
        }

        // GET: Revenue/Delete/{id}
        public IActionResult Delete(int id)
        {
            var revenue = _db.Revenues.FirstOrDefault(r => r.RevenueId == id);
            if (revenue == null)
            {
                return NotFound();
            }
            return View(revenue);
        }

        // POST: Revenue/Delete/{id}
        [HttpPost, ActionName("Delete")]
        public IActionResult DeleteConfirmed(int id)
        {
            var revenue = _db.Revenues.FirstOrDefault(r => r.RevenueId == id);
            if (revenue == null)
            {
                return NotFound();
            }
            _db.Revenues.Remove(revenue);
            _db.SaveChanges();
            return RedirectToAction("Index");
        }

        // POST: Revenue/DeleteRevenue (AJAX)
        [HttpPost]
        public JsonResult DeleteRevenue([FromForm]int revenueId)
        {
            var revenue = _db.Revenues.FirstOrDefault(r => r.RevenueId == revenueId);
            if (revenue == null)
            {
                return Json(new { success = false, message = "Revenue not found." });
            }
            _db.Revenues.Remove(revenue);
            _db.SaveChanges();
            return Json(new { success = true });
        }

        // GET: Revenue/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Revenue/Create
        [HttpPost]
        public IActionResult Create(RevenueModel model)
        {
            if (ModelState.IsValid)
            {
                var role = User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
                var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(uid, out int userId);
                var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
                
                if (role?.ToLower() == "partner")
                    model.ChannelPartnerId = currentUser?.ChannelPartnerId;
                
                _db.Revenues.Add(model);
                _db.SaveChanges();
                return RedirectToAction("Index");
            }
            return View(model);
        }

        // POST: Revenue/CreateModal (AJAX)
        [HttpPost]
        public JsonResult CreateModal([FromForm] RevenueModel model)
        {
            if (ModelState.IsValid)
            {
                var role = User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
                var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(uid, out int userId);
                var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
                
                if (role?.ToLower() == "partner")
                    model.ChannelPartnerId = currentUser?.ChannelPartnerId;
                
                _db.Revenues.Add(model);
                _db.SaveChanges();
                return Json(new { success = true });
            }
            return Json(new { success = false, message = "Invalid data" });
        }

        // GET: Revenue/GetRevenue/{id} (AJAX)
        [HttpGet]
        public JsonResult GetRevenue(int id)
        {
            var revenue = _db.Revenues.FirstOrDefault(r => r.RevenueId == id);
            if (revenue == null)
                return Json(new { success = false, message = "Revenue not found" });
            
            return Json(new { 
                success = true, 
                data = new {
                    revenueId = revenue.RevenueId,
                    type = revenue.Type,
                    description = revenue.Description,
                    amount = revenue.Amount
                }
            });
        }

        // POST: Revenue/EditModal (AJAX)
        [HttpPost]
        public JsonResult EditModal([FromForm] RevenueModel model)
        {
            if (ModelState.IsValid)
            {
                var revenue = _db.Revenues.FirstOrDefault(r => r.RevenueId == model.RevenueId);
                if (revenue == null)
                    return Json(new { success = false, message = "Revenue not found" });
                
                revenue.Type = model.Type;
                revenue.Description = model.Description;
                revenue.Amount = model.Amount;
                
                _db.Revenues.Update(revenue);
                _db.SaveChanges();
                return Json(new { success = true });
            }
            return Json(new { success = false, message = "Invalid data" });
        }
    }
}

