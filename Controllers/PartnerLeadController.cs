using CRM.Helpers;
using CRM.Models;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Claims;

namespace CRM.Controllers
{
    public class PartnerLeadController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<PartnerLeadController> _logger;
        public PartnerLeadController(AppDbContext context, ILogger<PartnerLeadController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return RedirectToAction("List");
        }

        [HttpGet]
        public IActionResult Submit()
        {
            return View();
        }

        //[HttpPost]
        //public async Task<IActionResult> Submit(PartnerLeadModel model)
        //{
        //    if (ModelState.IsValid)
        //    {
        //        model.Status = "New";
        //        model.CreatedOn = IndianTime.Now;
        //        _context.PartnerLeads.Add(model);
        //        await _context.SaveChangesAsync();
        //        ViewBag.Message = "Lead submitted successfully.";
        //    }
        //    return View();
        //}
        [HttpPost]
        public async Task<IActionResult> Submit(PartnerLeadModel model)
        {
            // Ensure PartnerId is set from JWT if missing or 0
            if (model.PartnerId == 0)
            {
                var userClaims = User?.Claims;
                string partnerIdStr = "0";
                if (userClaims != null)
                {
                    var idClaim = userClaims.FirstOrDefault(c => c.Type == "UserId" || c.Type == "sub" || c.Type == ClaimTypes.NameIdentifier);
                    if (idClaim != null)
                    {
                        partnerIdStr = idClaim.Value;
                    }
                }
                if (int.TryParse(partnerIdStr, out int partnerIdInt))
                {
                    model.PartnerId = partnerIdInt;
                }
            }
            model.Status = "New";
                model.CreatedOn = IndianTime.Now;
                _context.PartnerLeads.Add(model);
                await _context.SaveChangesAsync();
                // Redirect to the list after successful submission
                return RedirectToAction("List");
            
        }

        [HttpGet]
        public IActionResult List()
        {
            var leads = _context.PartnerLeads.ToList();
            var properties = _context.Properties.Where(p => p.IsActive).ToList();
            ViewBag.Properties = properties;
            // Extract PartnerId (UserId) from JWT token
            string partnerId = "";
            var userClaims = User?.Claims;
            if (userClaims != null)
            {
                var idClaim = userClaims.FirstOrDefault(c => c.Type == "UserId" || c.Type == "sub" || c.Type == ClaimTypes.NameIdentifier);
                if (idClaim != null)
                {
                    partnerId = idClaim.Value;
                }
            }
            ViewBag.PartnerId = partnerId;
            return View(leads);
        }

        [HttpPost]
        public async Task<IActionResult> UploadLeadFile(IFormFile file, int a)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    TempData["Error"] = "Please select a file to upload";
                    return RedirectToAction("List");
                }

                // Get current partner user ID
                var userClaims = User?.Claims;
                int partnerId = 0;
                if (userClaims != null)
                {
                    var idClaim = userClaims.FirstOrDefault(c => c.Type == "UserId" || c.Type == "sub" || c.Type == ClaimTypes.NameIdentifier);
                    if (idClaim != null && int.TryParse(idClaim.Value, out int id))
                    {
                        partnerId = id;
                    }
                }

                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                ms.Position = 0;

                using var workbook = new ClosedXML.Excel.XLWorkbook(ms);
                var sheet = workbook.Worksheets.First();
                var lastRow = sheet.LastRowUsed().RowNumber();

                for (int r = 2; r <= lastRow; r++) // Skip header
                {
                    var leadName = sheet.Cell(r, 1).GetString().Trim();
                    if (string.IsNullOrEmpty(leadName)) continue;

                    var partnerLead = new PartnerLeadModel
                    {
                        LeadName = leadName,
                        Contact = sheet.Cell(r, 2).GetString().Trim(),
                        Email = sheet.Cell(r, 3).GetString().Trim(),
                        Stage = sheet.Cell(r, 4).GetString().Trim(),
                        Status = "New",
                        Source = sheet.Cell(r, 7).GetString().Trim(),
                        Location = sheet.Cell(r, 8).GetString().Trim(),
                        Type = sheet.Cell(r, 11).GetString().Trim(),
                        PartnerId = partnerId,
                        CreatedOn = IndianTime.Now
                    };

                    _context.PartnerLeads.Add(partnerLead);
                }

                await _context.SaveChangesAsync();
                TempData["Success"] = "Bulk upload completed successfully";
                return RedirectToAction("List");
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Bulk upload failed: {ex.Message}";
                return RedirectToAction("List");
            }
        }

        [HttpPost]
        public async Task<IActionResult> ConvertToSale(int leadId, decimal commissionAmount)
        {
            var lead = _context.PartnerLeads.Find(leadId);
            if (lead != null)
            {
                lead.Status = "Converted";
                lead.ConvertedToSale = true;
                lead.CommissionAmount = commissionAmount;
                _context.PartnerLeads.Update(lead);
                await _context.SaveChangesAsync();

                // Create new LeadModel in main Leads table
                // Find property by name and get AssignedTo
                var property = _context.Properties.FirstOrDefault(p => p.PropertyName == lead.PropertyInterest);
                int? executiveId = property?.AssignedTo;
                var newLead = new LeadModel
                {
                    Name = lead.LeadName ?? "",
                    Contact = lead.Contact ?? "",
                    Email = lead.Email ?? "",
                    Stage = lead.Stage ?? "New",
                    Status = "Converted",
                    Source = lead.Source ?? "Partner",
                    Type = lead.Type ?? "",
                    PreferredLocation = lead.Location ?? "",
                    CreatedOn = IndianTime.Now,
                    CreatedBy = lead.PartnerId,
                    ExecutiveId = executiveId,
                    // Add other fields as needed
                };
                _context.Leads.Add(newLead);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction("List");
        }
    }
}
