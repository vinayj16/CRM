using CRM.Helpers;
using CRM.Models;
using CRM.Services;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using CRM.Attributes;
using System.Security.Claims;

namespace CRM.Controllers
{
    public class BookingsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IWebHostEnvironment _environment;
        private readonly PayoutService _payoutService;
        private readonly ILogger<BookingsController> _logger;

        public BookingsController(AppDbContext db, IHttpContextAccessor httpContextAccessor, IWebHostEnvironment environment, PayoutService payoutService, ILogger<BookingsController> logger)
        {
            _db = db;
            _httpContextAccessor = httpContextAccessor;
            _environment = environment;
            _payoutService = payoutService;
            _logger = logger;
        }

        // GET: Bookings/Index
        [PermissionAuthorize("View")]
        public IActionResult Index(string search = "", string status = "")
        {
            var role = User?.FindFirst(ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid ?? "0", out int userId);

            var bookingsQuery = _db.Bookings.AsQueryable();
            
            // Get current user's ChannelPartnerId
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;

            // Role-based filtering
            if (role?.ToLower() == "partner")
            {
                // Partners see bookings for their leads only
                var partnerLeadIds = _db.Leads.Where(l => l.ChannelPartnerId == channelPartnerId).Select(l => l.LeadId).ToList();
                bookingsQuery = bookingsQuery.Where(b => partnerLeadIds.Contains(b.LeadId));
            }
            else if (role?.ToLower() == "admin")
            {
                // Admin sees their own bookings + partner bookings for handed over leads
                var adminLeadIds = _db.Leads.Where(l => l.ChannelPartnerId == null || l.HandoverStatus == "ReadyToBook" || l.HandoverStatus == "HandedOver").Select(l => l.LeadId).ToList();
                bookingsQuery = bookingsQuery.Where(b => b.ChannelPartnerId == null || adminLeadIds.Contains(b.LeadId));
            }
            else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
            {
                // Agent/Sales see only their assigned leads' bookings
                var myLeadIds = _db.Leads.Where(l => l.ExecutiveId == userId).Select(l => l.LeadId).ToList();
                bookingsQuery = bookingsQuery.Where(b => myLeadIds.Contains(b.LeadId));
            }

            // Apply search filter
            if (!string.IsNullOrEmpty(search))
            {
                bookingsQuery = bookingsQuery.Where(b =>
                    b.BookingNumber.Contains(search) ||
                    (b.Notes != null && b.Notes.Contains(search)));
            }

            // Apply status filter
            if (!string.IsNullOrEmpty(status))
            {
                bookingsQuery = bookingsQuery.Where(b => b.Status == status);
            }

            var bookings = bookingsQuery
                .OrderByDescending(b => b.BookingDate)
                .ToList();

            // Calculate realized collections from payment records for each booking.
            // This avoids marking FullPayment bookings as paid when no payment is recorded.
            var bookingIds = bookings.Select(b => b.BookingId).ToList();
            var paidAmounts = _db.Payments
                .Where(p => bookingIds.Contains(p.BookingId))
                .GroupBy(p => p.BookingId)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
            ViewBag.PaymentPlans = paidAmounts;

            // Get related data for display
            ViewBag.Leads = _db.Leads.ToList();
            ViewBag.Properties = _db.Properties.ToList();
            ViewBag.Flats = _db.PropertyFlats.ToList();
            ViewBag.Quotations = _db.Quotations.ToList();
            ViewBag.SearchTerm = search;
            ViewBag.StatusFilter = status;
            
            // Add user info for view-level access control
            ViewBag.IsPartnerTeam = currentUser?.ChannelPartnerId != null;

            return View(bookings);
        }

        // GET: Bookings/Create
        public IActionResult Create(int? quotationId)
        {
            var role = User?.FindFirst(ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid ?? "0", out int userId);
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
            
            // Partners and their team members cannot create bookings
            if (role?.ToLower() == "partner" || currentUser?.ChannelPartnerId != null)
            {
                return RedirectToAction("Index");
            }
            
            var model = new BookingModel();

            if (quotationId.HasValue)
            {
                // Load from quotation
                var quotation = _db.Quotations
                    
                    
                    
                    .FirstOrDefault(q => q.QuotationId == quotationId.Value);

                if (quotation != null && quotation.Status == "Accepted")
                {
                    model.QuotationId = quotation.QuotationId;
                    model.LeadId = quotation.LeadId;
                    model.PropertyId = quotation.PropertyId;
                    model.FlatId = quotation.FlatId ?? 0;
                    model.TotalAmount = quotation.GrandTotal;

                    ViewBag.Quotation = quotation;
                    ViewBag.Lead = quotation.Lead;
                    ViewBag.Property = quotation.Property;
                    ViewBag.Flat = quotation.Flat;
                }
            }
            else
            {
                // Load all quotations for dropdown
                var allQuotations = _db.Quotations
                    
                    
                    .ToList();

                // Role-based filtering for non-partners
                if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
                {
                    var myLeadIds = _db.Leads.Where(l => l.ExecutiveId == userId).Select(l => l.LeadId).ToList();
                    allQuotations = allQuotations.Where(q => myLeadIds.Contains(q.LeadId)).ToList();
                }

                // Find the latest accepted quotation per lead
                var latestAccepted = allQuotations
                    .Where(q => q.Status == "Accepted")
                    .GroupBy(q => q.LeadId)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(q => q.QuotationId).FirstOrDefault()?.QuotationId);

                ViewBag.AllQuotations = allQuotations;
                ViewBag.LatestAcceptedQuotationIds = latestAccepted;
                // Show only accepted quotations for available flats (not booked)
                // and exclude quotations already converted to non-cancelled bookings.
                var acceptedQuotations = allQuotations
                    .Where(q => q.Status == "Accepted")
                    .OrderByDescending(q => q.QuotationId)
                    .ToList();

                var bookedFlatIds = _db.PropertyFlats
                    .Where(f => f.FlatStatus == "Booked")
                    .Select(f => f.FlatId)
                    .ToHashSet();

                var bookedQuotationIds = _db.Bookings
                    .Where(b => b.QuotationId.HasValue && b.Status != "Cancelled")
                    .Select(b => b.QuotationId!.Value)
                    .Distinct()
                    .ToHashSet();

                var availableAcceptedQuotations = acceptedQuotations
                    .Where(q => !bookedQuotationIds.Contains(q.QuotationId))
                    .Where(q => !q.FlatId.HasValue || !bookedFlatIds.Contains(q.FlatId.Value))
                    .ToList();
                ViewBag.AcceptedQuotations = availableAcceptedQuotations;
            }

