using CRM.Helpers;
        
using CRM.Models;
using Microsoft.AspNetCore.Mvc;
using CRM.Attributes;
using System.Security.Claims;

namespace CRM.Controllers
{
    public class InvoicesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<InvoicesController> _logger;

        public InvoicesController(AppDbContext db, IHttpContextAccessor httpContextAccessor, ILogger<InvoicesController> logger)
        {
            _db = db;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        // GET: Invoices/Index
        [PermissionAuthorize("View")]
        public IActionResult Index(string search = "", string status = "")
        {
            var role = User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid, out int userId);

            var invoicesQuery = _db.Invoices.AsQueryable();
            
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;

            if (role?.ToLower() == "partner")
            {
                // Partners see invoices for their leads only
                var partnerLeadIds = _db.Leads.Where(l => l.ChannelPartnerId == channelPartnerId).Select(l => l.LeadId).ToList();
                var partnerBookingIds = _db.Bookings.Where(b => partnerLeadIds.Contains(b.LeadId)).Select(b => b.BookingId).ToList();
                invoicesQuery = invoicesQuery.Where(i => partnerBookingIds.Contains(i.BookingId));
            }
            else if (role?.ToLower() == "admin")
            {
                // Admin sees their own invoices + partner invoices for handed over leads
                var adminLeadIds = _db.Leads.Where(l => l.ChannelPartnerId == null || l.HandoverStatus == "ReadyToBook" || l.HandoverStatus == "HandedOver").Select(l => l.LeadId).ToList();
                var adminBookingIds = _db.Bookings.Where(b => b.ChannelPartnerId == null || adminLeadIds.Contains(b.LeadId)).Select(b => b.BookingId).ToList();
                invoicesQuery = invoicesQuery.Where(i => adminBookingIds.Contains(i.BookingId));
            }
            else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
            {
                var myLeadIds = _db.Leads.Where(l => l.ExecutiveId == userId).Select(l => l.LeadId).ToList();
                var myBookingIds = _db.Bookings.Where(b => myLeadIds.Contains(b.LeadId)).Select(b => b.BookingId).ToList();
                invoicesQuery = invoicesQuery.Where(i => myBookingIds.Contains(i.BookingId));
            }

            // Apply search filter
            if (!string.IsNullOrEmpty(search))
            {
                invoicesQuery = invoicesQuery.Where(i =>
                    i.InvoiceNumber.Contains(search) ||
                    i.Notes.Contains(search));
            }

            // Apply status filter
            if (!string.IsNullOrEmpty(status))
            {
                invoicesQuery = invoicesQuery.Where(i => i.Status == status);
            }

            var invoices = invoicesQuery
                .OrderByDescending(i => i.InvoiceDate)
                .ToList();

            // Load related data (.Include is a no-op on MongoDbSet)
            var bookings = _db.Bookings.ToList();
            var allLeads = _db.Leads.ToList();
            var allProperties = _db.Properties.ToList();
            foreach (var b in bookings)
            {
                b.Lead = allLeads.FirstOrDefault(l => l.LeadId == b.LeadId);
                b.Property = allProperties.FirstOrDefault(p => p.PropertyId == b.PropertyId);
            }
            var installments = _db.PaymentInstallments.ToList();
            ViewBag.Bookings = bookings ?? new List<CRM.Models.BookingModel>();
            ViewBag.Installments = installments ?? new List<CRM.Models.PaymentInstallmentModel>();
            ViewBag.SearchTerm = search;
            ViewBag.StatusFilter = status;
            
            // Add user info for view-level access control
            ViewBag.IsPartnerTeam = currentUser?.ChannelPartnerId != null;

            return View(invoices);
        }

        // GET: Invoices/Create
        public IActionResult Create(int? bookingId, int? installmentId)
        {
            var role = User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid, out int userId);
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
            
            // Partners and their team members cannot create invoices
            if (role?.ToLower() == "partner" || currentUser?.ChannelPartnerId != null)
            {
                return RedirectToAction("Index");
            }
            
            var model = new InvoiceModel();

