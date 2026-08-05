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
    [Route("LeadScoring")]
    public class LeadScoringController : Controller
    {
        private readonly AppDbContext _db;
        private readonly LeadScoringService _scoring;
        private readonly ILogger<LeadScoringController> _logger;

        public LeadScoringController(AppDbContext db, LeadScoringService scoring, ILogger<LeadScoringController> logger)
        {
            _db = db;
            _scoring = scoring;
            _logger = logger;
        }

        [PermissionAuthorize("View")]
        [Route("")]
        [Route("Index")]
        public IActionResult Index() => View();

        [HttpGet]
        [Route("GetAll")]
        public IActionResult GetAll()
        {
            var scores = _db.LeadScores.AsQueryable().ToList();
            var leads = _db.Leads.AsQueryable().ToList();
            var result = scores.Select(s =>
            {
                var lead = leads.FirstOrDefault(l => l.LeadId == s.LeadId);
                return new
                {
                    s.ScoreId, s.LeadId, s.Score, s.Grade, s.Reasons,
                    LeadName = lead?.Name ?? "Unknown",
                    Stage = lead?.Stage ?? ""
                };
            }).OrderByDescending(x => x.Score).ToList();
            return Json(result);
        }

        [HttpPost]
        [Route("Recompute")]
        public async Task<IActionResult> Recompute()
        {
            await _scoring.RecomputeAllAsync();
            return Json(new { success = true, message = "Lead scores recomputed." });
        }

        [HttpGet]
        [Route("GetScore")]
        public IActionResult GetScore(int leadId)
        {
            var lead = _db.Leads.FirstOrDefault(l => l.LeadId == leadId);
            if (lead == null) return NotFound();
            var (score, grade, reasons) = _scoring.Compute(lead);
            return Json(new { leadId, score, grade, reasons });
        }
    }
}