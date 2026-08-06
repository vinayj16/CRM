using CRM.Helpers;
using CRM.Attributes;
using CRM.Models;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CRM.Controllers
{
    // Request model for assigning leads
    public class AssignLeadsRequest
    {
        public List<int> LeadIds { get; set; } = new List<int>();
        public int ExecutiveId { get; set; }
    }

    [RoleAuthorize("Admin,Partner")]
    public class WebhookLeadsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly CRM.Services.INotificationService _notificationService;
        private readonly ILogger<WebhookLeadsController> _logger;

        public WebhookLeadsController(AppDbContext context, CRM.Services.INotificationService notificationService, ILogger<WebhookLeadsController> logger)
        {
            _context = context;
            _notificationService = notificationService;
            _logger = logger;
        }

        private (int? userId, string? role, int? channelPartnerId) GetUserContext()
        {
            var userIdClaim = User.FindFirst("UserId")?.Value;
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var channelPartnerIdClaim = User.FindFirst("ChannelPartnerId")?.Value;
            
            int? userId = null;
            if (!string.IsNullOrEmpty(userIdClaim) && int.TryParse(userIdClaim, out int uid))
                userId = uid;
                
            int? channelPartnerId = null;
            if (!string.IsNullOrEmpty(channelPartnerIdClaim) && int.TryParse(channelPartnerIdClaim, out int cpId))
                channelPartnerId = cpId;
                
            return (userId, role, channelPartnerId);
        }

        // GET: WebhookLeads/Index
        [Route("WebhookLeads")]
        [Route("WebhookLeads/Index")]
        public IActionResult Index()
        {
            var (userId, role, channelPartnerId) = GetUserContext();
            
            IQueryable<LeadModel> webhookLeadsQuery = _context.Leads.AsQueryable();
            IQueryable<UserModel> executivesQuery = _context.Users.Where(u => u.Role == "Sales" || u.Role == "Agent").AsQueryable();
            
            if (role?.ToLower() == "admin")
            {
                // Admin sees ALL unassigned leads (ExecutiveId == null) regardless of ChannelPartnerId
                // This includes partner unassigned leads that haven't been assigned to an executive yet
                webhookLeadsQuery = webhookLeadsQuery.Where(l => l.ExecutiveId == null);
                    
                // Admin sees all internal agents (ChannelPartnerId = null)
                executivesQuery = executivesQuery.Where(u => u.ChannelPartnerId == null);
            }
            else if (role?.ToLower() == "partner")
            {
                // Partner sees only their own unassigned leads
                webhookLeadsQuery = webhookLeadsQuery.Where(l => 
                    l.ExecutiveId == null && l.ChannelPartnerId == channelPartnerId);
                    
                // Partner sees only their own agents
                executivesQuery = executivesQuery.Where(u => u.ChannelPartnerId == channelPartnerId);
            }
            
            var webhookLeads = webhookLeadsQuery.OrderByDescending(l => l.CreatedOn).ToList();
            var executives = executivesQuery.OrderBy(u => u.Username)
                .Select(u => new { u.UserId, u.Username, u.Role })
                .ToList();

            ViewBag.Executives = executives;
            return View(webhookLeads);
        }

        [Route("WebhookLeads/AssignExecutive")]
        // POST: WebhookLeads/AssignExecutive
        [HttpPost]
        public async Task<IActionResult> AssignExecutive([FromBody] AssignLeadsRequest request)
        {
            try
            {
                var (userId, role, channelPartnerId) = GetUserContext();
                
                if (request == null || request.LeadIds == null || !request.LeadIds.Any())
                {
                    return Json(new { success = false, message = "No leads selected" });
                }

                // Verify the executive belongs to the current user's context
                var executive = await _context.Users.FirstOrDefaultAsync(u => u.UserId == request.ExecutiveId);
                if (executive == null)
                {
                    return Json(new { success = false, message = "Executive not found" });
                }
                
                // Security check: ensure user can only assign to their own agents
                if (role?.ToLower() == "admin" && executive.ChannelPartnerId != null)
                {
                    return Json(new { success = false, message = "Cannot assign to partner agents" });
                }
                if (role?.ToLower() == "partner" && executive.ChannelPartnerId != channelPartnerId)
                {
                    return Json(new { success = false, message = "Cannot assign to other partner's agents" });
                }

                var leads = await _context.Leads
                    .Where(l => request.LeadIds.Contains(l.LeadId))
                    .ToListAsync();

                foreach (var lead in leads)
                {
                    lead.ExecutiveId = request.ExecutiveId;
                    lead.ModifiedOn = IndianTime.Now;
                    
                    // If this is a partner lead ready to book (Admin assigning), update handover status
                    if (role?.ToLower() == "admin" && lead.ChannelPartnerId != null && lead.HandoverStatus == "ReadyToBook")
                    {
                        lead.HandoverStatus = "HandedOver";
                        lead.AdminAssignedTo = request.ExecutiveId;
                        
                        // Add handover audit record
                        _context.LeadHandoverAudit.Add(new LeadHandoverAuditModel
                        {
                            LeadId = lead.LeadId,
                            FromStatus = "ReadyToBook",
                            ToStatus = "HandedOver",
                            HandedOverBy = userId ?? 0,
                            AssignedTo = request.ExecutiveId,
                            Notes = $"Lead assigned to agent via Unassigned Leads page"
                        });
                    }

                    _context.Leads.Update(lead);
                }

                await _context.SaveChangesAsync();
                
                // Send assignment notifications to agents
                var currentUser = await _context.Users.FindAsync(userId);
                var assignedByName = currentUser?.Username ?? "Admin";
                
                foreach (var lead in leads)
                {
                    await _notificationService.NotifyLeadAssignedAsync(
                        lead.LeadId,
                        lead.Name ?? "Unknown Lead",
                        request.ExecutiveId,
                        assignedByName
                    );
                }

                return Json(new 
                { 
                    success = true, 
                    message = $"{leads.Count} lead(s) assigned successfully" 
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [Route("WebhookLeads/DeleteLead")]
        // POST: WebhookLeads/DeleteLead
        [HttpPost]
        public async Task<IActionResult> DeleteLead(int leadId)
        {
            try
            {
                var lead = await _context.Leads.FindAsync(leadId);
                if (lead == null)
                {
                    return Json(new { success = false, message = "Lead not found" });
                }

                _context.Leads.Remove(lead);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Lead deleted successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}