            if (bookingId.HasValue)
            {
                var booking = _db.Bookings.FirstOrDefault(b => b.BookingId == bookingId.Value);

                if (booking != null)
                {
                    // Populate nav properties manually (.Include is a no-op on MongoDbSet)
                    booking.Lead = _db.Leads.FirstOrDefault(l => l.LeadId == booking.LeadId);
                    booking.Property = _db.Properties.FirstOrDefault(p => p.PropertyId == booking.PropertyId);
                    booking.Flat = _db.PropertyFlats.FirstOrDefault(f => f.FlatId == booking.FlatId);
                    
                    model.BookingId = booking.BookingId;
                    ViewBag.Booking = booking;

                    // Get payment plan and installments
                    var paymentPlan = _db.PaymentPlans.FirstOrDefault(p => p.BookingId == bookingId.Value);
                    if (paymentPlan != null)
                    {
                        var installments = _db.PaymentInstallments
                            .Where(i => i.PlanId == paymentPlan.PlanId)
                            .OrderBy(i => i.InstallmentNumber)
                            .ToList();
                        ViewBag.Installments = installments;
                    }

                    // If installmentId is provided, auto-fill from installment
                    if (installmentId.HasValue)
                    {
                        var installment = _db.PaymentInstallments.Find(installmentId.Value);
                        if (installment != null)
                        {
                            model.InstallmentId = installment.InstallmentId;
                            model.Amount = installment.Amount;
                            model.DueDate = installment.DueDate;
                            ViewBag.SelectedInstallment = installment;
                        }
                    }
                }
            }
            else
            {
                // For full-payment bookings, exclude those that already have an invoice.
                // For milestone bookings, exclude only those where ALL milestones have invoices.
                var fullyInvoicedBookingIds = new HashSet<int>();

                // Get all full-payment booking IDs (MongoDB doesn't support .Include nav props)
                var fullPayBookingIds = _db.Bookings
                    .Where(b => b.PaymentType == "FullPayment" || b.PaymentType == "Full Payment")
                    .Select(b => b.BookingId)
                    .ToList();

                // Full-payment bookings with any invoice are fully invoiced
                var fullPayInvoiced = _db.Invoices
                    .Where(i => fullPayBookingIds.Contains(i.BookingId))
                    .Select(i => i.BookingId)
                    .Distinct()
                    .ToList();
                fullyInvoicedBookingIds.UnionWith(fullPayInvoiced);

                // Milestone bookings: exclude only if every milestone has an invoice
                var milestoneBookingIds = _db.Bookings
                    .Where(b => (b.Status == "Confirmed" || b.Status == "Completed")
                        && b.PaymentType != "FullPayment" && b.PaymentType != "Full Payment")
                    .Select(b => b.BookingId)
                    .ToList();

                foreach (var bId in milestoneBookingIds)
                {
                    var plan = _db.PaymentPlans.FirstOrDefault(p => p.BookingId == bId);
                    if (plan != null)
                    {
                        var totalMilestones = _db.PaymentInstallments.Where(i => i.PlanId == plan.PlanId).Count();
                        var invoicedMilestones = _db.Invoices.Where(i => i.BookingId == bId && i.InstallmentId != null).Count();
                        if (totalMilestones > 0 && invoicedMilestones >= totalMilestones)
                            fullyInvoicedBookingIds.Add(bId);
                    }
                }

                var allBookingsQuery = _db.Bookings
                    .Where(b => (b.Status == "Confirmed" || b.Status == "Completed")
                        && !fullyInvoicedBookingIds.Contains(b.BookingId));

                // Role-based filtering
                var channelPartnerId = currentUser?.ChannelPartnerId;
                
                if (role?.ToLower() == "partner")
                {
                    var partnerLeadIds = _db.Leads.Where(l => l.ChannelPartnerId == channelPartnerId).Select(l => l.LeadId).ToList();
                    allBookingsQuery = allBookingsQuery.Where(b => partnerLeadIds.Contains(b.LeadId));
                }
                else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
                {
                    var myLeadIds = _db.Leads.Where(l => l.ExecutiveId == userId).Select(l => l.LeadId).ToList();
                    allBookingsQuery = allBookingsQuery.Where(b => myLeadIds.Contains(b.LeadId));
                }

                var allBookings = allBookingsQuery.ToList();
                var allLeadsForBookings = _db.Leads.ToList();
                var allPropertiesForBookings = _db.Properties.ToList();
                foreach (var b in allBookings)
                {
                    b.Lead = allLeadsForBookings.FirstOrDefault(l => l.LeadId == b.LeadId);
                    b.Property = allPropertiesForBookings.FirstOrDefault(p => p.PropertyId == b.PropertyId);
                }

                ViewBag.AllBookings = allBookings;
            }

