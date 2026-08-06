using CRM.Attributes;
using CRM.Helpers;
using CRM.Models;
using CRM.Services;
//using DocumentFormat.OpenXml.Drawing;
using iTextSharp.text;
using iTextSharp.text.html.simpleparser;
using iTextSharp.text.pdf;
using iTextSharp.tool.xml;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System.IO;
using System.Security.Claims;

namespace CRM.Controllers
{
    public class PaymentsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IRazorViewEngine _viewEngine;
        private readonly ITempDataProvider _tempDataProvider;
        private readonly IServiceProvider _serviceProvider;
        private readonly RazorpayService _razorpayService;
        private readonly ILogger<PaymentsController> _logger;

        public PaymentsController(AppDbContext db, IHttpContextAccessor httpContextAccessor, IRazorViewEngine viewEngine, ITempDataProvider tempDataProvider, IServiceProvider serviceProvider, RazorpayService razorpayService, ILogger<PaymentsController> logger)
        {
            _db = db;
            _httpContextAccessor = httpContextAccessor;
            _viewEngine = viewEngine;
            _tempDataProvider = tempDataProvider;
            _serviceProvider = serviceProvider;
            _razorpayService = razorpayService;
            _logger = logger;
        }

        // GET: Payments/Index
        [PermissionAuthorize("View")]
        public IActionResult Index(string search = "", DateTime? fromDate = null, DateTime? toDate = null)
        {
            var role = User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid, out int userId);

            var paymentsQuery = _db.Payments.AsQueryable();
            
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;

            if (role?.ToLower() == "partner")
            {
                // Partners see payments for their leads only
                var partnerLeadIds = _db.Leads.Where(l => l.ChannelPartnerId == channelPartnerId).Select(l => l.LeadId).ToList();
                var partnerBookingIds = _db.Bookings.Where(b => partnerLeadIds.Contains(b.LeadId)).Select(b => b.BookingId).ToList();
                paymentsQuery = paymentsQuery.Where(p => partnerBookingIds.Contains(p.BookingId));
            }
            else if (role?.ToLower() == "admin")
            {
                // Admin sees their own payments + partner payments for handed over leads
                var adminLeadIds = _db.Leads.Where(l => l.ChannelPartnerId == null || l.HandoverStatus == "ReadyToBook" || l.HandoverStatus == "HandedOver").Select(l => l.LeadId).ToList();
                var adminBookingIds = _db.Bookings.Where(b => b.ChannelPartnerId == null || adminLeadIds.Contains(b.LeadId)).Select(b => b.BookingId).ToList();
                paymentsQuery = paymentsQuery.Where(p => adminBookingIds.Contains(p.BookingId));
            }
            else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
            {
                if (channelPartnerId != null)
                {
                    // Partner team members see payments for their partner's leads only
                    var partnerLeadIds = _db.Leads.Where(l => l.ChannelPartnerId == channelPartnerId).Select(l => l.LeadId).ToList();
                    var partnerBookingIds = _db.Bookings.Where(b => partnerLeadIds.Contains(b.LeadId)).Select(b => b.BookingId).ToList();
                    paymentsQuery = paymentsQuery.Where(p => partnerBookingIds.Contains(p.BookingId));
                }
                else
                {
                    // Admin team members see payments for their own leads
                    var myLeadIds = _db.Leads.Where(l => l.ExecutiveId == userId).Select(l => l.LeadId).ToList();
                    var myBookingIds = _db.Bookings.Where(b => myLeadIds.Contains(b.LeadId)).Select(b => b.BookingId).ToList();
                    paymentsQuery = paymentsQuery.Where(p => myBookingIds.Contains(p.BookingId));
                }
            }

            // Apply search filter
            if (!string.IsNullOrEmpty(search))
            {
                paymentsQuery = paymentsQuery.Where(p =>
                    (!string.IsNullOrEmpty(p.ReceiptNumber) && p.ReceiptNumber.Contains(search)) ||
                    (!string.IsNullOrEmpty(p.TransactionReference) && p.TransactionReference.Contains(search)) ||
                    (!string.IsNullOrEmpty(p.Notes) && p.Notes.Contains(search)));
            }

