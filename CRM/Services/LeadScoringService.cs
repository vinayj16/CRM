using CRM.Helpers;
using CRM.Models;
using Microsoft.Extensions.Logging;

namespace CRM.Services
{
    /// <summary>
    /// Computes an AI-style lead score (0-100) from lead attributes.
    /// Higher score = hotter lead.
    /// </summary>
    public class LeadScoringService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<LeadScoringService> _logger;

        public LeadScoringService(AppDbContext db, ILogger<LeadScoringService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public (int Score, string Grade, string Reasons) Compute(LeadModel lead)
        {
            int score = 0;
            var reasons = new List<string>();

            if (!string.IsNullOrEmpty(lead.Contact) && lead.Contact.Length >= 10)
            {
                score += 15; reasons.Add("Valid contact number");
            }
            if (!string.IsNullOrEmpty(lead.Email) && lead.Email.Contains("@"))
            {
                score += 10; reasons.Add("Valid email");
            }
            if (!string.IsNullOrEmpty(lead.PreferredLocation))
            {
                score += 10; reasons.Add("Location specified");
            }
            if (!string.IsNullOrEmpty(lead.Budget) || !string.IsNullOrEmpty(lead.Sqft))
            {
                score += 15; reasons.Add("Budget/area specified");
            }
            if (!string.IsNullOrEmpty(lead.PropertyType))
            {
                score += 10; reasons.Add("Property type specified");
            }
            // Stage weighting
            var stage = (lead.Stage ?? "").ToLower();
            if (stage.Contains("booking") || stage.Contains("negotiation")) { score += 25; reasons.Add("Advanced stage"); }
            else if (stage.Contains("site visit") || stage.Contains("quotation")) { score += 15; reasons.Add("Mid-funnel stage"); }
            else if (stage.Contains("contacted") || stage.Contains("qualified")) { score += 10; reasons.Add("Engaged"); }
            else { score += 5; reasons.Add("New lead"); }

            // Recency
            if (lead.CreatedOn >= IndianTime.Now.AddDays(-7)) { score += 10; reasons.Add("Recent inquiry"); }

            // Follow-ups indicate interest
            var fuCount = _db.FollowUps.Count(f => f.LeadId == lead.LeadId);
            if (fuCount > 0) { score += Math.Min(fuCount * 3, 15); reasons.Add($"{fuCount} follow-up(s)"); }

            score = Math.Min(score, 100);
            var grade = score >= 70 ? "Hot" : score >= 40 ? "Warm" : "Cold";
            return (score, grade, string.Join("; ", reasons));
        }

        public async Task RecomputeAllAsync()
        {
            var leads = _db.Leads.ToList();
            var existing = _db.LeadScores.ToList();
            foreach (var lead in leads)
            {
                var (score, grade, reasons) = Compute(lead);
                var rec = existing.FirstOrDefault(s => s.LeadId == lead.LeadId);
                if (rec == null)
                {
                    _db.LeadScores.Add(new LeadScoreModel
                    {
                        LeadId = lead.LeadId,
                        Score = score,
                        Grade = grade,
                        Reasons = reasons,
                        ComputedOn = IndianTime.Now
                    });
                }
                else
                {
                    rec.Score = score; rec.Grade = grade; rec.Reasons = reasons; rec.ComputedOn = IndianTime.Now;
                    _db.LeadScores.Update(rec);
                }
            }
            await _db.SaveChangesAsync();
        }
    }
}