            // Get booking percentage from settings
            var bookingPercentage = SettingsController.GetSettingValueDecimal(_db, "BookingPercentage", 20);
            ViewBag.BookingPercentage = bookingPercentage;

            return View(model);
        }

        // POST: Bookings/Create
        [HttpPost]
        public async Task<IActionResult> Create(BookingModel model, List<IFormFile> documents)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                // Generate booking number
                var prefix = SettingsController.GetSettingValue(_db, "BookingPrefix", "BK");
                var year = IndianTime.Now.Year;
                var lastBooking = _db.Bookings
                    .Where(b => b.BookingNumber.StartsWith($"{prefix}-{year}"))
                    .OrderByDescending(b => b.BookingId)
                    .FirstOrDefault();

                int nextNumber = 1;
                if (lastBooking != null)
                {
                    var lastNumberStr = lastBooking.BookingNumber.Split('-').Last();
                    if (int.TryParse(lastNumberStr, out int lastNum))
                        nextNumber = lastNum + 1;
                }

                model.BookingNumber = $"{prefix}-{year}-{nextNumber:D4}";
                model.BookingDate = IndianTime.Now;
                model.Status = "Confirmed";
                model.CreatedBy = _getCurrentUserId();
                model.CreatedOn = IndianTime.Now;
                
                // Set ChannelPartnerId based on current user
                var role = User?.FindFirst(ClaimTypes.Role)?.Value;
                var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(uid ?? "0", out int userId);
                var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
                model.ChannelPartnerId = currentUser?.ChannelPartnerId;

                // Booking constraints:
                // 1. Only one booking per lead per flat
                // 2. Flat must not be already booked
                var existingBooking = _db.Bookings.FirstOrDefault(b => b.LeadId == model.LeadId && b.FlatId == model.FlatId);
                var flatBooked = _db.PropertyFlats.FirstOrDefault(f => f.FlatId == model.FlatId && f.FlatStatus == "Booked");
                if (existingBooking != null)
                {
                    return Json(new { success = false, message = "This lead already has a booking for this flat." });
                }
                if (flatBooked != null)
                {
                    return Json(new { success = false, message = "This flat is already booked." });
                }

                // Auto-generate BookingId (MongoDbSet does not auto-increment int keys)
                model.BookingId = _db.Bookings.Any() ? await _db.Bookings.MaxAsync(b => b.BookingId) + 1 : 1;

                // Save booking
                _db.Bookings.Add(model);
                await _db.SaveChangesAsync();

                

                // Update quotation status if linked
                if (model.QuotationId.HasValue)
                {
                    var quotation = await _db.Quotations.FindAsync(model.QuotationId.Value);
                    if (quotation != null)
                    {
                        // Check for other accepted quotations for same lead and property
                        var otherAcceptedQuotations = _db.Quotations.Where(q =>
                            q.QuotationId != quotation.QuotationId &&
                            q.LeadId == quotation.LeadId &&
                            q.PropertyId == quotation.PropertyId &&
                            q.Status == "Accepted").ToList();
                        bool forceAccept = false;
                        if (Request.HasFormContentType && Request.Form.ContainsKey("ForceAccept"))
                        {
                            bool.TryParse(Request.Form["ForceAccept"], out forceAccept);
                        }
                        if (otherAcceptedQuotations.Any() && !forceAccept)
                        {
                            return Json(new {
                                success = false,
                                message = "Another quotation for this customer and property is already accepted. If you proceed, all other accepted quotations will be rejected and this one will be accepted. Do you want to continue?"
                            });
                        }
                        foreach (var q in otherAcceptedQuotations)
                        {
                            q.Status = "Rejected";
                            _db.Quotations.Update(q);
                        }
                        quotation.Status = "Accepted";
                        _db.Quotations.Update(quotation);
                        await _db.SaveChangesAsync();
                    }
                }

                // Handle document uploads
                if (documents != null && documents.Any())
                {
                    var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "bookings", model.BookingId.ToString());
                    Directory.CreateDirectory(uploadsFolder);

                    var maxDocId = _db.BookingDocuments.ToList().Count > 0 ? _db.BookingDocuments.ToList().Max(d => d.DocumentId) : 0;
                    foreach (var file in documents)
                    {
                        if (file.Length > 0)
                        {
                            var fileName = Path.GetFileName(file.FileName) ?? "unknown";
                            var filePath = Path.Combine(uploadsFolder, fileName);

                            using (var stream = new FileStream(filePath, FileMode.Create))
                            {
                                await file.CopyToAsync(stream);
                            }

                            maxDocId++;
                            var document = new BookingDocumentModel
                            {
                                DocumentId = maxDocId,
                                BookingId = model.BookingId,
                                DocumentType = GetDocumentType(fileName),
                                DocumentName = fileName,
                                FilePath = $"/uploads/bookings/{model.BookingId}/{fileName}",
                                UploadedBy = _getCurrentUserId(),
                                UploadedOn = IndianTime.Now
                            };

                            _db.BookingDocuments.Add(document);
                        }
                    }
                    await _db.SaveChangesAsync();
                }

                // Create payment plan if EMI selected
                if (model.PaymentType == "EMI")
                {
                    await CreatePaymentPlan(model.BookingId, model.TotalAmount, model.BookingAmount);
                }

                // Update lead stage and status
                var lead = _db.Leads.FirstOrDefault(l => l.LeadId == model.LeadId);
                if (lead != null)
                {
                    lead.Stage = "Booked";
                    lead.Status = "Closed";
                    _db.Leads.Update(lead);
                    await _db.SaveChangesAsync();
                }
                // Update flat status to Booked
                var flat = await _db.PropertyFlats.FindAsync(model.FlatId);
                if (flat != null)
                {
                    flat.FlatStatus = "Booked";
                    flat.IsActive = false; // Mark as inactive when booked
                    _db.PropertyFlats.Update(flat);
                    await _db.SaveChangesAsync();
                }

                // AUTOMATED COMMISSION CALCULATION
                await ProcessCommissionsForBooking(model);
                
                // Commit transaction
                await transaction.CommitAsync();
                
                return Json(new
                {
                    success = true,
                    message = "Booking created successfully!",
                    bookingId = model.BookingId,
                    bookingNumber = model.BookingNumber
                });
            }
            catch (Exception ex)
            {

                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // GET: Bookings/Details/5
        [Route("bookingdetails/{id}")]

        public IActionResult Details(string id, bool autoPrint = false)
        {
            var decodedId = IdObfuscator.Decode(id);
            if (decodedId == null)
            {
                return NotFound();
            }
            ViewBag.EncodedId = id;

            var booking = _db.Bookings.FirstOrDefault(b => b.BookingId == decodedId.Value);

            if (booking == null)
            {
                return NotFound();
            }

            // Populate navigation properties manually (.Include is a no-op on MongoDbSet)
            booking.Lead = _db.Leads.FirstOrDefault(l => l.LeadId == booking.LeadId);
            booking.Property = _db.Properties.FirstOrDefault(p => p.PropertyId == booking.PropertyId);
            booking.Flat = _db.PropertyFlats.FirstOrDefault(f => f.FlatId == booking.FlatId);
            if (booking.QuotationId.HasValue)
                booking.Quotation = _db.Quotations.FirstOrDefault(q => q.QuotationId == booking.QuotationId.Value);

            // Get documents
            var documents = _db.BookingDocuments
                .Where(d => d.BookingId == decodedId.Value)
                .ToList();

            // Get payment plan if EMI
            PaymentPlanModel paymentPlan = null;
            List<PaymentInstallmentModel> installments = null;

            if (booking.PaymentType == "EMI")
            {
                paymentPlan = _db.PaymentPlans
                    .FirstOrDefault(p => p.BookingId == decodedId.Value);

                if (paymentPlan != null)
                {
                    installments = _db.PaymentInstallments
                        .Where(i => i.PlanId == paymentPlan.PlanId)
                        .OrderBy(i => i.InstallmentNumber)
                        .ToList();
                }
            }

            // Get company settings for header
#pragma warning disable CS8600
            ViewBag.CompanyName = SettingsController.GetSettingValue(_db, "CompanyName") ?? "Company";
            ViewBag.CompanyAddress = SettingsController.GetSettingValue(_db, "CompanyAddress") ?? "Address";
            ViewBag.CompanyPhone = SettingsController.GetSettingValue(_db, "CompanyPhone") ?? "Phone";
            ViewBag.CompanyEmail = SettingsController.GetSettingValue(_db, "CompanyEmail") ?? "Email";
            ViewBag.CompanyGST = SettingsController.GetSettingValue(_db, "CompanyGST") ?? "GST";
#pragma warning restore CS8600

            ViewBag.Documents = documents;
            ViewBag.PaymentPlan = paymentPlan;
            ViewBag.Installments = installments;
            ViewBag.AutoPrint = autoPrint;

            return View(booking);
        }

        // POST: Bookings/UpdateStatus
        [HttpPost]
        public async Task<IActionResult> UpdateStatus(int bookingId, string status)
        {
            try
            {
                var booking = _db.Bookings.Find(bookingId);
                if (booking == null)
                {
                    return Json(new { success = false, message = "Booking not found" });
                }

                booking.Status = status;
                booking.ModifiedOn = IndianTime.Now;

                // If status is 'Confirmed', update lead stage and status
                if (status == "Confirmed")
                {
                    var lead = _db.Leads.FirstOrDefault(l => l.LeadId == booking.LeadId);
                    if (lead != null)
                    {
                        lead.Stage = "Booked";
                        lead.Status = "Won";
                        _db.Leads.Update(lead);
                        _db.SaveChanges();
                    }

                    // AUTOMATED COMMISSION CALCULATION FOR STATUS UPDATE
                    await ProcessCommissionsForBooking(booking);
                }

                // If cancelling, free up the flat
                if (status == "Cancelled")
                {
                    var flat = _db.PropertyFlats.Find(booking.FlatId);
                    if (flat != null)
                    {
                        flat.FlatStatus = "Available";
                        flat.IsActive = true; // Mark as active when cancelled
                        _db.PropertyFlats.Update(flat);
                    }
                }

                _db.SaveChanges();
                return Json(new { success = true, message = "Status updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // POST: Bookings/Delete
        [HttpPost]
        public IActionResult Delete(int id)
        {
            try
            {
                var booking = _db.Bookings.Find(id);
                if (booking == null)
                {
                    return Json(new { success = false, message = "Booking not found" });
                }

                // Check if there are any payments
                var hasPayments = _db.Payments.Any(p => p.BookingId == id);
                if (hasPayments)
                {
                    return Json(new { success = false, message = "Cannot delete booking with existing payments" });
                }

                // Free up the flat
                var flat = _db.PropertyFlats.Find(booking.FlatId);
                if (flat != null && flat.FlatStatus == "Booked")
                {
                    flat.FlatStatus = "Available";
                    flat.IsActive = true; // Mark as active when booking deleted
                    _db.PropertyFlats.Update(flat);
                }

                _db.Bookings.Remove(booking);
                _db.SaveChanges();

                return Json(new { success = true, message = "Booking deleted successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // GET: Bookings/GetQuotationDetails
        [HttpGet]
        public IActionResult GetQuotationDetails(int quotationId)
        {
            try
            {
                var quotation = _db.Quotations.FirstOrDefault(q => q.QuotationId == quotationId);

                if (quotation == null)
                {
                    return Json(new { success = false, message = "Quotation not found" });
                }

                // Look up related data manually (.Include is a no-op on MongoDbSet)
                var leadName = _db.Leads.Where(l => l.LeadId == quotation.LeadId).Select(l => l.Name).FirstOrDefault();
                var propertyName = _db.Properties.Where(p => p.PropertyId == quotation.PropertyId).Select(p => p.PropertyName).FirstOrDefault();
                var flatName = quotation.FlatId.HasValue
                    ? _db.PropertyFlats.Where(f => f.FlatId == quotation.FlatId.Value).Select(f => f.FlatName).FirstOrDefault()
                    : null;

                return Json(new
                {
                    success = true,
                    leadId = quotation.LeadId,
                    leadName = leadName,
                    propertyId = quotation.PropertyId,
                    propertyName = propertyName,
                    flatId = quotation.FlatId,
                    flatName = flatName,
                    totalAmount = quotation.GrandTotal
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // Helper: Create Payment Plan
        private async Task CreatePaymentPlan(int bookingId, decimal totalAmount, decimal bookingAmount)
        {
            var emiStructure = SettingsController.GetSettingValue(_db, "EMIStructure", "20-30-30-20");
            var percentages = emiStructure.Split('-').Select(p => decimal.Parse(p)).ToList();

            var paymentPlan = new PaymentPlanModel
            {
                PlanId = _db.PaymentPlans.Any() ? await _db.PaymentPlans.MaxAsync(p => p.PlanId) + 1 : 1,
                BookingId = bookingId,
                TotalAmount = totalAmount,
                PaidAmount = 0,
                OutstandingAmount = totalAmount,
                PlanType = "Milestone",
                PlanStructure = emiStructure,
                CreatedOn = IndianTime.Now
            };

            _db.PaymentPlans.Add(paymentPlan);
            await _db.SaveChangesAsync();

            // Create installments
            var milestones = new[] { "Booking", "Agreement", "Foundation", "Possession" };
            var today = IndianTime.Now;

            var maxInstallmentId = _db.PaymentInstallments.ToList().Count > 0 ? _db.PaymentInstallments.ToList().Max(x => x.InstallmentId) : 0;
            for (int i = 0; i < percentages.Count; i++)
            {
                var installmentAmount = (totalAmount * percentages[i]) / 100;
                var dueDate = today.AddMonths((i + 1) * 3); // 3 months gap between installments

                maxInstallmentId++;
                var installment = new PaymentInstallmentModel
                {
                    InstallmentId = maxInstallmentId,
                    PlanId = paymentPlan.PlanId,
                    InstallmentNumber = i + 1,
                    MilestoneName = milestones[i],
                    DueDate = dueDate,
                    Amount = installmentAmount,
                    // Booking confirmation does not imply payment completion.
                    PaidAmount = 0,
                    Status = "Pending",
                    PaidDate = null,
                    CreatedOn = IndianTime.Now
                };

                _db.PaymentInstallments.Add(installment);
            }

            await _db.SaveChangesAsync();
        }

        // Helper: Get Document Type from filename
        private string GetDocumentType(string fileName)
        {
            var lowerFileName = fileName.ToLower();
            if (lowerFileName.Contains("aadhar") || lowerFileName.Contains("aadhaar"))
                return "Aadhar";
            if (lowerFileName.Contains("pan"))
                return "PAN";
            if (lowerFileName.Contains("agreement"))
                return "Agreement";
            if (lowerFileName.Contains("cheque"))
                return "Cheque";
            return "Other";
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

        // AUTOMATED COMMISSION PROCESSING
        private async Task ProcessCommissionsForBooking(BookingModel booking)
        {
            try
            {
                var month = booking.BookingDate.ToString("MMMM");
                var year = booking.BookingDate.Year;

                // 1. PROCESS AGENT COMMISSION (if agent involved)
                var lead = await _db.Leads.FindAsync(booking.LeadId);
                
                if (lead?.ExecutiveId != null)
                {
                    // Find user by ExecutiveId, then find matching agent by email
                    var user = await _db.Users.FindAsync(lead.ExecutiveId.Value);
                    
                    if (user != null)
                    {
                        // Find agent by email match (most reliable)
                        var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Email == user.Email && a.Status == "Approved");
                        
                        if (agent != null)
                        {
                            await ProcessAgentCommission(agent, booking, month, year);
                        }
                    }
                }

                // 2. PROCESS CHANNEL PARTNER COMMISSION (if lead from partner)
                var partnerLead = await _db.PartnerLeads.FirstOrDefaultAsync(pl => pl.LeadId == booking.LeadId);
                if (partnerLead != null)
                {
                    var partner = await _db.ChannelPartners.FindAsync(partnerLead.PartnerId);
                    if (partner != null && partner.Status == "Approved")
                    {
                        await ProcessPartnerCommission(partner, booking, month, year);
                    }
                }

                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Log error but don't break booking process
                Console.WriteLine($"Commission processing error: {ex.Message}");
            }
        }

        private async Task ProcessAgentCommission(AgentModel agent, BookingModel booking, string month, int year)
        {
            // Only process commission for agents with commission structure
            if (agent.AgentType == "Salary") return;

            // Check if commission already exists
            var existingLog = await _db.AgentCommissionLogs
                .FirstOrDefaultAsync(c => c.AgentId == agent.AgentId && c.BookingId == booking.BookingId);
            if (existingLog != null) return;

            // Calculate commission using agent's actual percentage
            var commissionPercentage = GetCommissionPercentage(agent.CommissionRules);
            var commissionAmount = (booking.TotalAmount * commissionPercentage) / 100;


            // Create commission log
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
            _db.AgentCommissionLogs.Add(commissionLog);

            // Update or create agent payout
            await UpdateAgentPayout(agent.AgentId, month, year);
        }

        private async Task ProcessPartnerCommission(ChannelPartnerModel partner, BookingModel booking, string month, int year)
        {
            // Check if commission already exists
            var existingLog = await _db.ChannelPartnerCommissionLogs
                .FirstOrDefaultAsync(c => c.PartnerId == partner.PartnerId && c.BookingId == booking.BookingId);
            if (existingLog != null) return;

            // Get partner commission (percentage-based)
            var commissionPercentage = GetPartnerCommissionPercentage(partner);
            var commissionAmount = (booking.TotalAmount * commissionPercentage) / 100;

            // Create commission log
            var commissionLog = new ChannelPartnerCommissionLogModel
            {
                PartnerId = partner.PartnerId,
                BookingId = booking.BookingId,
                FixedCommissionAmount = commissionAmount,
                SaleDate = booking.BookingDate,
                Month = month,
                Year = year
            };
            _db.ChannelPartnerCommissionLogs.Add(commissionLog);

            // Update or create partner payout
            await UpdatePartnerPayout(partner.PartnerId, month, year);
        }

        private async Task UpdateAgentPayout(int agentId, string month, int year)
        {
            var existingPayout = await _db.AgentPayouts
                .FirstOrDefaultAsync(p => p.AgentId == agentId && p.Month == month && p.Year == year);

            var agent = await _db.Agents.FindAsync(agentId);
            var commissionLogs = await _db.AgentCommissionLogs
                .Where(c => c.AgentId == agentId && c.Month == month && c.Year == year)
                .ToListAsync();

            var totalCommission = commissionLogs.Sum(c => c.CommissionAmount);
            var totalSales = commissionLogs.Count;
            var baseSalary = agent?.Salary ?? 0;

            // Calculate attendance deduction (only for salary-based agents)
            var attendanceDeduction = agent?.AgentType == "Commission" ? 0 : await _payoutService.CalculateAttendanceDeduction(agentId, month, year);

            // Calculate final payout based on agent type
            var finalPayout = CalculateFinalPayout(agent?.AgentType, baseSalary, attendanceDeduction, totalCommission);

            if (existingPayout != null)
            {
                // Update existing payout
                existingPayout.CommissionAmount = totalCommission;
                existingPayout.TotalSales = totalSales;
                existingPayout.FinalPayout = finalPayout;
                existingPayout.AttendanceDeduction = attendanceDeduction;
                _db.AgentPayouts.Update(existingPayout);
            }
            else
            {
                // Create new payout
                var startDate = new DateTime(year, DateTime.ParseExact(month, "MMMM", null).Month, 1);
                var endDate = startDate.AddMonths(1).AddDays(-1);
                
                var payout = new AgentPayoutModel
                {
                    AgentId = agentId,
                    Month = month,
                    Year = year,
                    BaseSalary = baseSalary,
                    AttendanceDeduction = attendanceDeduction,
                    CommissionAmount = totalCommission,
                    FinalPayout = finalPayout,
                    TotalSales = totalSales,
                    WorkingDays = GetWorkingDaysInMonth(startDate, endDate),
                    PresentDays = await GetPresentDays(agentId, startDate, endDate),
                    Status = "Pending"
                };
                _db.AgentPayouts.Add(payout);
            }
        }

        private async Task UpdatePartnerPayout(int partnerId, string month, int year)
        {
            var existingPayout = await _db.PartnerPayouts
                .FirstOrDefaultAsync(p => p.PartnerId == partnerId && p.Month == month && p.Year == year);

            var commissionLogs = await _db.ChannelPartnerCommissionLogs
                .Where(c => c.PartnerId == partnerId && c.Month == month && c.Year == year)
                .ToListAsync();

            var totalCommission = commissionLogs.Sum(c => c.FixedCommissionAmount);
            var totalSales = commissionLogs.Count;

            if (existingPayout != null)
            {
                // Update existing payout
                existingPayout.TotalCommission = totalCommission;
                existingPayout.TotalSales = totalSales;
                existingPayout.ConvertedLeads = totalSales;
                existingPayout.Amount = totalCommission;
                _db.PartnerPayouts.Update(existingPayout);
            }
            else
            {
                // Create new payout
                var partner = await _db.ChannelPartners.FindAsync(partnerId);
                var commissionPercentage = GetPartnerCommissionPercentage(partner);
                
                var payout = new PartnerPayoutModel
                {
                    PartnerId = partnerId,
                    Month = month,
                    Year = year,
                    FixedCommissionPerSale = commissionPercentage,
                    TotalSales = totalSales,
                    TotalCommission = totalCommission,
                    Amount = totalCommission,
                    ConvertedLeads = totalSales,
                    Status = "Pending"
                };
                _db.PartnerPayouts.Add(payout);
            }
        }

        private decimal CalculateFinalPayout(string? agentType, decimal baseSalary, decimal deduction, decimal commission)
        {
            return agentType?.ToLower() switch
            {
                "salary" => baseSalary - deduction,
                "hybrid" => (baseSalary - deduction) + commission,
                "commission" => commission,
                _ => commission
            };
        }

        private decimal GetCommissionPercentage(string? commissionRules)
        {
            if (string.IsNullOrEmpty(commissionRules)) return 0;
            
            // Handle "20% of sale" or "10%" formats
            var commissionText = commissionRules.Replace("% of sale", "").Replace("%", "").Trim();
            return decimal.TryParse(commissionText, out decimal percentage) ? percentage : 0;
        }

        private decimal GetPartnerCommissionPercentage(ChannelPartnerModel? partner)
        {
            // Preferred source: the numeric CommissionPercentage field (e.g. 5 for 5%).
            if (partner != null && partner.CommissionPercentage > 0)
                return partner.CommissionPercentage;

            // Fallback: parse a human-readable scheme such as "5% of sale" or "2% of sale".
            var commissionScheme = partner?.CommissionScheme;
            if (string.IsNullOrEmpty(commissionScheme)) return 0;
            var commissionText = commissionScheme.Replace("%", "").Trim().Split(' ')[0];
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
            return await _db.AgentAttendance
                .Where(a => a.AgentId == agentId && 
                           a.Date >= startDate && 
                           a.Date <= endDate && 
                           a.Status == "Present")
                .CountAsync();
        }

        // GET: Bookings/DownloadPdf/5
        public IActionResult DownloadPdf(int id)
        {
            var booking = _db.Bookings.FirstOrDefault(b => b.BookingId == id);

            if (booking == null)
                return NotFound();

            // Populate navigation properties manually (.Include is a no-op on MongoDbSet)
            booking.Lead = _db.Leads.FirstOrDefault(l => l.LeadId == booking.LeadId);
            booking.Property = _db.Properties.FirstOrDefault(p => p.PropertyId == booking.PropertyId);
            booking.Flat = _db.PropertyFlats.FirstOrDefault(f => f.FlatId == booking.FlatId);
            if (booking.QuotationId.HasValue)
                booking.Quotation = _db.Quotations.FirstOrDefault(q => q.QuotationId == booking.QuotationId.Value);

            var documents = _db.BookingDocuments.Where(d => d.BookingId == id).ToList();
            var paymentPlan = _db.PaymentPlans.FirstOrDefault(p => p.BookingId == id);
            var installments = paymentPlan != null ? _db.PaymentInstallments.Where(i => i.PlanId == paymentPlan.PlanId).OrderBy(i => i.InstallmentNumber).ToList() : new List<CRM.Models.PaymentInstallmentModel>();

            var flatAreaText = booking.Flat?.AreaSqft.HasValue == true
                ? booking.Flat.AreaSqft.Value.ToString("N0")
                : (!string.IsNullOrWhiteSpace(booking.Flat?.Area) ? booking.Flat!.Area! : "N/A");
            var floorText = !string.IsNullOrWhiteSpace(booking.Flat?.FloorNumber)
                ? booking.Flat!.FloorNumber!
                : (!string.IsNullOrWhiteSpace(booking.Flat?.FloorName) ? booking.Flat!.FloorName! : "N/A");

            var milestonePaid = paymentPlan?.PaidAmount ?? 0m;
            var displayedOutstanding = Math.Max(booking.TotalAmount - booking.BookingAmount - milestonePaid, 0m);

            // Company info
            var companyName = SettingsController.GetSettingValue(_db, "CompanyName") ?? "Company";
            var companyAddress = SettingsController.GetSettingValue(_db, "CompanyAddress") ?? "Address";
            var companyPhone = SettingsController.GetSettingValue(_db, "CompanyPhone") ?? "Phone";
            var companyEmail = SettingsController.GetSettingValue(_db, "CompanyEmail") ?? "Email";
            var companyGst = SettingsController.GetSettingValue(_db, "CompanyGST") ?? "GST";

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4, 36, 36, 36, 36);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 18);
                var labelFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
                var valueFont = FontFactory.GetFont(FontFactory.HELVETICA, 12);
                var sectionFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14);

                // Company Header
                doc.Add(new Paragraph(companyName, titleFont));
                doc.Add(new Paragraph(companyAddress, valueFont));
                doc.Add(new Paragraph($"Phone: {companyPhone} | Email: {companyEmail}", valueFont));
                doc.Add(new Paragraph($"GST: {companyGst}", valueFont));
                doc.Add(new Paragraph(" "));

                // Booking Number and Status
                doc.Add(new Paragraph($"BOOKING CONFIRMATION: {booking.BookingNumber}", sectionFont));
                doc.Add(new Paragraph($"Status: {booking.Status}", labelFont));
                doc.Add(new Paragraph(" "));

                // Customer Details
                doc.Add(new Paragraph("Customer Details", sectionFont));
                doc.Add(new Paragraph($"Name: {booking.Lead?.Name}", valueFont));
                doc.Add(new Paragraph($"Contact: {booking.Lead?.Contact}", valueFont));
                doc.Add(new Paragraph($"Email: {booking.Lead?.Email}", valueFont));
                doc.Add(new Paragraph(" "));

                // Booking Details
                doc.Add(new Paragraph("Booking Details", sectionFont));
                doc.Add(new Paragraph($"Booking Date: {booking.BookingDate:dd MMM yyyy}", valueFont));
                if (booking.AgreementDate.HasValue)
                    doc.Add(new Paragraph($"Agreement Date: {booking.AgreementDate:dd MMM yyyy}", valueFont));
                if (booking.PossessionDate.HasValue)
                    doc.Add(new Paragraph($"Possession Date: {booking.PossessionDate:dd MMM yyyy}", valueFont));
                doc.Add(new Paragraph(" "));

                // Property Details
                doc.Add(new Paragraph("Property Details", sectionFont));
                doc.Add(new Paragraph($"Project: {booking.Property?.PropertyName}", valueFont));
                doc.Add(new Paragraph($"Location: {booking.Property?.Location}", valueFont));
                doc.Add(new Paragraph($"Developer: {booking.Property?.Developer}", valueFont));
                doc.Add(new Paragraph($"Flat: {booking.Flat?.FlatName}", valueFont));
                doc.Add(new Paragraph($"Type: {booking.Flat?.BHK}", valueFont));
                doc.Add(new Paragraph($"Area: {flatAreaText} sq.ft", valueFont));
                doc.Add(new Paragraph($"Floor: {floorText}", valueFont));
                doc.Add(new Paragraph(" "));

                // Payment Details
                doc.Add(new Paragraph("Payment Information", sectionFont));
                doc.Add(new Paragraph($"Total Property Value: â‚¹{booking.TotalAmount:N2}", valueFont));
                doc.Add(new Paragraph($"Booking Amount Paid: â‚¹{booking.BookingAmount:N2}", valueFont));
                doc.Add(new Paragraph($"Payment Type: {booking.PaymentType}", valueFont));
                if (paymentPlan != null)
                {
                    doc.Add(new Paragraph($"Outstanding: â‚¹{displayedOutstanding:N2}", valueFont));
                    doc.Add(new Paragraph($"Milestone Paid So Far: â‚¹{milestonePaid:N2}", valueFont));
                    doc.Add(new Paragraph($"Plan Type: {paymentPlan.PlanType}", valueFont));
                }
                else if (booking.PaymentType == "EMI")
                {
                    var emiOutstandingWithoutPlan = Math.Max(booking.TotalAmount - booking.BookingAmount, 0m);
                    doc.Add(new Paragraph($"Outstanding: â‚¹{emiOutstandingWithoutPlan:N2}", valueFont));
                }
                doc.Add(new Paragraph(" "));

                // EMI Payment Schedule
                if (booking.PaymentType == "EMI" && installments.Any())
                {
                    doc.Add(new Paragraph("EMI Payment Schedule", sectionFont));
                    PdfPTable emiTable = new PdfPTable(6) { WidthPercentage = 100 };
                    emiTable.SetWidths(new float[] { 1, 2, 2, 2, 2, 2 });
                    emiTable.AddCell(new PdfPCell(new Phrase("#", labelFont)));
                    emiTable.AddCell(new PdfPCell(new Phrase("Milestone", labelFont)));
                    emiTable.AddCell(new PdfPCell(new Phrase("Due Date", labelFont)));
                    emiTable.AddCell(new PdfPCell(new Phrase("Amount", labelFont)));
                    emiTable.AddCell(new PdfPCell(new Phrase("Paid", labelFont)));
                    emiTable.AddCell(new PdfPCell(new Phrase("Status", labelFont)));
                    foreach (var inst in installments)
                    {
                        emiTable.AddCell(new PdfPCell(new Phrase(inst.InstallmentNumber.ToString(), valueFont)));
                        emiTable.AddCell(new PdfPCell(new Phrase(inst.MilestoneName, valueFont)));
                        emiTable.AddCell(new PdfPCell(new Phrase(inst.DueDate.ToString("dd MMM yyyy"), valueFont)));
                        emiTable.AddCell(new PdfPCell(new Phrase($"â‚¹{inst.Amount:N2}", valueFont)));
                        emiTable.AddCell(new PdfPCell(new Phrase($"â‚¹{inst.PaidAmount:N2}", valueFont)));
                        emiTable.AddCell(new PdfPCell(new Phrase(inst.Status, valueFont)));
                    }
                    doc.Add(emiTable);
                    doc.Add(new Paragraph(" "));
                }

                // Uploaded Documents
                if (documents.Any())
                {
                    doc.Add(new Paragraph("Uploaded Documents", sectionFont));
                    foreach (var d in documents)
                    {
                        doc.Add(new Paragraph($"{d.DocumentType}: {d.DocumentName} ({d.UploadedOn:dd MMM yyyy})", valueFont));
                    }
                    doc.Add(new Paragraph(" "));
                }

                // Notes
                if (!string.IsNullOrEmpty(booking.Notes))
                {
                    doc.Add(new Paragraph("Notes:", sectionFont));
                    doc.Add(new Paragraph(booking.Notes, valueFont));
                    doc.Add(new Paragraph(" "));
                }

                // Terms & Conditions
                doc.Add(new Paragraph("Terms & Conditions:", sectionFont));
                var terms = new[] {
                    "This booking is subject to the terms and conditions as per the agreement.",
                    "All payments should be made as per the payment schedule.",
                    "Property specifications are subject to change as per approved plans.",
                    "Possession date is tentative and subject to completion of construction.",
                    "All payments are non-refundable unless specified otherwise in the agreement."
                };
                foreach (var t in terms)
                    doc.Add(new Paragraph(t, valueFont));
                doc.Add(new Paragraph(" "));

                // Footer
                doc.Add(new Paragraph($"Thank you for choosing {companyName}", valueFont));
                doc.Add(new Paragraph($"For any queries, please contact us at {companyPhone} or {companyEmail}", valueFont));

                doc.Close();
                return File(ms.ToArray(), "application/pdf", $"Booking_{booking.BookingNumber}.pdf");
            }
        }

        // P0-B2: Booking Cancellation with Refund Calculation
        [HttpPost]
        [PermissionAuthorize("Delete")]
        public async Task<IActionResult> CancelBooking(int bookingId, string cancellationReason)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var booking = await _db.Bookings.FindAsync(bookingId);
                if (booking == null)
                {
                    return Json(new { success = false, message = "Booking not found" });
                }

                if (booking.Status == "Cancelled")
                {
                    return Json(new { success = false, message = "Booking is already cancelled" });
                }

                // Calculate total paid amount
                var totalPaid = _db.Payments
                    .Where(p => p.BookingId == bookingId)
                    .Sum(p => p.Amount);

                // Calculate refund based on cancellation policy
                // Example: 80% refund if cancelled before 30 days, 50% before 15 days, 0% after
                var daysToBooking = (booking.BookingDate - IndianTime.Now).Days;
                decimal refundPercentage = 0;
                string refundPolicy = "";

                if (daysToBooking > 30)
                {
                    refundPercentage = 0.80m; // 80% refund
                    refundPolicy = "80% refund (>30 days before booking)";
                }
                else if (daysToBooking > 15)
                {
                    refundPercentage = 0.50m; // 50% refund
                    refundPolicy = "50% refund (15-30 days before booking)";
                }
                else
                {
                    refundPercentage = 0; // No refund
                    refundPolicy = "No refund (<15 days before booking)";
                }

                var refundAmount = totalPaid * refundPercentage;

                // Update booking status
                booking.Status = "Cancelled";
                booking.Notes = $"{booking.Notes}\n\n[CANCELLED] Reason: {cancellationReason}\nCancellation Date: {IndianTime.Now:yyyy-MM-dd}\nTotal Paid: ₹{totalPaid:N2}\nRefund Policy: {refundPolicy}\nRefund Amount: ₹{refundAmount:N2}";
                _db.Bookings.Update(booking);

                // Free up the flat
                var flat = await _db.PropertyFlats.FindAsync(booking.FlatId);
                if (flat != null)
                {
                    flat.FlatStatus = "Available";
                    flat.IsActive = true;
                    _db.PropertyFlats.Update(flat);
                }

                // Create refund payment record if applicable
                if (refundAmount > 0)
                {
                    var refundPayment = new PaymentModel
                    {
                        BookingId = bookingId,
                        InvoiceId = 0, // No invoice for refunds
                        ReceiptNumber = $"REFUND_{bookingId}_{IndianTime.Now:yyyyMMddHHmmss}",
                        Amount = -refundAmount, // Negative for refund
                        PaymentDate = IndianTime.Now,
                        PaymentMethod = "Refund",
                        TransactionReference = $"Booking Cancellation - {cancellationReason}",
                        Notes = $"Refund for cancelled booking. {refundPolicy}"
                    };
                    _db.Payments.Add(refundPayment);
                }

                // Update lead status back to "Interested"
                var lead = await _db.Leads.FindAsync(booking.LeadId);
                if (lead != null)
                {
                    lead.Stage = "Interested";
                    lead.Status = "Active";
                    _db.Leads.Update(lead);
                }

                // Create booking amendment record
                var amendment = new BookingAmendmentModel
                {
                    BookingId = bookingId,
                    AmendmentType = "Cancellation",
                    PreviousValue = booking.Status,
                    NewValue = "Cancelled",
                    Reason = cancellationReason,
                    AmendedBy = int.Parse(User?.FindFirst("UserId")?.Value ?? "0"),
                    AmendedOn = IndianTime.Now,
                    Status = "Approved" // Auto-approve cancellations
                };
                _db.BookingAmendments.Add(amendment);

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new
                {
                    success = true,
                    message = $"Booking cancelled successfully. Refund amount: ₹{refundAmount:N2}",
                    refundAmount = refundAmount,
                    refundPolicy = refundPolicy
                });
            }
            catch (Exception ex)
            {

                return Json(new { success = false, message = $"Error cancelling booking: {ex.Message}" });
            }
        }
    }
}


