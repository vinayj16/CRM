using CRM.Helpers;
using CRM.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CRM.Attributes;
using System.Security.Claims;

namespace CRM.Controllers
{
    [Authorize]
    public class SalesPipelinesController : Controller
    {        private readonly AppDbContext _db;
        private readonly ILogger<SalesPipelinesController> _logger;
        public SalesPipelinesController(AppDbContext db, ILogger<SalesPipelinesController> logger)
        {
            _db = db;
            _logger = logger;
        }
        [PermissionAuthorize("View")]
        [Route("salespipelines")]
        [Route("SalesPipelines/Index")]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult GetLeadsByStage()
        {
            var role = User?.FindFirst(ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid, out int userId);
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;

            var query = _db.Leads.AsQueryable();

            if (role?.ToLower() == "partner")
                query = query.Where(l => l.ChannelPartnerId == channelPartnerId);
            else if (role?.ToLower() == "admin")
                query = query.Where(l => l.ChannelPartnerId == null);
            else if (role?.ToLower() == "sales" || role?.ToLower() == "agent")
                query = query.Where(l => l.ExecutiveId == userId);

            var stages = new[] { "New", "Office Meeting", "Site Visit Requested", "Site Visit Done", "Quotation", "Quotation Sent", "Negotiation", "Booked" };
            
            var leadsByStage = stages.Select(stage => new
            {
                stage = stage,
                leads = query.Where(l => l.Stage == stage)
                    .Select(l => new
                    {
                        l.LeadId,
                        encodeUrl = IdObfuscator.Url("Lead", l.LeadId),
                        l.Name,
                        l.Contact,
                        l.Stage,
                        l.Status
                    })
                    .ToList()
            }).ToList();

            return Json(leadsByStage);
        }

        [HttpPost]
        public IActionResult UpdateLeadStage([FromBody] UpdateStageRequest request)
        {
            try
            {
                var lead = _db.Leads.FirstOrDefault(l => l.LeadId == request.LeadId);
                if (lead == null)
                    return Json(new { success = false, message = "Lead not found" });

                lead.Stage = request.NewStage;
                lead.ModifiedOn = IndianTime.Now;
                _db.SaveChanges();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }

    public class UpdateStageRequest
    {
        public int LeadId { get; set; }
        public string NewStage { get; set; }
    }
}