            // Apply date filters
            if (fromDate.HasValue)
            {
                paymentsQuery = paymentsQuery.Where(p => p.PaymentDate >= fromDate.Value);
            }
            if (toDate.HasValue)
            {
                paymentsQuery = paymentsQuery.Where(p => p.PaymentDate <= toDate.Value);
            }

            var payments = paymentsQuery
                .OrderByDescending(p => p.PaymentDate)
                .ToList();

            // Load related data (.Include is a no-op on MongoDbSet)
            var invoicesForView = _db.Invoices.ToList();
            var allLeadsForInvoices = _db.Leads.ToList();
            var allBookingsForInvoices = _db.Bookings.ToList();
            foreach (var inv in invoicesForView)
            {
                inv.Booking = allBookingsForInvoices.FirstOrDefault(b => b.BookingId == inv.BookingId);
                if (inv.Booking != null)
                    inv.Booking.Lead = allLeadsForInvoices.FirstOrDefault(l => l.LeadId == inv.Booking.LeadId);
            }
            ViewBag.Invoices = invoicesForView;

            var bookingsForView = _db.Bookings.ToList();
            var allLeadsForBookings = _db.Leads.ToList();
            var allPropertiesForBookings = _db.Properties.ToList();
            foreach (var b in bookingsForView)
            {
                b.Lead = allLeadsForBookings.FirstOrDefault(l => l.LeadId == b.LeadId);
                b.Property = allPropertiesForBookings.FirstOrDefault(p => p.PropertyId == b.PropertyId);
            }
            ViewBag.Bookings = bookingsForView;
            ViewBag.SearchTerm = search;
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
            
            // Add user info for view-level access control
            ViewBag.IsPartnerTeam = currentUser?.ChannelPartnerId != null;

