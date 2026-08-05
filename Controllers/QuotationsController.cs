using CRM.Helpers;
      
using Microsoft.AspNetCore.Mvc;
using CRM.Attributes;
using CRM.Models;
using Microsoft.AspNetCore.Authorization;
using iTextSharp.text.pdf;
using iTextSharp.text;
using Newtonsoft.Json;

namespace CRM.Controllers
{
    [Authorize]
    public class QuotationsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<QuotationsController> _logger;

        public QuotationsController(AppDbContext db, IHttpContextAccessor httpContextAccessor, ILogger<QuotationsController> logger)
        {
            _db = db;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        // GET: Quotations
        [PermissionAuthorize("View")]        public IActionResult Index(string search = "", string status = "")

        {
            var role = User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid, out int userId);

            var quotations = _db.Quotations.AsQueryable();
            
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;

            if (role?.ToLower() == "partner")
            {
                // Partners see quotations for their leads only
                var partnerLeadIds = _db.Leads.Where(l => l.ChannelPartnerId == channelPartnerId).Select(l => l.LeadId).ToList();
                quotations = quotations.Where(q => partnerLeadIds.Contains(q.LeadId));
            }
            else if (role?.ToLower() == "admin")
            {
                // Admin sees their own quotations + partner quotations for handed over leads
                var adminLeadIds = _db.Leads.Where(l => l.ChannelPartnerId == null || l.HandoverStatus == "ReadyToBook" || l.HandoverStatus == "HandedOver").Select(l => l.LeadId).ToList();
                quotations = quotations.Where(q => q.ChannelPartnerId == null || adminLeadIds.Contains(q.LeadId));
            }
            else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
            {
                var myLeadIds = _db.Leads.Where(l => l.ExecutiveId == userId).Select(l => l.LeadId).ToList();
                quotations = quotations.Where(q => myLeadIds.Contains(q.LeadId));
            }

            // Apply filters
            if (!string.IsNullOrEmpty(search))
            {
                quotations = quotations.Where(q => 
                    q.QuotationNumber.Contains(search) ||
                    (q.Notes ?? "").Contains(search));
            }

            if (!string.IsNullOrEmpty(status))
            {
                quotations = quotations.Where(q => q.Status == status);
            }

            var result = quotations
                .OrderByDescending(q => q.CreatedOn)
                .ToList();

            // P0-Q1: Check quotation expiry and mark expired ones
            var today = IndianTime.Today;
            var expiredQuotations = result.Where(q => q.ValidUntil.HasValue && q.ValidUntil.Value < today && q.Status == "Pending").ToList();
            foreach (var expired in expiredQuotations)
            {
                expired.Status = "Expired";
                _db.Quotations.Update(expired);
            }
            if (expiredQuotations.Any())
            {
                _db.SaveChanges();
                TempData["Info"] = $"{expiredQuotations.Count} quotation(s) automatically marked as expired.";
            }

            // Get related data for display
            var leadIds = result.Select(q => q.LeadId).Distinct().ToList();
            var propertyIds = result.Select(q => q.PropertyId).Distinct().ToList();
            var flatIds = result.Where(q => q.FlatId.HasValue).Select(q => q.FlatId!.Value).Distinct().ToList();

            ViewBag.Leads = _db.Leads.Where(l => leadIds.Contains(l.LeadId)).ToList();
            ViewBag.Properties = _db.Properties.Where(p => propertyIds.Contains(p.PropertyId)).ToList();
            ViewBag.Flats = _db.PropertyFlats.Where(f => flatIds.Contains(f.FlatId)).ToList();
            ViewBag.Bookings = _db.Bookings.Where(b => flatIds.Contains(b.FlatId) && b.Status != "Cancelled").ToList();

            ViewBag.SearchTerm = search;
            ViewBag.StatusFilter = status;
            
            // Add user info for view-level access control
            ViewBag.IsPartnerTeam = currentUser?.ChannelPartnerId != null;

            return View(result);
        }

        // GET: Quotations/Create
        public IActionResult Create()
        {
            var role = User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid, out int userId);
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
            
            // Partners and their team members cannot create quotations
            if (role?.ToLower() == "partner" || currentUser?.ChannelPartnerId != null)
            {
                return RedirectToAction("Index");
            }

            // Filter leads based on role
            if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
            {
                ViewBag.Leads = _db.Leads.Where(l => l.ExecutiveId == userId).OrderBy(l => l.Name).ToList();
            }
            else
            {
                ViewBag.Leads = _db.Leads.OrderBy(l => l.Name).ToList();
            }
            ViewBag.Properties = _db.Properties.OrderBy(p => p.PropertyName).ToList();
            