            // Get GST rate from settings
            var gstRate = SettingsController.GetSettingValueDecimal(_db, "GSTRate", 5);
            ViewBag.GSTRate = gstRate;

            // Generate next invoice number with prefix
            var prefix = SettingsController.GetSettingValue(_db, "InvoicePrefix", "INV");
            var year = IndianTime.Now.Year;
            var lastInvoice = _db.Invoices
                .Where(i => i.InvoiceNumber.StartsWith($"{prefix}-{year}"))
                .OrderByDescending(i => i.InvoiceId)
                .FirstOrDefault();

            int nextNumber = 1;
            if (lastInvoice != null)
            {
                var lastNumberStr = lastInvoice.InvoiceNumber.Split('-').Last();
                if (int.TryParse(lastNumberStr, out int lastNum))
                    nextNumber = lastNum + 1;
            }

            ViewBag.InvoicePrefix = $"{prefix}-{year}-";
            model.InvoiceNumber = $"{nextNumber:D4}";
            return View(model);
        }

        // GET: Invoices/GetBookingDetails (AJAX)
        [HttpGet]
        public IActionResult GetBookingDetails(int bookingId)
        {
            try
            {
                var booking = _db.Bookings.FirstOrDefault(b => b.BookingId == bookingId);
                
                // Look up related data manually (.Include is a no-op on MongoDbSet)
                var leadName = booking != null ? _db.Leads.Where(l => l.LeadId == booking.LeadId).Select(l => l.Name).FirstOrDefault() : null;
                var propertyName = booking != null ? _db.Properties.Where(p => p.PropertyId == booking.PropertyId).Select(p => p.PropertyName).FirstOrDefault() : null;
                var flatName = booking != null && booking.FlatId > 0 ? _db.PropertyFlats.Where(f => f.FlatId == booking.FlatId).Select(f => f.FlatName).FirstOrDefault() : null;

                if (booking == null)
                {
                    return Json(new { success = false, message = "Booking not found" });
                }

                // Get payment plan milestones that do not yet have invoices.
                var paymentPlan = _db.PaymentPlans.FirstOrDefault(p => p.BookingId == bookingId);
                var milestones = new List<object>();

                if (paymentPlan != null)
                {
                    var invoicedInstallmentIds = _db.Invoices
                        .Where(i => i.InstallmentId.HasValue)
                        .Select(i => i.InstallmentId!.Value)
                        .ToHashSet();

                    var installments = _db.PaymentInstallments
                        .Where(i => i.PlanId == paymentPlan.PlanId)
                        .OrderBy(i => i.InstallmentNumber)
                        .ToList();

                    milestones = installments
                        .Where(i => !invoicedInstallmentIds.Contains(i.InstallmentId))
                        .Select(i => new {
                        installmentId = i.InstallmentId,
                        installmentNumber = i.InstallmentNumber,
                        milestoneName = i.MilestoneName,
                        amount = i.Amount,
                        dueDate = i.DueDate.ToString("yyyy-MM-dd"),
                        status = i.Status,
                        isPaid = string.Equals(i.Status, "Paid", StringComparison.OrdinalIgnoreCase) || i.PaidAmount >= i.Amount
                    }).ToList<object>();
                }

                return Json(new {
                    success = true,
                    leadName = leadName ?? "",
                    propertyName = propertyName ?? "",
                    flatName = flatName ?? "",
                    paymentType = booking?.PaymentType ?? "",
                    totalAmount = booking.TotalAmount,
                    milestones = milestones
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // POST: Invoices/Create
        [HttpPost]
        public IActionResult Create(InvoiceModel model, List<InvoiceItemModel> items)
        {
            // Validate model
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return Json(new { success = false, message = "Validation failed", errors });
            }
            // Validate DueDate is in valid SQL Server range
            if (model.DueDate < new DateTime(1753, 1, 1) || model.DueDate > new DateTime(9999, 12, 31))
            {
                return Json(new { success = false, message = "DueDate is out of valid SQL Server range (1753-01-01 to 9999-12-31). Please select a valid due date." });
            }
            try
            {
                var booking = _db.Bookings.FirstOrDefault(b => b.BookingId == model.BookingId);
                if (booking == null)
                {
                    return Json(new { success = false, message = "Invalid booking selected." });
                }

                bool isFullPayment = string.Equals(booking.PaymentType, "FullPayment", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(booking.PaymentType, "Full Payment", StringComparison.OrdinalIgnoreCase);
                if (model.InstallmentId.HasValue)
                {
                    var installment = _db.PaymentInstallments.FirstOrDefault(i => i.InstallmentId == model.InstallmentId.Value);
                    if (installment == null)
                    {
                        return Json(new { success = false, message = "Selected milestone not found." });
                    }

                    var existingInstallmentInvoice = _db.Invoices.FirstOrDefault(i => i.InstallmentId == model.InstallmentId.Value);
                    if (existingInstallmentInvoice != null)
                    {
                        return Json(new { success = false, message = "Invoice already exists for selected milestone." });
                    }

                    // Always trust installment amount for installment-based invoice.
                    model.Amount = installment.Amount;
                }

                if (isFullPayment)
                {
                    model.InstallmentId = null;
                    model.Amount = booking.TotalAmount;
                }

                // Generate invoice number from settings prefix
                var prefix = SettingsController.GetSettingValue(_db, "InvoicePrefix", "INV");
                var year = IndianTime.Now.Year;
                var lastInvoice = _db.Invoices
                    .Where(i => i.InvoiceNumber.StartsWith($"{prefix}-{year}"))
                    .OrderByDescending(i => i.InvoiceId)
                    .FirstOrDefault();

                int nextNumber = 1;
                if (lastInvoice != null)
                {
                    var lastNumberStr = lastInvoice.InvoiceNumber.Split('-').Last();
                    if (int.TryParse(lastNumberStr, out int lastNum))
                        nextNumber = lastNum + 1;
                }

                model.InvoiceNumber = $"{prefix}-{year}-{nextNumber:D4}";
                model.InvoiceDate = IndianTime.Now;
                model.Status = "Generated";
                model.CreatedOn = IndianTime.Now;

                // No tax calculation - milestone amounts already include tax
                model.TaxAmount = 0;
                model.TotalAmount = model.Amount;
                model.PaidAmount = 0;

                // Save invoice
                _db.Invoices.Add(model);
                _db.SaveChanges();

                // Save invoice items
                foreach (var item in items)
                {
                    if (!string.IsNullOrEmpty(item.Description) && item.Amount > 0)
                    {
                        item.InvoiceId = model.InvoiceId;
                        _db.InvoiceItems.Add(item);
                    }
                }
                _db.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = "Invoice generated successfully!",
                    invoiceId = model.InvoiceId,
                    invoiceNumber = model.InvoiceNumber
                });
            }
            catch (Exception ex)
            {
                // Show inner exception if available
                var errorMsg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return Json(new { success = false, message = $"Error: {errorMsg}" });
            }
        }

        // GET: Invoices/Details/5
        [Route("invoicedetails/{id}")]
        public IActionResult Details(string id, bool autoPrint = false)
        {
            var decodedId = IdObfuscator.Decode(id);
            if (decodedId == null)
            {
                return NotFound();
            }
            ViewBag.EncodedId = id;

            var invoice = _db.Invoices.FirstOrDefault(i => i.InvoiceId == decodedId.Value);

            if (invoice == null)
            {
                return NotFound();
            }

            // Populate navigation properties manually (.Include is a no-op on MongoDbSet)
            invoice.Booking = _db.Bookings.FirstOrDefault(b => b.BookingId == invoice.BookingId);
            if (invoice.Booking != null)
            {
                invoice.Booking.Lead = _db.Leads.FirstOrDefault(l => l.LeadId == invoice.Booking.LeadId);
                invoice.Booking.Property = _db.Properties.FirstOrDefault(p => p.PropertyId == invoice.Booking.PropertyId);
                invoice.Booking.Flat = _db.PropertyFlats.FirstOrDefault(f => f.FlatId == invoice.Booking.FlatId);
            }
            if (invoice.InstallmentId.HasValue)
                invoice.Installment = _db.PaymentInstallments.FirstOrDefault(inst => inst.InstallmentId == invoice.InstallmentId.Value);

            // Get invoice items
            var items = _db.InvoiceItems.Where(i => i.InvoiceId == decodedId.Value).ToList();
            ViewBag.Items = items;

            // Get payments for this invoice
            var payments = _db.Payments.Where(p => p.InvoiceId == decodedId.Value).ToList();
            ViewBag.Payments = payments;

            // Get company settings for header
            ViewBag.CompanyName = SettingsController.GetSettingValue(_db, "CompanyName");
            ViewBag.CompanyAddress = SettingsController.GetSettingValue(_db, "CompanyAddress");
            ViewBag.CompanyPhone = SettingsController.GetSettingValue(_db, "CompanyPhone");
            ViewBag.CompanyEmail = SettingsController.GetSettingValue(_db, "CompanyEmail");
            ViewBag.CompanyGST = SettingsController.GetSettingValue(_db, "CompanyGST");
            ViewBag.GSTRate = SettingsController.GetSettingValueDecimal(_db, "GSTRate", 5);
            ViewBag.AutoPrint = autoPrint;

            // Get active bank account for payment details
            ViewBag.BankAccount = _db.BankAccounts.FirstOrDefault(b => b.IsActive);

            return View(invoice);
        }

        // POST: Invoices/UpdateStatus
        [HttpPost]
        public IActionResult UpdateStatus(int invoiceId, string status)
        {
            try
            {
                var invoice = _db.Invoices.Find(invoiceId);
                if (invoice == null)
                {
                    return Json(new { success = false, message = "Invoice not found" });
                }

                invoice.Status = status;
                invoice.ModifiedOn = IndianTime.Now;
                _db.Invoices.Update(invoice);
                _db.SaveChanges();

                return Json(new { success = true, message = "Status updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // POST: Invoices/Delete
        [HttpPost]
        public IActionResult Delete(int id)
        {
            try
            {
                var invoice = _db.Invoices.Find(id);
                if (invoice == null)
                {
                    return Json(new { success = false, message = "Invoice not found" });
                }

                // Check if there are any payments
                var hasPayments = _db.Payments.Any(p => p.InvoiceId == id);
                if (hasPayments)
                {
                    return Json(new { success = false, message = "Cannot delete invoice with existing payments" });
                }

                _db.Invoices.Remove(invoice);
                _db.SaveChanges();

                return Json(new { success = true, message = "Invoice deleted successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }



        // GET: Invoices/GenerateForInstallment
        [HttpPost]
        public IActionResult GenerateForInstallment(int installmentId)
        {
            try
            {
                var installment = _db.PaymentInstallments.FirstOrDefault(i => i.InstallmentId == installmentId);

                if (installment == null)
                {
                    return Json(new { success = false, message = "Installment not found" });
                }

                // Look up payment plan and booking manually (.Include is a no-op on MongoDbSet)
                var paymentPlan = _db.PaymentPlans.FirstOrDefault(p => p.PlanId == installment.PlanId);

                // Check if invoice already exists for this installment
                var existingInvoice = _db.Invoices.FirstOrDefault(i => i.InstallmentId == installmentId);
                if (existingInvoice != null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Invoice already exists for this installment",
                        invoiceId = existingInvoice.InvoiceId
                    });
                }

                // Generate invoice number from settings prefix
                var prefix = SettingsController.GetSettingValue(_db, "InvoicePrefix", "INV");
                var year = IndianTime.Now.Year;
                var lastInvoice = _db.Invoices
                    .Where(i => i.InvoiceNumber.StartsWith($"{prefix}-{year}"))
                    .OrderByDescending(i => i.InvoiceId)
                    .FirstOrDefault();

                int nextNumber = 1;
                if (lastInvoice != null)
                {
                    var lastNumberStr = lastInvoice.InvoiceNumber.Split('-').Last();
                    if (int.TryParse(lastNumberStr, out int lastNum))
                        nextNumber = lastNum + 1;
                }

                var invoiceNumber = $"{prefix}-{year}-{nextNumber:D4}";

                // No tax calculation - installment amounts already include tax
                var taxAmount = 0;
                var totalAmount = installment.Amount;

                // Create invoice
                var invoice = new InvoiceModel
                {
                    InvoiceNumber = invoiceNumber,
                    BookingId = paymentPlan?.BookingId ?? 0,
                    InstallmentId = installmentId,
                    InvoiceDate = IndianTime.Now,
                    DueDate = installment.DueDate,
                    Amount = installment.Amount,
                    TaxAmount = taxAmount,
                    TotalAmount = totalAmount,
                    PaidAmount = 0,
                    Status = "Generated",
                    Notes = $"Invoice for {installment.MilestoneName} installment",
                    CreatedOn = IndianTime.Now
                };

                _db.Invoices.Add(invoice);
                _db.SaveChanges();

                // Create invoice item
                var invoiceItem = new InvoiceItemModel
                {
                    InvoiceId = invoice.InvoiceId,
                    Description = $"{installment.MilestoneName} Payment - Installment {installment.InstallmentNumber}",
                    Quantity = 1,
                    Rate = installment.Amount,
                    Amount = installment.Amount
                };

                _db.InvoiceItems.Add(invoiceItem);
                _db.SaveChanges();

                return Json(new
                {
                    success = true,
                    message = "Invoice generated successfully!",
                    invoiceId = invoice.InvoiceId,
                    invoiceNumber = invoice.InvoiceNumber
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // Helper: Get current user ID
        private int _getCurrentUserId()
        {
            var userIdClaim = _httpContextAccessor.HttpContext?.Request.Cookies["UserId"];
            if (int.TryParse(userIdClaim, out int userId))
            {
                return userId;
            }
            return 0;
        }

        // GET: Invoices/DownloadPdf/{id}
        public IActionResult DownloadPdf(int id)
        {
            var invoice = _db.Invoices.FirstOrDefault(i => i.InvoiceId == id);

            if (invoice == null)
                return NotFound();

            // Populate navigation properties manually (.Include is a no-op on MongoDbSet)
            invoice.Booking = _db.Bookings.FirstOrDefault(b => b.BookingId == invoice.BookingId);
            if (invoice.Booking != null)
            {
                invoice.Booking.Lead = _db.Leads.FirstOrDefault(l => l.LeadId == invoice.Booking.LeadId);
                invoice.Booking.Property = _db.Properties.FirstOrDefault(p => p.PropertyId == invoice.Booking.PropertyId);
                invoice.Booking.Flat = _db.PropertyFlats.FirstOrDefault(f => f.FlatId == invoice.Booking.FlatId);
            }
            if (invoice.InstallmentId.HasValue)
                invoice.Installment = _db.PaymentInstallments.FirstOrDefault(inst => inst.InstallmentId == invoice.InstallmentId.Value);

            var items = _db.InvoiceItems.Where(i => i.InvoiceId == id).ToList();
            var payments = _db.Payments.Where(p => p.InvoiceId == id).ToList();

            var companyName = SettingsController.GetSettingValue(_db, "CompanyName");
            var companyAddress = SettingsController.GetSettingValue(_db, "CompanyAddress");
            var companyPhone = SettingsController.GetSettingValue(_db, "CompanyPhone");
            var companyEmail = SettingsController.GetSettingValue(_db, "CompanyEmail");
            var companyGst = SettingsController.GetSettingValue(_db, "CompanyGST");
            var gstRate = SettingsController.GetSettingValueDecimal(_db, "GSTRate", 5);

            using (var ms = new System.IO.MemoryStream())
            {
                var doc = new iTextSharp.text.Document(iTextSharp.text.PageSize.A4, 36, 36, 36, 36);
                iTextSharp.text.pdf.PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var titleFont = iTextSharp.text.FontFactory.GetFont(iTextSharp.text.FontFactory.HELVETICA_BOLD, 18);
                var labelFont = iTextSharp.text.FontFactory.GetFont(iTextSharp.text.FontFactory.HELVETICA_BOLD, 12);
                var valueFont = iTextSharp.text.FontFactory.GetFont(iTextSharp.text.FontFactory.HELVETICA, 12);

                // Company Header
                doc.Add(new iTextSharp.text.Paragraph(companyName, titleFont));
                doc.Add(new iTextSharp.text.Paragraph(companyAddress, valueFont));
                doc.Add(new iTextSharp.text.Paragraph($"Phone: {companyPhone} | Email: {companyEmail}", valueFont));
                doc.Add(new iTextSharp.text.Paragraph($"GST: {companyGst}", valueFont));
                doc.Add(new iTextSharp.text.Paragraph(" "));

                // Invoice Info
                doc.Add(new iTextSharp.text.Paragraph($"INVOICE: {invoice.InvoiceNumber}", labelFont));
                doc.Add(new iTextSharp.text.Paragraph($"Date: {invoice.InvoiceDate:dd-MMM-yyyy}", valueFont));
                doc.Add(new iTextSharp.text.Paragraph($"Due Date: {invoice.DueDate:dd-MMM-yyyy}", valueFont));
                doc.Add(new iTextSharp.text.Paragraph($"Status: {invoice.Status}", valueFont));
                doc.Add(new iTextSharp.text.Paragraph(" "));

                // Lead/Property/Flat
                doc.Add(new iTextSharp.text.Paragraph($"Lead: {invoice.Booking?.Lead?.Name}", valueFont));
                doc.Add(new iTextSharp.text.Paragraph($"Property: {invoice.Booking?.Property?.PropertyName}", valueFont));
                doc.Add(new iTextSharp.text.Paragraph($"Flat: {invoice.Booking?.Flat?.FlatName}", valueFont));
                doc.Add(new iTextSharp.text.Paragraph(" "));

                // Installment
                if (invoice.Installment != null)
                {
                    doc.Add(new iTextSharp.text.Paragraph($"Installment: {invoice.Installment.MilestoneName}", valueFont));
                }

                // Notes
                if (!string.IsNullOrEmpty(invoice.Notes))
                {
                    doc.Add(new iTextSharp.text.Paragraph($"Notes: {invoice.Notes}", valueFont));
                }
                doc.Add(new iTextSharp.text.Paragraph(" "));

                // Items Table
                if (items.Any())
                {
                    var table = new iTextSharp.text.pdf.PdfPTable(4) { WidthPercentage = 100 };
                    table.AddCell("Description");
                    table.AddCell("Qty");
                    table.AddCell("Rate");
                    table.AddCell("Amount");
                    foreach (var item in items)
                    {
                        table.AddCell(item.Description);
                        table.AddCell(item.Quantity.ToString());
                        table.AddCell(item.Rate.ToString("N2"));
                        table.AddCell(item.Amount.ToString("N2"));
                    }
                    doc.Add(table);
                }

                doc.Add(new iTextSharp.text.Paragraph(" "));

                // Amounts
                doc.Add(new iTextSharp.text.Paragraph($"Base Amount: â‚¹{invoice.Amount:N2}", labelFont));
                doc.Add(new iTextSharp.text.Paragraph($"GST ({gstRate}%): â‚¹{invoice.TaxAmount:N2}", labelFont));
                doc.Add(new iTextSharp.text.Paragraph($"Total Amount: â‚¹{invoice.TotalAmount:N2}", labelFont));
                doc.Add(new iTextSharp.text.Paragraph($"Paid Amount: â‚¹{invoice.PaidAmount:N2}", labelFont));
                doc.Add(new iTextSharp.text.Paragraph($"Outstanding: â‚¹{(invoice.TotalAmount - invoice.PaidAmount):N2}", labelFont));

                doc.Close();
                var pdfBytes = ms.ToArray();
                return File(pdfBytes, "application/pdf", $"Invoice_{invoice.InvoiceNumber}.pdf");
            }
        }
    }
}