            return View(payments);
        }

        // GET: Payments/Create
        public IActionResult Create(int? invoiceId)
        {
            var role = User?.FindFirst(ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid, out int userId);
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
            
            // Partners and their team members cannot create payments
            if (role?.ToLower() == "partner" || currentUser?.ChannelPartnerId != null)
            {
                return RedirectToAction("Index");
            }
            
            var model = new PaymentModel();

            if (invoiceId.HasValue)
            {
                var invoice = _db.Invoices.FirstOrDefault(i => i.InvoiceId == invoiceId.Value);

                if (invoice != null)
                {
                    // Populate nav properties manually (.Include is a no-op on MongoDbSet)
                    invoice.Booking = _db.Bookings.FirstOrDefault(b => b.BookingId == invoice.BookingId);
                    if (invoice.Booking != null)
                    {
                        invoice.Booking.Lead = _db.Leads.FirstOrDefault(l => l.LeadId == invoice.Booking.LeadId);
                        invoice.Booking.Property = _db.Properties.FirstOrDefault(p => p.PropertyId == invoice.Booking.PropertyId);
                    }
                    if (invoice.InstallmentId.HasValue)
                        invoice.Installment = _db.PaymentInstallments.FirstOrDefault(inst => inst.InstallmentId == invoice.InstallmentId.Value);

                    model.InvoiceId = invoice.InvoiceId;
                    model.BookingId = invoice.BookingId;
                    model.InstallmentId = invoice.InstallmentId;
                    model.Amount = invoice.TotalAmount - invoice.PaidAmount;
                    ViewBag.Invoice = invoice;
                }
            }
            else
            {
                // Load unpaid/partial invoices (admin only)
                var pendingInvoices = _db.Invoices
                    .Where(i => i.Status != "Paid" && i.Status != "Cancelled")
                    .ToList();
                var allLeads = _db.Leads.ToList();
                var allBookings = _db.Bookings.ToList();
                foreach (var inv in pendingInvoices)
                {
                    inv.Booking = allBookings.FirstOrDefault(b => b.BookingId == inv.BookingId);
                    if (inv.Booking != null)
                        inv.Booking.Lead = allLeads.FirstOrDefault(l => l.LeadId == inv.Booking.LeadId);
                }
                ViewBag.PendingInvoices = pendingInvoices;
            }

            return View(model);
        }

        // POST: Payments/Create
        [HttpPost]
        public IActionResult Create(PaymentModel model)
        {
            try
            {
                // Generate receipt number (REC-2025-0001)
                var year = IndianTime.Now.Year;
                var lastPayment = _db.Payments
                    .Where(p => p.ReceiptNumber.StartsWith($"REC-{year}"))
                    .OrderByDescending(p => p.PaymentId)
                    .FirstOrDefault();

                int nextNumber = 1;
                if (lastPayment != null)
                {
                    var lastNumberStr = lastPayment.ReceiptNumber.Split('-').Last();
                    if (int.TryParse(lastNumberStr, out int lastNum))
                        nextNumber = lastNum + 1;
                }

                model.ReceiptNumber = $"REC-{year}-{nextNumber:D4}";
                model.PaymentDate = IndianTime.Now;
                model.ReceivedBy = _getCurrentUserId();
                model.CreatedOn = IndianTime.Now;

                // P0-P2: Validate payment doesn't exceed installment balance BEFORE saving
                if (model.InstallmentId.HasValue)
                {
                    var installmentForCheck = _db.PaymentInstallments.Find(model.InstallmentId.Value);
                    if (installmentForCheck != null)
                    {
                        var remainingBalance = installmentForCheck.Amount - installmentForCheck.PaidAmount;
                        if (model.Amount > remainingBalance)
                        {
                            return Json(new
                            {
                                success = false,
                                message = $"Payment amount (₹{model.Amount:N2}) exceeds installment balance (₹{remainingBalance:N2})"
                            });
                        }
                    }
                }

                // Save payment
                _db.Payments.Add(model);
                _db.SaveChanges();

                // Update invoice
                var invoice = _db.Invoices.Find(model.InvoiceId);
                if (invoice != null)
                {
                    invoice.PaidAmount += model.Amount;
                    invoice.ModifiedOn = IndianTime.Now;

                    // Update invoice status
                    if (invoice.PaidAmount >= invoice.TotalAmount)
                    {
                        invoice.Status = "Paid";
                    }
                    else if (invoice.PaidAmount > 0)
                    {
                        invoice.Status = "Partial";
                    }

                    _db.Invoices.Update(invoice);
                    _db.SaveChanges();
                }

                // Update installment if linked
                if (model.InstallmentId.HasValue)
                {
                    var installment = _db.PaymentInstallments.Find(model.InstallmentId.Value);
                    if (installment != null)
                    {
                        installment.PaidAmount += model.Amount;
                        installment.PaidDate = IndianTime.Now;

                        // Update installment status
                        if (installment.PaidAmount >= installment.Amount)
                        {
                            installment.Status = "Paid";
                        }
                        else if (installment.PaidAmount > 0)
                        {
                            installment.Status = "Partial";
                        }
                        else if (installment.DueDate < IndianTime.Now)
                        {
                            installment.Status = "Overdue";
                        }

                        _db.PaymentInstallments.Update(installment);
                        _db.SaveChanges();

                        // Update payment plan
                        var paymentPlan = _db.PaymentPlans.Find(installment.PlanId);
                        if (paymentPlan != null)
                        {
                            paymentPlan.PaidAmount += model.Amount;
                            paymentPlan.OutstandingAmount -= model.Amount;
                            _db.PaymentPlans.Update(paymentPlan);
                            _db.SaveChanges();
                        }
                    }
                }

                return Json(new
                {
                    success = true,
                    message = "Payment recorded successfully!",
                    paymentId = model.PaymentId,
                    receiptNumber = model.ReceiptNumber,
                    encodeid = IdObfuscator.Encode(model.PaymentId)
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // POST: Payments/ProcessOnlinePayment - P0-P1: Payment reconciliation with Razorpay verification
        [HttpPost]
        public async Task<IActionResult> ProcessOnlinePayment(int invoiceId, int? installmentId, string razorpayPaymentId, string razorpayOrderId, string razorpaySignature)
        {
            try
            {
                // P0-P1: Verify Razorpay signature to prevent payment tampering
                if (!_razorpayService.VerifyPaymentSignature(razorpayPaymentId, razorpayOrderId, razorpaySignature))
                {
                    return Json(new
                    {
                        success = false,
                        message = "Payment verification failed. If amount was deducted, it will be refunded within 5-7 business days."
                    });
                }

                var invoice = _db.Invoices.Find(invoiceId);
                if (invoice == null)
                {
                    return Json(new { success = false, message = "Invoice not found" });
                }

                // Calculate payment amount
                decimal paymentAmount = invoice.TotalAmount - invoice.PaidAmount;
                
                if (installmentId.HasValue)
                {
                    var installment = _db.PaymentInstallments.Find(installmentId.Value);
                    if (installment != null)
                    {
                        paymentAmount = installment.Amount - installment.PaidAmount;
                    }
                }

                // Generate receipt number
                var year = IndianTime.Now.Year;
                var lastPayment = _db.Payments
                    .Where(p => p.ReceiptNumber.StartsWith($"REC-{year}"))
                    .OrderByDescending(p => p.PaymentId)
                    .FirstOrDefault();

                int nextNumber = 1;
                if (lastPayment != null)
                {
                    var lastNumberStr = lastPayment.ReceiptNumber.Split('-').Last();
                    if (int.TryParse(lastNumberStr, out int lastNum))
                        nextNumber = lastNum + 1;
                }

                var payment = new PaymentModel
                {
                    InvoiceId = invoiceId,
                    BookingId = invoice.BookingId,
                    InstallmentId = installmentId,
                    Amount = paymentAmount,
                    PaymentMethod = "Online/Razorpay",
                    TransactionReference = razorpayPaymentId,
                    ReceiptNumber = $"REC-{year}-{nextNumber:D4}",
                    PaymentDate = IndianTime.Now,
                    ReceivedBy = _getCurrentUserId(),
                    Notes = $"Online payment via Razorpay. Order ID: {razorpayOrderId}",
                    CreatedOn = IndianTime.Now
                };

                _db.Payments.Add(payment);
                await _db.SaveChangesAsync();

                // Update invoice
                invoice.PaidAmount += paymentAmount;
                invoice.ModifiedOn = IndianTime.Now;
                invoice.Status = invoice.PaidAmount >= invoice.TotalAmount ? "Paid" : "Partial";
                _db.Invoices.Update(invoice);
                await _db.SaveChangesAsync();

                // Update installment if linked
                if (installmentId.HasValue)
                {
                    var installment = _db.PaymentInstallments.Find(installmentId.Value);
                    if (installment != null)
                    {
                        installment.PaidAmount += paymentAmount;
                        installment.PaidDate = IndianTime.Now;
                        installment.Status = installment.PaidAmount >= installment.Amount ? "Paid" : "Partial";
                        _db.PaymentInstallments.Update(installment);
                        await _db.SaveChangesAsync();

                        // Update payment plan
                        var paymentPlan = _db.PaymentPlans.Find(installment.PlanId);
                        if (paymentPlan != null)
                        {
                            paymentPlan.PaidAmount += paymentAmount;
                            paymentPlan.OutstandingAmount -= paymentAmount;
                            _db.PaymentPlans.Update(paymentPlan);
                            await _db.SaveChangesAsync();
                        }
                    }
                }

                return Json(new
                {
                    success = true,
                    message = "Payment processed successfully!",
                    paymentId = payment.PaymentId,
                    receiptNumber = payment.ReceiptNumber,
                    encodeid = IdObfuscator.Encode(payment.PaymentId)
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error processing payment: {ex.Message}" });
            }
        }

        // GET: Payments/Receipt/5

        [Route("paymentreceipt/{id}")]
        public IActionResult Receipt(string id, bool autoPrint = false)
        {
            var decodedId = IdObfuscator.Decode(id);
            if (decodedId == null)
            {
                return NotFound();
            }
            ViewBag.EncodedId = id;

            var payment = _db.Payments.FirstOrDefault(p => p.PaymentId == decodedId.Value);

            if (payment == null)
            {
                return NotFound();
            }

            // Populate navigation properties manually (.Include is a no-op on MongoDbSet)
            payment.Invoice = _db.Invoices.FirstOrDefault(i => i.InvoiceId == payment.InvoiceId);
            if (payment.Invoice != null)
            {
                payment.Invoice.Booking = _db.Bookings.FirstOrDefault(b => b.BookingId == payment.Invoice.BookingId);
                if (payment.Invoice.Booking != null)
                {
                    payment.Invoice.Booking.Lead = _db.Leads.FirstOrDefault(l => l.LeadId == payment.Invoice.Booking.LeadId);
                    payment.Invoice.Booking.Property = _db.Properties.FirstOrDefault(p => p.PropertyId == payment.Invoice.Booking.PropertyId);
                    payment.Invoice.Booking.Flat = _db.PropertyFlats.FirstOrDefault(f => f.FlatId == payment.Invoice.Booking.FlatId);
                }
            }
            if (payment.InstallmentId.HasValue)
                payment.Installment = _db.PaymentInstallments.FirstOrDefault(inst => inst.InstallmentId == payment.InstallmentId.Value);

            // Get company settings for header
            ViewBag.CompanyName = SettingsController.GetSettingValue(_db, "CompanyName");
            ViewBag.CompanyAddress = SettingsController.GetSettingValue(_db, "CompanyAddress");
            ViewBag.CompanyPhone = SettingsController.GetSettingValue(_db, "CompanyPhone");
            ViewBag.CompanyEmail = SettingsController.GetSettingValue(_db, "CompanyEmail");
            ViewBag.CompanyGST = SettingsController.GetSettingValue(_db, "CompanyGST");
            ViewBag.AutoPrint = autoPrint;

            return View(payment);
        }

        // POST: Payments/Delete
        [HttpPost]
        public IActionResult Delete(int id)
        {
            try
            {
                var payment = _db.Payments.Find(id);
                if (payment == null)
                {
                    return Json(new { success = false, message = "Payment not found" });
                }

                // Reverse the payment amounts
                var invoice = _db.Invoices.Find(payment.InvoiceId);
                if (invoice != null)
                {
                    invoice.PaidAmount -= payment.Amount;
                    
                    // Update invoice status
                    if (invoice.PaidAmount == 0)
                    {
                        invoice.Status = "Generated";
                    }
                    else if (invoice.PaidAmount < invoice.TotalAmount)
                    {
                        invoice.Status = "Partial";
                    }

                    _db.Invoices.Update(invoice);
                }

                // Reverse installment payment
                if (payment.InstallmentId.HasValue)
                {
                    var installment = _db.PaymentInstallments.Find(payment.InstallmentId.Value);
                    if (installment != null)
                    {
                        installment.PaidAmount -= payment.Amount;
                        
                        if (installment.PaidAmount == 0)
                        {
                            installment.Status = "Pending";
                            installment.PaidDate = null;
                        }
                        else if (installment.PaidAmount < installment.Amount)
                        {
                            installment.Status = "Partial";
                        }

                        _db.PaymentInstallments.Update(installment);

                        // Update payment plan
                        var paymentPlan = _db.PaymentPlans.Find(installment.PlanId);
                        if (paymentPlan != null)
                        {
                            paymentPlan.PaidAmount -= payment.Amount;
                            paymentPlan.OutstandingAmount += payment.Amount;
                            _db.PaymentPlans.Update(paymentPlan);
                        }
                    }
                }

                _db.Payments.Remove(payment);
                _db.SaveChanges();

                return Json(new { success = true, message = "Payment deleted successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // GET: Payments/DownloadPdf/{id}
        [HttpGet]
        public IActionResult DownloadPdf(int id)
        {
            var payment = _db.Payments.FirstOrDefault(p => p.PaymentId == id);

            if (payment == null)
                return NotFound();

            // Populate navigation properties manually (.Include is a no-op on MongoDbSet)
            payment.Invoice = _db.Invoices.FirstOrDefault(i => i.InvoiceId == payment.InvoiceId);
            if (payment.Invoice != null)
            {
                payment.Invoice.Booking = _db.Bookings.FirstOrDefault(b => b.BookingId == payment.Invoice.BookingId);
                if (payment.Invoice.Booking != null)
                {
                    payment.Invoice.Booking.Lead = _db.Leads.FirstOrDefault(l => l.LeadId == payment.Invoice.Booking.LeadId);
                    payment.Invoice.Booking.Property = _db.Properties.FirstOrDefault(p => p.PropertyId == payment.Invoice.Booking.PropertyId);
                    payment.Invoice.Booking.Flat = _db.PropertyFlats.FirstOrDefault(f => f.FlatId == payment.Invoice.Booking.FlatId);
                }
            }
            if (payment.InstallmentId.HasValue)
                payment.Installment = _db.PaymentInstallments.FirstOrDefault(inst => inst.InstallmentId == payment.InstallmentId.Value);

            var companyName = SettingsController.GetSettingValue(_db, "CompanyName");
            var companyAddress = SettingsController.GetSettingValue(_db, "CompanyAddress");
            var companyPhone = SettingsController.GetSettingValue(_db, "CompanyPhone");
            var companyEmail = SettingsController.GetSettingValue(_db, "CompanyEmail");
            var companyGst = SettingsController.GetSettingValue(_db, "CompanyGST");
            var gstRate = SettingsController.GetSettingValueDecimal(_db, "GSTRate", 5);

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4, 36, 36, 36, 36);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 18);
                var labelFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
                var valueFont = FontFactory.GetFont(FontFactory.HELVETICA, 12);

                // Company Header
                doc.Add(new Paragraph(companyName, titleFont));
                doc.Add(new Paragraph(companyAddress, valueFont));
                doc.Add(new Paragraph($"Phone: {companyPhone} | Email: {companyEmail}", valueFont));
                doc.Add(new Paragraph($"GST: {companyGst}", valueFont));
                doc.Add(new Paragraph(" "));

                // Receipt Info
                doc.Add(new Paragraph($"PAYMENT RECEIPT: {payment.ReceiptNumber}", labelFont));
                doc.Add(new Paragraph($"Date: {payment.PaymentDate:dd-MMM-yyyy}", valueFont));
                doc.Add(new Paragraph($"Status: {(payment.Invoice?.Status ?? "")}", valueFont));
                doc.Add(new Paragraph(" "));

                // Lead/Property/Flat
                doc.Add(new Paragraph($"Lead: {payment.Invoice?.Booking?.Lead?.Name}", valueFont));
                doc.Add(new Paragraph($"Property: {payment.Invoice?.Booking?.Property?.PropertyName}", valueFont));
                doc.Add(new Paragraph($"Flat: {payment.Invoice?.Booking?.Flat?.FlatName}", valueFont));
                doc.Add(new Paragraph(" "));

                // Installment
                if (payment.Installment != null)
                {
                    doc.Add(new Paragraph($"Installment: {payment.Installment.MilestoneName}", valueFont));
                }

                // Notes
                if (!string.IsNullOrEmpty(payment.Notes))
                {
                    doc.Add(new Paragraph($"Notes: {payment.Notes}", valueFont));
                }
                doc.Add(new Paragraph(" "));

                // Amounts
                doc.Add(new Paragraph($"Amount Received: â‚¹{payment.Amount:N2}", labelFont));
                doc.Add(new Paragraph($"Payment Method: {payment.PaymentMethod}", valueFont));
                doc.Add(new Paragraph($"Invoice Total: â‚¹{payment.Invoice?.TotalAmount:N2}", valueFont));
                doc.Add(new Paragraph($"Previously Paid: â‚¹{(payment.Invoice?.PaidAmount ?? 0) - payment.Amount:N2}", valueFont));
                doc.Add(new Paragraph($"Outstanding: â‚¹{(payment.Invoice?.TotalAmount ?? 0) - (payment.Invoice?.PaidAmount ?? 0):N2}", valueFont));
                doc.Add(new Paragraph(" "));

                // Amount in Words
                doc.Add(new Paragraph($"Amount in Words: Rupees {NumberToWords((int)payment.Amount)} Only", valueFont));
                doc.Add(new Paragraph(" "));

                // Footer
                doc.Add(new Paragraph("This is a computer-generated receipt and does not require a signature.", valueFont));
                doc.Add(new Paragraph("Payment is subject to realization of cheque/online transfer.", valueFont));
                doc.Add(new Paragraph("Please retain this receipt for your records.", valueFont));
                doc.Add(new Paragraph(" "));
                doc.Add(new Paragraph("Thank you for your payment!", valueFont));
                doc.Add(new Paragraph($"For any queries, please contact us at {companyPhone} or {companyEmail}", valueFont));

                doc.Close();
                var pdfBytes = ms.ToArray();
                return File(pdfBytes, "application/pdf", $"Receipt_{payment.ReceiptNumber}.pdf");
            }
        }

        // Helper to convert number to words (Indian system)
        private string NumberToWords(int number)
        {
            if (number == 0) return "Zero";
            string[] ones = { "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine" };
            string[] teens = { "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen" };
            string[] tens = { "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety" };
            string words = "";
            if (number >= 10000000) { words += NumberToWords(number / 10000000) + " Crore "; number %= 10000000; }
            if (number >= 100000) { words += NumberToWords(number / 100000) + " Lakh "; number %= 100000; }
            if (number >= 1000) { words += NumberToWords(number / 1000) + " Thousand "; number %= 1000; }
            if (number >= 100) { words += ones[number / 100] + " Hundred "; number %= 100; }
            if (number >= 20) { words += tens[number / 10] + " "; number %= 10; }
            if (number >= 10) { words += teens[number - 10] + " "; number = 0; }
            if (number > 0) { words += ones[number] + " "; }
            return words.Trim();
        }

        // GET: Payments/GetInvoiceDetails
        [HttpGet]
        public IActionResult GetInvoiceDetails(int invoiceId)
        {
            try
            {
                var invoice = _db.Invoices.FirstOrDefault(i => i.InvoiceId == invoiceId);

                if (invoice == null)
                {
                    return Json(new { success = false, message = "Invoice not found" });
                }

                // Look up related data manually (.Include is a no-op on MongoDbSet)
                var bookingForInvoice = _db.Bookings.FirstOrDefault(b => b.BookingId == invoice.BookingId);
                var leadName = bookingForInvoice != null ? _db.Leads.Where(l => l.LeadId == bookingForInvoice.LeadId).Select(l => l.Name).FirstOrDefault() : null;
                var propertyName = bookingForInvoice != null ? _db.Properties.Where(p => p.PropertyId == bookingForInvoice.PropertyId).Select(p => p.PropertyName).FirstOrDefault() : null;
                var milestoneName = invoice.InstallmentId.HasValue
                    ? _db.PaymentInstallments.Where(inst => inst.InstallmentId == invoice.InstallmentId.Value).Select(inst => inst.MilestoneName).FirstOrDefault()
                    : null;

                return Json(new
                {
                    success = true,
                    invoiceNumber = invoice.InvoiceNumber,
                    bookingId = invoice.BookingId,
                    installmentId = invoice.InstallmentId,
                    leadName = leadName,
                    propertyName = propertyName,
                    totalAmount = invoice.TotalAmount,
                    paidAmount = invoice.PaidAmount,
                    balanceAmount = invoice.TotalAmount - invoice.PaidAmount,
                    milestoneName = milestoneName
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // GET: Payment/Upgrade - Handle upgrade payment page
        [AllowAnonymous]
        public IActionResult Upgrade(string orderId)
        {
            if (string.IsNullOrEmpty(orderId))
            {
                return BadRequest("Order ID is required");
            }
            
            ViewBag.OrderId = orderId;
            return View();
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
    }
}

