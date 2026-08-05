using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CRM.Helpers;
using CRM.Models;
using CRM.Services;

namespace CRM.Controllers
{
    [Route("api/mobile")]
    [ApiController]
    public class MobileApiFeaturesController : ControllerBase
    {
        private readonly AppDbContext _db;

        public MobileApiFeaturesController(AppDbContext db)
        {
            _db = db;
        }

        private int GetTenantId()
        {
            var claim = User.FindFirst("TenantId")?.Value;
            return int.TryParse(claim, out var tid) ? tid : 0;
        }

        private int GetUserId()
        {
            var claim = User.FindFirst("UserId")?.Value;
            return int.TryParse(claim, out var uid) ? uid : 0;
        }

        // ===================== CAMPAIGNS =====================

        [HttpGet("campaigns")]
        public async Task<IActionResult> GetCampaigns([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var tid = GetTenantId();
            var query = _db.Campaigns.Where(c => c.TenantId == tid).OrderByDescending(c => c.CreatedOn);
            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return Ok(new { success = true, data = items, total, page, pageSize });
        }

        [HttpPost("campaigns")]
        public async Task<IActionResult> CreateCampaign([FromBody] MobileCampaignRequest request)
        {
            var tid = GetTenantId();
            var maxId = await _db.Campaigns.AnyAsync() ? await _db.Campaigns.MaxAsync(c => c.CampaignId) : 0;
            var campaign = new CampaignModel
            {
                TenantId = tid,
                CampaignId = maxId + 1,
                CampaignName = request.Name ?? "New Campaign",
                Channel = request.Channel,
                Status = "Draft",
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                Budget = request.Budget,
                MessageTemplate = request.MessageTemplate,
                AudienceFilter = request.AudienceFilter,
                CreatedBy = GetUserId(),
                CreatedOn = IndianTime.Now
            };
            _db.Campaigns.Add(campaign);
            return Ok(new { success = true, data = campaign });
        }

        [HttpPut("campaigns/{id}/status")]
        public async Task<IActionResult> UpdateCampaignStatus(int id, [FromBody] MobileStatusRequest request)
        {
            var tid = GetTenantId();
            var campaign = await _db.Campaigns.FirstOrDefaultAsync(c => c.CampaignId == id && c.TenantId == tid);
            if (campaign == null) return NotFound(new { success = false, message = "Campaign not found" });
            campaign.Status = request.Status ?? campaign.Status;
            campaign.UpdatedOn = IndianTime.Now;
            return Ok(new { success = true });
        }

        // ===================== AGENTS =====================

        [HttpGet("agents")]
        public async Task<IActionResult> GetAgents([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var tid = GetTenantId();
            var query = _db.Agents.Where(a => a.TenantId == tid).OrderByDescending(a => a.CreatedOn);
            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return Ok(new { success = true, data = items, total, page, pageSize });
        }

        [HttpPost("agents")]
        public async Task<IActionResult> CreateAgent([FromBody] MobileAgentRequest request)
        {
            var tid = GetTenantId();
            var maxId = await _db.Agents.AnyAsync() ? await _db.Agents.MaxAsync(a => a.AgentId) : 0;
            var agent = new AgentModel
            {
                TenantId = tid,
                AgentId = maxId + 1,
                FullName = request.Name ?? "",
                Email = request.Email,
                Phone = request.Phone,
                Address = request.Address,
                AgentType = request.AgentType ?? "Commission",
                Salary = request.Salary,
                Status = "Active",
                CreatedOn = IndianTime.Now
            };
            _db.Agents.Add(agent);
            return Ok(new { success = true, data = agent });
        }

        // ===================== PARTNER LEADS =====================

        [HttpGet("partner-leads")]
        public async Task<IActionResult> GetPartnerLeads([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var tid = GetTenantId();
            var query = _db.PartnerLeads.Where(p => p.TenantId == tid).OrderByDescending(p => p.CreatedOn);
            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return Ok(new { success = true, data = items, total, page, pageSize });
        }

        [HttpPost("partner-leads")]
        public async Task<IActionResult> SubmitPartnerLead([FromBody] MobilePartnerLeadRequest request)
        {
            var tid = GetTenantId();
            var maxId = await _db.PartnerLeads.AnyAsync() ? await _db.PartnerLeads.MaxAsync(p => p.LeadId) : 0;
            var lead = new PartnerLeadModel
            {
                TenantId = tid,
                LeadId = maxId + 1,
                PartnerId = request.PartnerId,
                LeadName = request.Name ?? "",
                Contact = request.Contact,
                Email = request.Email,
                Location = request.Location,
                Stage = request.Stage ?? "New",
                Status = "New",
                Source = request.Source,
                Type = request.Type,
                PropertyInterest = request.PropertyInterest,
                CreatedOn = IndianTime.Now
            };
            _db.PartnerLeads.Add(lead);
            return Ok(new { success = true, data = lead });
        }

        [HttpPut("partner-leads/{id}/status")]
        public async Task<IActionResult> UpdatePartnerLeadStatus(int id, [FromBody] MobileStatusRequest request)
        {
            var tid = GetTenantId();
            var lead = await _db.PartnerLeads.FirstOrDefaultAsync(p => p.LeadId == id && p.TenantId == tid);
            if (lead == null) return NotFound(new { success = false, message = "Partner lead not found" });
            lead.Status = request.Status ?? lead.Status;
            return Ok(new { success = true });
        }

        // ===================== PARTNER COMMISSIONS =====================

        [HttpGet("partner-commissions")]
        public async Task<IActionResult> GetPartnerCommissions([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var tid = GetTenantId();
            var query = _db.PartnerCommissions.Where(p => p.TenantId == tid).OrderByDescending(p => p.CreatedOn);
            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return Ok(new { success = true, data = items, total, page, pageSize });
        }

        // ===================== TESTIMONIALS =====================

        [HttpGet("testimonials")]
        public async Task<IActionResult> GetTestimonials([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var tid = GetTenantId();
            var query = _db.Testimonials.Where(t => t.TenantId == tid).OrderByDescending(t => t.TestimonialId);
            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return Ok(new { success = true, data = items, total, page, pageSize });
        }

        [HttpPost("testimonials")]
        public async Task<IActionResult> CreateTestimonial([FromBody] MobileTestimonialRequest request)
        {
            var tid = GetTenantId();
            var maxId = await _db.Testimonials.AnyAsync() ? await _db.Testimonials.MaxAsync(t => t.TestimonialId) : 0;
            var testimonial = new TestimonialModel
            {
                TenantId = tid,
                TestimonialId = maxId + 1,
                Name = request.Name ?? "",
                Content = request.Content ?? "",
                Rating = request.Rating,
                Tag = request.Tag ?? "General",
                IsActive = true
            };
            _db.Testimonials.Add(testimonial);
            return Ok(new { success = true, data = testimonial });
        }

        // ===================== COMPANY CHAT / MESSAGES =====================

        [HttpGet("company-messages")]
        public async Task<IActionResult> GetCompanyMessages([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            var tid = GetTenantId();
            var uid = GetUserId();
            var query = _db.CompanyMessages
                .Where(m => m.TenantId == tid && !m.IsDeleted &&
                    (m.RecipientId == 0 || m.RecipientId == uid || m.SenderId == uid))
                .OrderByDescending(m => m.SentAt);
            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return Ok(new { success = true, data = items, total, page, pageSize });
        }

        [HttpPost("company-messages")]
        public async Task<IActionResult> SendCompanyMessage([FromBody] MobileMessageRequest request)
        {
            var tid = GetTenantId();
            var uid = GetUserId();
            var userName = User.FindFirst("Name")?.Value ?? "User";
            var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
            var msg = new CompanyMessageModel
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                TenantId = tid,
                SenderId = uid,
                SenderName = userName,
                SenderRole = userRole,
                RecipientId = request.RecipientId,
                RecipientName = request.RecipientName ?? "",
                MessageText = request.Message ?? "",
                SentAt = IndianTime.Now,
                IsRead = false,
                IsDeleted = false
            };
            _db.CompanyMessages.Add(msg);
            return Ok(new { success = true, data = msg });
        }

        [HttpPost("company-messages/{id}/read")]
        public async Task<IActionResult> MarkMessageRead(string id)
        {
            var tid = GetTenantId();
            var msg = await _db.CompanyMessages.FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tid);
            if (msg == null) return NotFound(new { success = false });
            msg.IsRead = true;
            msg.ReadAt = IndianTime.Now;
            return Ok(new { success = true });
        }

        // ===================== INQUIRIES =====================

        [HttpGet("inquiries")]
        public async Task<IActionResult> GetInquiries([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var tid = GetTenantId();
            var query = _db.Inquiries.Where(i => i.TenantId == tid).OrderByDescending(i => i.Id);
            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return Ok(new { success = true, data = items, total, page, pageSize });
        }

        [HttpPut("inquiries/{id}/status")]
        public async Task<IActionResult> UpdateInquiryStatus(int id, [FromBody] MobileStatusRequest request)
        {
            var tid = GetTenantId();
            var inquiry = await _db.Inquiries.FirstOrDefaultAsync(i => i.Id == id && i.TenantId == tid);
            if (inquiry == null) return NotFound(new { success = false, message = "Inquiry not found" });
            inquiry.Status = request.Status ?? inquiry.Status;
            return Ok(new { success = true });
        }
    }

    // ===================== REQUEST MODELS =====================

    public class MobileCampaignRequest
    {
        public string? Name { get; set; }
        public string? Channel { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public decimal? Budget { get; set; }
        public string? MessageTemplate { get; set; }
        public string? AudienceFilter { get; set; }
    }

    public class MobileAgentRequest
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? AgentType { get; set; }
        public decimal? Salary { get; set; }
    }

    public class MobilePartnerLeadRequest
    {
        public int PartnerId { get; set; }
        public string? Name { get; set; }
        public string? Contact { get; set; }
        public string? Email { get; set; }
        public string? Location { get; set; }
        public string? Stage { get; set; }
        public string? Source { get; set; }
        public string? Type { get; set; }
        public string? PropertyInterest { get; set; }
    }

    public class MobileTestimonialRequest
    {
        public string? Name { get; set; }
        public string? Content { get; set; }
        public int Rating { get; set; } = 5;
        public string? Tag { get; set; }
    }

    public class MobileMessageRequest
    {
        public int RecipientId { get; set; }
        public string? RecipientName { get; set; }
        public string? Message { get; set; }
    }
}
