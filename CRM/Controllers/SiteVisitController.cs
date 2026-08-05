using CRM.Attributes;
using CRM.Helpers;
using CRM.Models;
using CRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CRM.Controllers
{
    [Authorize]
    [Route("SiteVisits")]
    public class SiteVisitController : Controller
    {
        private readonly AppDbContext _db;
        private readonly INotificationService _notificationService;
        private readonly ILogger<SiteVisitController> _logger;

        public SiteVisitController(AppDbContext db, INotificationService notificationService, ILogger<SiteVisitController> logger)
        {
            _db = db;
            _notificationService = notificationService;
            _logger = logger;
        }

        private int _userId()
        {
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid, out int id);
            return id;
        }
        private string _role() => User?.FindFirst(ClaimTypes.Role)?.Value ?? "Admin";

        [PermissionAuthorize("View")]
        [Route("")]
        [Route("Index")]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        [Route("GetAll")]
        public IActionResult GetAll(string status = "", string search = "")
        {
            var role = _role();
            var uid = _userId();
            var visits = _db.SiteVisits.AsQueryable().ToList();

            if (role.ToLower() == "sales" || role.ToLower() == "agent")
                visits = visits.Where(v => v.ExecutiveId == uid).ToList();
            if (!string.IsNullOrEmpty(status))
                visits = visits.Where(v => v.Status == status).ToList();
            if (!string.IsNullOrEmpty(search))
            {
                search = search.ToLower();
                visits = visits.Where(v =>
                    (v.LeadName != null && v.LeadName.ToLower().Contains(search)) ||
                    (v.PropertyName != null && v.PropertyName.ToLower().Contains(search)) ||
                    (v.ExecutiveName != null && v.ExecutiveName.ToLower().Contains(search))).ToList();
            }

            visits = visits.OrderByDescending(v => v.ScheduledDate).ToList();
            return Json(visits);
        }

        [HttpGet]
        [Route("Get")]
        public IActionResult Get(int id)
        {
            var v = _db.SiteVisits.FirstOrDefault(x => x.SiteVisitId == id);
            if (v == null) return NotFound();
            return Json(v);
        }

        [HttpGet]
        [Route("GetLeads")]
        public IActionResult GetLeads()
        {
            var leads = _db.Leads.AsQueryable()
                .Select(l => new { l.LeadId, l.Name, l.ExecutiveId })
                .ToList();
            return Json(leads);
        }

        [HttpGet]
        [Route("GetProperties")]
        public IActionResult GetProperties()
        {
            var props = _db.Properties.AsQueryable()
                .Select(p => new { p.PropertyId, p.PropertyName })
                .ToList();
            return Json(props);
        }

        [HttpPost]
        [Route("Save")]
        public async Task<IActionResult> Save([FromForm] SiteVisitModel model)
        {
            try
            {
                var existing = _db.SiteVisits.ToList();
                var lead = _db.Leads.FirstOrDefault(l => l.LeadId == model.LeadId);
                var prop = _db.Properties.FirstOrDefault(p => p.PropertyId == model.PropertyId);
                var exec = _db.Users.FirstOrDefault(u => u.UserId == model.ExecutiveId);

                if (model.SiteVisitId == 0)
                {
                    model.SiteVisitId = existing.Any() ? existing.Max(v => v.SiteVisitId) + 1 : 1;
                    model.LeadName = lead?.Name;
                    model.PropertyName = prop?.PropertyName;
                    model.ExecutiveName = exec?.Username;
                    model.CreatedBy = _userId();
                    model.CreatedOn = IndianTime.Now;
                    model.Status = "Scheduled";
                    _db.SiteVisits.Add(model);
                }
                else
                {
                    var v = _db.SiteVisits.FirstOrDefault(x => x.SiteVisitId == model.SiteVisitId);
                    if (v == null) return NotFound();
                    v.LeadId = model.LeadId;
                    v.LeadName = lead?.Name;
                    v.ExecutiveId = model.ExecutiveId;
                    v.ExecutiveName = exec?.Username;
                    v.PropertyId = model.PropertyId;
                    v.PropertyName = prop?.PropertyName;
                    v.ScheduledDate = model.ScheduledDate;
                    v.TimeSlot = model.TimeSlot;
                    v.Status = model.Status;
                    v.Vehicle = model.Vehicle;
                    v.DriverName = model.DriverName;
                    v.Feedback = model.Feedback;
                    v.Rating = model.Rating;
                    v.Notes = model.Notes;
                    v.UpdatedOn = IndianTime.Now;
                    _db.SiteVisits.Update(v);
                }
                await _db.SaveChangesAsync();

                if (model.ExecutiveId.HasValue)
                {
                    await _notificationService.CreateNotificationAsync(
                        "Site Visit Scheduled",
                        $"Site visit for {model.LeadName} on {model.ScheduledDate:MMM dd, yyyy} {model.TimeSlot}",
                        "SiteVisit", model.ExecutiveId.Value, "/sitevisits");
                }

                return Json(new { success = true, message = "Site visit saved." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [Route("CheckIn")]
        public async Task<IActionResult> CheckIn(int id, string location = "")
        {
            var v = _db.SiteVisits.FirstOrDefault(x => x.SiteVisitId == id);
            if (v == null) return NotFound();
            v.CheckInTime = IndianTime.Now;
            v.CheckInLocation = location;
            v.Status = "Completed";
            v.UpdatedOn = IndianTime.Now;
            _db.SiteVisits.Update(v);
            await _db.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        [Route("CheckOut")]
        public async Task<IActionResult> CheckOut(int id, string location = "", string feedback = "", int rating = 0)
        {
            var v = _db.SiteVisits.FirstOrDefault(x => x.SiteVisitId == id);
            if (v == null) return NotFound();
            v.CheckOutTime = IndianTime.Now;
            v.CheckOutLocation = location;
            v.Feedback = feedback;
            v.Rating = rating > 0 ? rating : v.Rating;
            v.UpdatedOn = IndianTime.Now;
            _db.SiteVisits.Update(v);
            await _db.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        [Route("Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var v = _db.SiteVisits.FirstOrDefault(x => x.SiteVisitId == id);
            if (v == null) return NotFound();
            _db.SiteVisits.Remove(v);
            await _db.SaveChangesAsync();
            return Json(new { success = true });
        }
    }
}