            // Get GST rate from settings
            var gstRate = SettingsController.GetSettingValueDecimal(_db, "GSTRate", 5);
            ViewBag.GSTRate = gstRate;

            return View(new QuotationModel());
        }

        // POST: Quotations/Create
        [HttpPost]
        public IActionResult Create(QuotationModel model, List<QuotationItemModel> items)
        {
            // Prevent duplicate submissions by checking if quotation already exists for this lead/property combination
            var existingQuotation = _db.Quotations
                .Where(q => q.LeadId == model.LeadId && q.PropertyId == model.PropertyId && q.CreatedOn > IndianTime.Now.AddMinutes(-1))
                .FirstOrDefault();
            
            if (existingQuotation != null)
            {
                return Json(new { success = true, quotationId = existingQuotation.QuotationId, message = "Quotation already exists" });
            }
            
            try
            {
                // Validate required fields
                if (string.IsNullOrWhiteSpace(model.QuotationNumber))
                {
                    // Generate quotation number
                    var prefix = SettingsController.GetSettingValue(_db, "QuotationPrefix", "QT");
                    var year = IndianTime.Now.Year;
                    var lastQuotation = _db.Quotations
                        .Where(q => q.QuotationNumber.StartsWith($"{prefix}-{year}"))
                        .OrderByDescending(q => q.QuotationId)
                        .FirstOrDefault();

                    int nextNumber = 1;
                    if (lastQuotation != null)
                    {
                        var lastNumberStr = lastQuotation.QuotationNumber.Split('-').Last();
                        if (int.TryParse(lastNumberStr, out int lastNum))
                        {
                            nextNumber = lastNum + 1;
                        }
                    }
                    model.QuotationNumber = $"{prefix}-{year}-{nextNumber:D4}";
                }

                model.CreatedBy = _getCurrentUserId();
                model.CreatedOn = IndianTime.Now;
                model.Status = "Draft";
                
                var role = User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
                var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(uid, out int userId);
                var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
                model.ChannelPartnerId = currentUser?.ChannelPartnerId;

                // Validate model required fields
                if (model.LeadId == 0 || model.PropertyId == 0 || model.BasePrice <= 0)
                {
                    return Json(new { success = false, message = "Missing required fields: Lead, Property, or Base Price." });
                }
                if (items == null || items.Count == 0)
                {
                    return Json(new { success = false, message = "Please add at least one item." });
                }
                foreach (var item in items)
                {
                    if (item == null ||
                        string.IsNullOrWhiteSpace(item.ItemType) ||
                        string.IsNullOrWhiteSpace(item.Description) ||
                        item.Quantity <= 0 ||
                        item.Amount <= 0 ||
                        item.Total <= 0)
                    {
                        return Json(new { success = false, message = "All items must have valid type, description, quantity, amount, and total." });
                    }
                }

                // Calculate totals
                decimal subtotal = items.Sum(i => i.Total);
                if (subtotal <= 0)
                {
                    return Json(new { success = false, message = "Subtotal must be greater than 0." });
                }

                if (model.DiscountAmount < 0)
                {
                    return Json(new { success = false, message = "Discount cannot be negative." });
                }

                if (model.DiscountAmount > subtotal)
                {
                    return Json(new { success = false, message = "Discount cannot exceed subtotal." });
                }

                var discountPercent = subtotal > 0 ? (model.DiscountAmount / subtotal) * 100 : 0;
                if (discountPercent > 100)
                {
                    return Json(new { success = false, message = "Discount percent cannot be more than 100." });
                }
                model.TotalAmount = subtotal;
                
                // Calculate tax
                var gstRate = SettingsController.GetSettingValueDecimal(_db, "GSTRate", 5);
                model.TaxAmount = (subtotal - model.DiscountAmount) * (gstRate / 100);
                model.GrandTotal = (subtotal - model.DiscountAmount) + model.TaxAmount;

                _db.Quotations.Add(model);
                _db.SaveChanges();

                // Update lead stage to "Quotation"
                var lead = _db.Leads.FirstOrDefault(l => l.LeadId == model.LeadId);
                if (lead != null)
                {
                    lead.Stage = "Quotation";
                    lead.Status = "Active";
                    _db.Leads.Update(lead);
                    _db.SaveChanges();
                }
                // Add items without duplicates
                foreach (var item in items.GroupBy(i => new { i.ItemType, i.Description, i.Amount }).Select(g => g.First()))
                {
                    item.QuotationId = model.QuotationId;
                    _db.QuotationItems.Add(item);
                }
                //_db.SaveChanges();

                

                return Json(new { success = true, quotationId = model.QuotationId, message = "Quotation created successfully", encodedId = IdObfuscator.Encode(model.QuotationId) });
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return Json(new { success = false, message = $"Error: {innerMsg}" });
            }
        }

        // GET: Quotations/Details/5
        [Route("quotationdetails/{id}")]
        public IActionResult Details(string id, bool autoPrint = false)
        {
            var decodedId = IdObfuscator.Decode(id);
            if (decodedId == null)
            {
                return NotFound();
            }
            ViewBag.EncodedId = id;
            var quotation = _db.Quotations.FirstOrDefault(q => q.QuotationId == decodedId.Value);
            if (quotation == null)
            {
                return NotFound();
            }

            // Get related data
            var lead = _db.Leads.FirstOrDefault(l => l.LeadId == quotation.LeadId);
            var property = _db.Properties.FirstOrDefault(p => p.PropertyId == quotation.PropertyId);
            var flat = quotation.FlatId.HasValue ? _db.PropertyFlats.FirstOrDefault(f => f.FlatId == quotation.FlatId) : null;
            var items = _db.QuotationItems.Where(i => i.QuotationId == decodedId.Value).ToList();

            // Get company settings
            ViewBag.CompanyName = SettingsController.GetSettingValue(_db, "CompanyName");
            ViewBag.CompanyGST = SettingsController.GetSettingValue(_db, "CompanyGST");
            ViewBag.CompanyAddress = SettingsController.GetSettingValue(_db, "CompanyAddress");
            ViewBag.CompanyPhone = SettingsController.GetSettingValue(_db, "CompanyPhone");
            ViewBag.CompanyEmail = SettingsController.GetSettingValue(_db, "CompanyEmail");

            ViewBag.Lead = lead;
            ViewBag.Property = property;
            ViewBag.Flat = flat;
            ViewBag.Items = items;
            ViewBag.AutoPrint = autoPrint;

            return View(quotation);
        }

        // GET: Get floors by property
        [HttpGet]
        public IActionResult GetFloorsByProperty(int propertyId)
        {
            var floorRows = _db.PropertyFlats
                .Where(f => f.PropertyId == propertyId &&
                           (!string.IsNullOrEmpty(f.FloorNumber) || !string.IsNullOrEmpty(f.FloorName)))
                .Select(f => new
                {
                    floorNumber = f.FloorNumber,
                    floorName = f.FloorName
                })
                .ToList();

            var floors = floorRows
                .Select(f => new
                {
                    floorNumber = !string.IsNullOrWhiteSpace(f.floorNumber) ? f.floorNumber!.Trim() : (f.floorName ?? string.Empty).Trim(),
                    floorName = !string.IsNullOrWhiteSpace(f.floorName) ? f.floorName!.Trim() : $"Floor {(f.floorNumber ?? string.Empty).Trim()}"
                })
                .Where(f => !string.IsNullOrWhiteSpace(f.floorNumber))
                .Distinct()
                .OrderBy(f => int.TryParse(f.floorNumber, out int n) ? n : int.MaxValue)
                .ThenBy(f => f.floorNumber)
                .Select((f, index) => new
                {
                    floorId = index + 1,
                    floorNumber = f.floorNumber,
                    floorName = f.floorName
                })
                .ToList();

            return Json(new { success = true, floors });
        }

        // GET: Get flats by property and floor
        [HttpGet]
        public IActionResult GetFlatsByProperty(int propertyId, string? floorNumber = null, int? selectedFlatId = null)
        {
            var query = _db.PropertyFlats
                .Where(f => f.PropertyId == propertyId && 
                       ((f.FlatStatus != "Booked" || f.FlatStatus == "Available")
                        || (selectedFlatId.HasValue && f.FlatId == selectedFlatId.Value)));

            // Filter by floor if provided
            if (!string.IsNullOrEmpty(floorNumber))
            {
                query = query.Where(f => f.FloorNumber == floorNumber || f.FloorName == floorNumber);
            }

            var flats = query
                .OrderBy(f => f.FlatName)
                .Select(f => new {
                    flatId = f.FlatId,
                    flatName = f.FlatName,
                    bhk = f.BHK,
                    area = f.Area ?? f.AreaSqft.ToString(),
                    floorNumber = f.FloorNumber,
                    price = f.Price ?? 0,
                    status = f.Status ?? f.FlatStatus ?? "Available"
                })
                .ToList();

            return Json(new { success = true, flats });
        }

        //// POST: Update status
        //[HttpPost]
        //public IActionResult UpdateStatus(int quotationId, string status)
        //{
        //    try
        //    {
        //        var quotation = _db.Quotations.FirstOrDefault(q => q.QuotationId == quotationId);
        //        if (quotation == null)
        //        {
        //            return RedirectToAction("Index");
        //        }

        //        quotation.Status = status;
        //        quotation.ModifiedOn = IndianTime.Now;
        //        _db.SaveChanges();

        //        return RedirectToAction("Index");
        //    }
        //    catch (Exception ex)
        //    {
        //        return RedirectToAction("Index");
        //    }
        //}
        [HttpPost]
        public IActionResult UpdateStatus(int quotationId, string status)
        {
            try
            {
                var quotation = _db.Quotations.FirstOrDefault(q => q.QuotationId == quotationId);
                if (quotation == null)
                {
                    return Json(new { success = false, message = "Quotation not found" });
                }

                // If accepting, reject all other accepted quotations for same lead/property
                if (status == "Accepted")
                {
                    var otherAcceptedQuotations = _db.Quotations.Where(q =>
                        q.QuotationId != quotation.QuotationId &&
                        q.LeadId == quotation.LeadId &&
                        q.PropertyId == quotation.PropertyId &&
                        q.Status == "Accepted").ToList();
                    foreach (var q in otherAcceptedQuotations)
                    {
                        q.Status = "Rejected";
                        _db.Quotations.Update(q);

                    }
                    _db.SaveChanges(); // <-- Add this to save the rejected statuses
                }

                quotation.Status = status;
                quotation.ModifiedOn = IndianTime.Now;
                _db.SaveChanges();

                // Update lead stage based on quotation status
                var lead = _db.Leads.FirstOrDefault(l => l.LeadId == quotation.LeadId);
                if (lead != null)
                {
                    switch (status)
                    {
                        case "Sent":
                            lead.Stage = "Quotation Sent";
                            lead.Status = "Active";
                            break;
                        case "Accepted":
                            lead.Stage = "Quotation Accepted";
                            lead.Status = "Hot";
                            break;
                        case "Rejected":
                            lead.Stage = "Quotation Rejected";
                            lead.Status = "Cold";
                            break;
                    }
                    _db.Leads.Update(lead);
                    _db.SaveChanges();
                }

                return Json(new { success = true, message = "Status updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }
        // DELETE: Quotations/Delete/5
        [HttpPost]
        public IActionResult Delete(int id)
        {
            try
            {
                var quotation = _db.Quotations.FirstOrDefault(q => q.QuotationId == id);
                if (quotation == null)
                {
                    return Json(new { success = false, message = "Quotation not found" });
                }

                // Check if quotation is linked to a booking
                var booking = _db.Bookings.FirstOrDefault(b => b.QuotationId == id);
                if (booking != null)
                {
                    return Json(new { success = false, message = "Cannot delete quotation linked to a booking" });
                }

                _db.Quotations.Remove(quotation);
                _db.SaveChanges();

                return Json(new { success = true, message = "Quotation deleted successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // Helper method to get current user ID
        private int? _getCurrentUserId()
        {
            try
            {
                string? token = _httpContextAccessor.HttpContext?.Request.Cookies["jwtToken"];
                if (string.IsNullOrEmpty(token)) return null;

                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(token);

                var userIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == "UserId" || c.Type == "sub");
                if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
                {
                    return userId;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
        //public IActionResult DownloadPdf(int id)
        //{
        //    var quotation = _db.Quotations.FirstOrDefault(q => q.QuotationId == id);
        //    if (quotation == null)
        //        return NotFound();
        //    var items = _db.QuotationItems.Where(i => i.QuotationId == id).ToList();

        //    using (var ms = new MemoryStream())
        //    {
        //        var doc = new Document(PageSize.A4, 36, 36, 36, 36);
        //        PdfWriter.GetInstance(doc, ms);
        //        doc.Open();

        //        // Colors and fonts
        //        var headerColor = new BaseColor(102, 126, 234); // #667eea
        //        var sectionColor = new BaseColor(118, 75, 162); // #764ba2
        //        var tableHeaderColor = new BaseColor(230, 230, 250); // Lavender
        //        var tableBorderColor = new BaseColor(180, 180, 180);

        //        var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 18, headerColor);
        //        var labelFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12, sectionColor);
        //        var valueFont = FontFactory.GetFont(FontFactory.HELVETICA, 12, BaseColor.BLACK);

        //        // Header
        //        var header = new Paragraph($"Quotation #{quotation.QuotationNumber}", titleFont);
        //        header.Alignment = Element.ALIGN_CENTER;
        //        doc.Add(header);
        //        doc.Add(new Paragraph(" ")); // Spacer

        //        // Details section
        //        var detailsTable = new PdfPTable(2) { WidthPercentage = 80, HorizontalAlignment = Element.ALIGN_CENTER };
        //        detailsTable.DefaultCell.Border = Rectangle.NO_BORDER;
        //        detailsTable.AddCell(new Phrase("Date:", labelFont));
        //        detailsTable.AddCell(new Phrase($"{quotation.QuotationDate:dd-MM-yyyy}", valueFont));
        //        detailsTable.AddCell(new Phrase("Lead ID:", labelFont));
        //        detailsTable.AddCell(new Phrase($"{quotation.LeadId}", valueFont));
        //        detailsTable.AddCell(new Phrase("Property ID:", labelFont));
        //        detailsTable.AddCell(new Phrase($"{quotation.PropertyId}", valueFont));
        //        if (quotation.FlatId.HasValue)
        //        {
        //            detailsTable.AddCell(new Phrase("Flat ID:", labelFont));
        //            detailsTable.AddCell(new Phrase($"{quotation.FlatId}", valueFont));
        //        }
        //        detailsTable.AddCell(new Phrase("Base Price:", labelFont));
        //        detailsTable.AddCell(new Phrase($"â‚¹{quotation.BasePrice:N2}", valueFont));
        //        detailsTable.AddCell(new Phrase("Discount:", labelFont));
        //        detailsTable.AddCell(new Phrase($"â‚¹{quotation.DiscountAmount:N2}", valueFont));
        //        detailsTable.AddCell(new Phrase("Tax:", labelFont));
        //        detailsTable.AddCell(new Phrase($"â‚¹{quotation.TaxAmount:N2}", valueFont));
        //        detailsTable.AddCell(new Phrase("Grand Total:", labelFont));
        //        detailsTable.AddCell(new Phrase($"â‚¹{quotation.GrandTotal:N2}", valueFont));
        //        detailsTable.AddCell(new Phrase("Status:", labelFont));
        //        detailsTable.AddCell(new Phrase($"{quotation.Status}", valueFont));
        //        detailsTable.AddCell(new Phrase("Notes:", labelFont));
        //        detailsTable.AddCell(new Phrase($"{quotation.Notes}", valueFont));
        //        doc.Add(detailsTable);
        //        doc.Add(new Paragraph(" ")); // Spacer

        //        // Items Table
        //        var table = new PdfPTable(6) { WidthPercentage = 100 };
        //        table.SetWidths(new float[] { 1, 2, 4, 2, 2, 2 });

        //        // Table header with color
        //        string[] headers = { "#", "Type", "Description", "Qty", "Amount", "Total" };
        //        foreach (var h in headers)
        //        {
        //            var cell = new PdfPCell(new Phrase(h, labelFont))
        //            {
        //                BackgroundColor = tableHeaderColor,
        //                BorderColor = tableBorderColor,
        //                HorizontalAlignment = Element.ALIGN_CENTER,
        //                Padding = 5
        //            };
        //            table.AddCell(cell);
        //        }

        //        int idx = 1;
        //        foreach (var item in items)
        //        {
        //            table.AddCell(new PdfPCell(new Phrase(idx.ToString(), valueFont)) { Padding = 5, BorderColor = tableBorderColor });
        //            table.AddCell(new PdfPCell(new Phrase(item.ItemType, valueFont)) { Padding = 5, BorderColor = tableBorderColor });
        //            table.AddCell(new PdfPCell(new Phrase(item.Description, valueFont)) { Padding = 5, BorderColor = tableBorderColor });
        //            table.AddCell(new PdfPCell(new Phrase(item.Quantity.ToString(), valueFont)) { Padding = 5, BorderColor = tableBorderColor });
        //            table.AddCell(new PdfPCell(new Phrase($"â‚¹{item.Amount:N2}", valueFont)) { Padding = 5, BorderColor = tableBorderColor });
        //            table.AddCell(new PdfPCell(new Phrase($"â‚¹{item.Total:N2}", valueFont)) { Padding = 5, BorderColor = tableBorderColor });
        //            idx++;
        //        }
        //        doc.Add(table);

        //        doc.Close();
        //        return File(ms.ToArray(), "application/pdf", $"Quotation_{id}.pdf");
        //    }
        //}
        public IActionResult DownloadPdf(int id)
        {
            var quotation = _db.Quotations.FirstOrDefault(q => q.QuotationId == id);
            if (quotation == null)
                return NotFound();
            var items = _db.QuotationItems.Where(i => i.QuotationId == id).ToList();
            var lead = _db.Leads.FirstOrDefault(l => l.LeadId == quotation.LeadId);
            var property = _db.Properties.FirstOrDefault(p => p.PropertyId == quotation.PropertyId);
            var flat = quotation.FlatId.HasValue ? _db.PropertyFlats.FirstOrDefault(f => f.FlatId == quotation.FlatId) : null;

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4, 36, 36, 36, 36);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                // Colors and fonts
                var headerColor = new BaseColor(102, 126, 234); // #667eea
                var sectionColor = new BaseColor(118, 75, 162); // #764ba2
                var tableHeaderColor = new BaseColor(230, 230, 250); // Lavender
                var tableBorderColor = new BaseColor(180, 180, 180);
                var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 18, headerColor);
                var labelFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12, sectionColor);
                var valueFont = FontFactory.GetFont(FontFactory.HELVETICA, 12, BaseColor.BLACK);
                var smallFont = FontFactory.GetFont(FontFactory.HELVETICA, 10, BaseColor.DARK_GRAY);

                // Company Header
                //var companyName = "Your Real Estate Company";
                //var companyAddress = "123, Business Street, City, State - 500001";
                //var companyContact = "Phone: +91-9876543210 | Email: info@company.com";
                //var companyGST = "GST: XX-XXXXX-XXXXX-XX";

                var companyName = _db.Settings.FirstOrDefault(s => s.SettingKey == "CompanyName")?.SettingValue ?? "Real Estate Company";
                var companyAddress = _db.Settings.FirstOrDefault(s => s.SettingKey == "CompanyAddress")?.SettingValue ?? "123, Business Street, City, State - 500001";
                var companyContact = _db.Settings.FirstOrDefault(s => s.SettingKey == "CompanyContact")?.SettingValue ?? "Phone: +91-9876543210 | Email: info@company.com";
                var companyGST = _db.Settings.FirstOrDefault(s => s.SettingKey == "CompanyGST")?.SettingValue ?? "GST: XX-XXXXX-XXXXX-XX";

                var companyHeader = new Paragraph(companyName, titleFont) { Alignment = Element.ALIGN_CENTER };
                doc.Add(companyHeader);
                doc.Add(new Paragraph(companyAddress, smallFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph(companyContact, smallFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph(companyGST, smallFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph(" "));

                // Quotation Title
                var quotationTitle = new Paragraph($"QUOTATION: {quotation.QuotationNumber} {quotation.Status}", labelFont) { Alignment = Element.ALIGN_CENTER };
                doc.Add(quotationTitle);
                doc.Add(new Paragraph(" "));

                // Bill To Section
                doc.Add(new Paragraph("Bill To", labelFont));
                if (lead != null)
                {
                    doc.Add(new Paragraph(lead.Name, valueFont));
                    doc.Add(new Paragraph(lead.Contact, valueFont));
                    doc.Add(new Paragraph(lead.Email, valueFont));
                }
                doc.Add(new Paragraph(" "));

                // Quotation Details Section
                doc.Add(new Paragraph("Quotation Details", labelFont));
                var detailsTable = new PdfPTable(2) { WidthPercentage = 80, HorizontalAlignment = Element.ALIGN_LEFT };
                detailsTable.DefaultCell.Border = Rectangle.NO_BORDER;
                detailsTable.AddCell(new Phrase("Date:", labelFont));
                detailsTable.AddCell(new Phrase($"{quotation.QuotationDate:dd MMM yyyy}", valueFont));
                detailsTable.AddCell(new Phrase("Valid Until:", labelFont));
                detailsTable.AddCell(new Phrase(quotation.ValidUntil.HasValue ? quotation.ValidUntil.Value.ToString("dd MMM yyyy") : "-", valueFont));
                detailsTable.AddCell(new Phrase("Created By:", labelFont));
                detailsTable.AddCell(new Phrase($"{quotation.CreatedBy}", valueFont));
                doc.Add(detailsTable);
                doc.Add(new Paragraph(" "));

                // Property Details Section
                doc.Add(new Paragraph("Property Details", labelFont));
                var propTable = new PdfPTable(2) { WidthPercentage = 80, HorizontalAlignment = Element.ALIGN_LEFT };
                propTable.DefaultCell.Border = Rectangle.NO_BORDER;
                propTable.AddCell(new Phrase("Property:", labelFont));
                propTable.AddCell(new Phrase(property?.PropertyName ?? "-", valueFont));
                propTable.AddCell(new Phrase("Location:", labelFont));
                propTable.AddCell(new Phrase(property?.Location ?? "-", valueFont));
                propTable.AddCell(new Phrase("Flat:", labelFont));
                propTable.AddCell(new Phrase(flat?.FlatName ?? "-", valueFont));
                propTable.AddCell(new Phrase("Type:", labelFont));
                propTable.AddCell(new Phrase(flat?.BHK ?? "-", valueFont));
                propTable.AddCell(new Phrase("Area:", labelFont));
                propTable.AddCell(new Phrase(flat?.AreaSqft != null ? $"{flat.AreaSqft} sq.ft" : "-", valueFont));
                propTable.AddCell(new Phrase("Floor:", labelFont));
                propTable.AddCell(new Phrase(flat?.FloorNumber ?? "-", valueFont));
                doc.Add(propTable);
                doc.Add(new Paragraph(" "));

                // Items Table
                var table = new PdfPTable(6) { WidthPercentage = 100 };
                table.SetWidths(new float[] { 1, 4, 2, 2, 2, 2 });
                string[] headers = { "#", "Description", "Type", "Qty", "Amount", "Total" };
                foreach (var h in headers)
                {
                    var cell = new PdfPCell(new Phrase(h, labelFont))
                    {
                        BackgroundColor = tableHeaderColor,
                        BorderColor = tableBorderColor,
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        Padding = 5
                    };
                    table.AddCell(cell);
                }
                int idx = 1;
                foreach (var item in items)
                {
                    table.AddCell(new PdfPCell(new Phrase(idx.ToString(), valueFont)) { Padding = 5, BorderColor = tableBorderColor });
                    table.AddCell(new PdfPCell(new Phrase(item.Description, valueFont)) { Padding = 5, BorderColor = tableBorderColor });
                    table.AddCell(new PdfPCell(new Phrase(item.ItemType, valueFont)) { Padding = 5, BorderColor = tableBorderColor });
                    table.AddCell(new PdfPCell(new Phrase(item.Quantity.ToString(), valueFont)) { Padding = 5, BorderColor = tableBorderColor });
                    table.AddCell(new PdfPCell(new Phrase($"â‚¹{item.Amount:N2}", valueFont)) { Padding = 5, BorderColor = tableBorderColor });
                    table.AddCell(new PdfPCell(new Phrase($"â‚¹{item.Total:N2}", valueFont)) { Padding = 5, BorderColor = tableBorderColor });
                    idx++;
                }
                doc.Add(table);
                doc.Add(new Paragraph(" "));

                // Totals Section
                var totalsTable = new PdfPTable(2) { WidthPercentage = 40, HorizontalAlignment = Element.ALIGN_RIGHT };
                totalsTable.DefaultCell.Border = Rectangle.NO_BORDER;
                totalsTable.AddCell(new Phrase("Subtotal:", labelFont));
                totalsTable.AddCell(new Phrase($"â‚¹{quotation.TotalAmount:N2}", valueFont));
                totalsTable.AddCell(new Phrase("Discount:", labelFont));
                totalsTable.AddCell(new Phrase($"- â‚¹{quotation.DiscountAmount:N2}", valueFont));
                totalsTable.AddCell(new Phrase($"GST (5%):", labelFont));
                totalsTable.AddCell(new Phrase($"â‚¹{quotation.TaxAmount:N2}", valueFont));
                totalsTable.AddCell(new Phrase("Grand Total:", labelFont));
                totalsTable.AddCell(new Phrase($"â‚¹{quotation.GrandTotal:N2}", valueFont));
                doc.Add(totalsTable);
                doc.Add(new Paragraph(" "));

                // Notes
                doc.Add(new Paragraph("Notes:", labelFont));
                doc.Add(new Paragraph(quotation.Notes ?? "", valueFont));
                doc.Add(new Paragraph(" "));

                // Terms & Conditions
                doc.Add(new Paragraph("Terms & Conditions:", labelFont));
                var terms = new[] {
            "This quotation is valid for the period mentioned above.",
            "Payment terms and conditions as per the payment plan.",
            "All prices are inclusive of GST unless otherwise stated.",
            "Property specifications are subject to change as per approved plans.",
            "Booking will be confirmed only after receipt of booking amount.",
            "This is a computer-generated quotation and does not require a signature.",
            "Thank you for your interest in Your Real Estate Company",
            "For any queries, please contact us at +91-9876543210 or info@company.com"
        };
                foreach (var t in terms)
                    doc.Add(new Paragraph(t, smallFont));

                doc.Close();
                return File(ms.ToArray(), "application/pdf", $"Quotation_{id}.pdf");
            }
        }

        // GET: Quotations/Edit/{id}
        [Route("editquotation/{id}")]
        public IActionResult Edit(string id)
        {
            var decodedId = IdObfuscator.Decode(id);
            if (decodedId == null)
            {
                return NotFound();
            }
            var quotation = _db.Quotations.FirstOrDefault(q => q.QuotationId == decodedId.Value);
            if (quotation == null)
                return NotFound();

            var selectedFlat = quotation.FlatId.HasValue
                ? _db.PropertyFlats.FirstOrDefault(f => f.FlatId == quotation.FlatId.Value)
                : null;
            var selectedFloorNumber = selectedFlat?.FloorNumber;
            if (string.IsNullOrWhiteSpace(selectedFloorNumber))
            {
                selectedFloorNumber = selectedFlat?.FloorName;
            }

            ViewBag.Leads = _db.Leads.Where(l => l.Status == "Active").OrderBy(l => l.Name).ToList();
            ViewBag.Properties = _db.Properties.OrderBy(p => p.PropertyName).ToList();
            ViewBag.SelectedFloorNumber = selectedFloorNumber ?? string.Empty;
            ViewBag.SelectedFlatId = quotation.FlatId ?? 0;
            
            // Get GST rate from settings (same as Create)
            var gstRate = SettingsController.GetSettingValueDecimal(_db, "GSTRate", 5);
            ViewBag.GSTRate = gstRate;

            // Get items for this quotation
            ViewBag.Items = _db.QuotationItems.Where(i => i.QuotationId == decodedId.Value).ToList();

            return View(quotation);
        }

        // POST: Quotations/Edit
        [HttpPost]
        public IActionResult Edit(QuotationModel model, List<QuotationItemModel> items)
        {
            try
            {
                var quotation = _db.Quotations.FirstOrDefault(q => q.QuotationId == model.QuotationId);
                if (quotation == null)
                    return Json(new { success = false, message = "Quotation not found" });

                // Update quotation
                quotation.LeadId = model.LeadId;
                quotation.PropertyId = model.PropertyId;
                quotation.FlatId = model.FlatId;
                quotation.ValidUntil = model.ValidUntil;
                quotation.Notes = model.Notes;
                quotation.ModifiedOn = IndianTime.Now;

                // Calculate totals from items
                decimal subtotal = 0;
                foreach (var item in items)
                {
                    subtotal += item.Total;
                }
                quotation.BasePrice = subtotal;
                quotation.TotalAmount = subtotal;
                
                // Update discount amount from percentage (get from form)
                var discountPercent = Request.Form["DiscountPercent"].ToString();
                if (!string.IsNullOrEmpty(discountPercent) && decimal.TryParse(discountPercent, out decimal discountPercentValue))
                {
                    quotation.DiscountAmount = (subtotal * discountPercentValue) / 100;
                }
                
                var gstRate = SettingsController.GetSettingValueDecimal(_db, "GSTRate", 5);
                quotation.TaxAmount = (subtotal - quotation.DiscountAmount) * (gstRate / 100);
                quotation.GrandTotal = (subtotal - quotation.DiscountAmount) + quotation.TaxAmount;

                // Remove old items
                var oldItems = _db.QuotationItems.Where(i => i.QuotationId == model.QuotationId).ToList();
                _db.QuotationItems.RemoveRange(oldItems);

                // Add new items
                foreach (var item in items)
                {
                    item.QuotationId = model.QuotationId;
                    _db.QuotationItems.Add(item);
                }

                _db.SaveChanges();
                return Json(new { success = true, message = "Quotation updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }
    }
}

