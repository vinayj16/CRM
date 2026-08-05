using CRM.Helpers;
using CRM.MasterDb;
using CRM.MasterDb.Models;
using CRM.Models;
using CRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections;
using System.Security.Claims;
using System.Text.Json;

namespace CRM.Controllers
{
    [Authorize]
    public class ExportController : Controller
    {
        private readonly AppDbContext _db;
        private readonly MasterDbContext _masterDb;
        private readonly ILogger<ExportController> _logger;

        public ExportController(AppDbContext db, MasterDbContext masterDb, ILogger<ExportController> logger)
        {
            _db = db;
            _masterDb = masterDb;
            _logger = logger;
        }

        private bool IsAdmin()
        {
            return User.IsInRole("SuperAdmin") || User.IsInRole("Admin");
        }

        // GET /api/export/all - Full JSON export of all collections
        [HttpGet("api/export/all")]
        public async Task<IActionResult> ExportAllData()
        {
            if (!IsAdmin()) return Forbid();

            var data = new Dictionary<string, object>();

            // Users (exclude password)
            data["users"] = _db.Users.ToList().Select(u => new
            {
                u.UserId, u.Username, u.Email, u.Phone, u.Role,
                u.IsActive, u.CreatedDate, u.ChannelPartnerId, u.TenantId
            }).ToList();

            data["user_profiles"] = _db.UserProfiles.ToList();

            // SuperAdmins (exclude PasswordHash)
            var superAdmins = await _masterDb.SuperAdmins.ToListAsync();
            data["super_admins"] = superAdmins.Select(s => new
            {
                s.SuperAdminId, s.Email, s.FullName, s.IsActive,
                s.Role, s.LastLoginOn, s.CreatedOn
            }).ToList();

            data["agents"] = _db.Agents.ToList();
            data["channel_partners"] = _db.ChannelPartners.ToList();
            data["leads"] = _db.Leads.ToList();
            data["properties"] = _db.Properties.ToList();
            data["bookings"] = _db.Bookings.ToList();
            data["payments"] = _db.Payments.ToList();
            data["followups"] = _db.FollowUps.ToList();
            data["expenses"] = _db.Expenses.ToList();
            data["revenues"] = _db.Revenues.ToList();
            data["notifications"] = _db.Notifications.ToList();
            data["testimonials"] = _db.Testimonials.ToList();
            data["settings"] = _db.Settings.ToList();
            data["branding"] = _db.Branding.ToList();
            data["quotations"] = _db.Quotations.ToList();
            data["invoices"] = _db.Invoices.ToList();
            data["role_permissions"] = _db.RolePermissions.ToList();
            data["audit_logs"] = _db.AuditLogs.ToList();
            data["builders"] = _db.Builders.ToList();
            data["bank_accounts"] = _db.BankAccounts.ToList();
            data["inquiries"] = _db.Inquiries.ToList();

            // Master DB
            data["tenants"] = (await _masterDb.Tenants.ToListAsync()).Select(t => new
            {
                t.TenantId, t.CompanyName, t.Subdomain, t.Plan,
                t.IsActive, t.IsSuspended, t.MaxUsers
            }).ToList();

            data["saas_plans"] = (await _masterDb.SaasPlans.ToListAsync()).Select(p => new
            {
                p.PlanId, p.PlanName, p.MonthlyPrice, p.YearlyPrice,
                p.MaxUsers, p.MaxAgents, p.MaxLeadsPerMonth, p.IsActive
            }).ToList();

            data["tenant_subscriptions"] = (await _masterDb.TenantSubscriptions.ToListAsync()).Select(s => new
            {
                s.TenantId, s.PlanId, s.BillingCycle, s.Amount, s.Status,
                s.StartDate, s.EndDate, s.AutoRenew
            }).ToList();

            var result = new
            {
                exportDate = DateTime.UtcNow.ToString("o"),
                totalCollections = data.Count,
                summary = data.ToDictionary(k => k.Key, k => (object)(k.Value is System.Collections.IList li ? li.Count : 0)),
                data
            };

            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            return Content(json, "application/json");
        }

        // GET /api/export/summary - Data counts
        [HttpGet("api/export/summary")]
        public async Task<IActionResult> ExportSummary()
        {
            if (!IsAdmin()) return Forbid();

            var summary = new Dictionary<string, object>
            {
                ["users"] = _db.Users.Count(),
                ["user_profiles"] = _db.UserProfiles.Count(),
                ["super_admins"] = _masterDb.SuperAdmins.Count(),
                ["agents"] = _db.Agents.Count(),
                ["channel_partners"] = _db.ChannelPartners.Count(),
                ["leads"] = _db.Leads.Count(),
                ["properties"] = _db.Properties.Count(),
                ["bookings"] = _db.Bookings.Count(),
                ["payments"] = _db.Payments.Count(),
                ["followups"] = _db.FollowUps.Count(),
                ["expenses"] = _db.Expenses.Count(),
                ["revenues"] = _db.Revenues.Count(),
                ["notifications"] = _db.Notifications.Count(),
                ["testimonials"] = _db.Testimonials.Count(),
                ["settings"] = _db.Settings.Count(),
                ["branding"] = _db.Branding.Count(),
                ["quotations"] = _db.Quotations.Count(),
                ["invoices"] = _db.Invoices.Count(),
                ["tenants"] = _masterDb.Tenants.Count(),
                ["saas_plans"] = _masterDb.SaasPlans.Count(),
                ["tenant_subscriptions"] = _masterDb.TenantSubscriptions.Count(),
                ["role_permissions"] = _db.RolePermissions.Count(),
                ["audit_logs"] = _db.AuditLogs.Count(),
                ["builders"] = _db.Builders.Count(),
                ["bank_accounts"] = _db.BankAccounts.Count(),
                ["inquiries"] = _db.Inquiries.Count()
            };

            return Json(new
            {
                exportDate = DateTime.UtcNow.ToString("o"),
                totalCollections = summary.Count,
                totalDocuments = summary.Values.Sum(v => Convert.ToInt64(v)),
                collections = summary
            });
        }

        // GET /api/export/credentials - List all login credentials
        [HttpGet("api/export/credentials")]
        public async Task<IActionResult> ExportCredentials()
        {
            if (!IsAdmin()) return Forbid();

            var result = new List<object>();

            // SuperAdmins
            var superAdmins = await _masterDb.SuperAdmins.ToListAsync();
            foreach (var sa in superAdmins)
            {
                result.Add(new
                {
                    type = "SuperAdmin",
                    email = sa.Email,
                    fullName = sa.FullName,
                    role = "SuperAdmin",
                    isActive = sa.IsActive,
                    password = "Super@123"
                });
            }

            // Regular Users
            var users = _db.Users.ToList();
            foreach (var u in users)
            {
                result.Add(new
                {
                    type = "User",
                    email = u.Email,
                    username = u.Username,
                    role = u.Role,
                    isActive = u.IsActive,
                    password = "Test@123"
                });
            }

            return Json(new
            {
                generatedAt = DateTime.UtcNow.ToString("o"),
                totalAccounts = result.Count,
                accounts = result
            });
        }

        // POST /api/export/assign-subscriptions - Assign active subscriptions to ALL existing tenants
        [HttpPost("api/export/assign-subscriptions")]
        public async Task<IActionResult> AssignSubscriptions()
        {
            if (!IsAdmin()) return Forbid();

            try
            {
                var results = new List<string>();
                var tenants = await _masterDb.Tenants.ToListAsync();
                var plans = (await _masterDb.SaasPlans.ToListAsync()).OrderBy(p => p.PlanId).ToList();

                if (!plans.Any())
                {
                    return Json(new { success = false, error = "No SaaS plans exist. Run seed first." });
                }

                // Pick the Premium plan (last one) for active subscriptions, else Basic (first)
                var premiumPlan = plans.LastOrDefault();
                var basicPlan = plans.FirstOrDefault();

                int nextSubId = 1;
                var existingSubs = await _masterDb.TenantSubscriptions.ToListAsync();
                if (existingSubs.Any())
                {
                    nextSubId = existingSubs.Max(s => s.SubscriptionId) + 1;
                }

                foreach (var tenant in tenants)
                {
                    // Check if tenant already has an active subscription
                    var hasActiveSub = existingSubs.Any(s => s.TenantId == tenant.TenantId && s.Status == "Active");
                    if (hasActiveSub) continue;

                    var plan = tenant.TenantId <= 1 ? premiumPlan : basicPlan;

                    var sub = new TenantSubscriptionModel
                    {
                        SubscriptionId = nextSubId++,
                        TenantId = tenant.TenantId,
                        PlanId = plan.PlanId,
                        BillingCycle = "Monthly",
                        Amount = plan.MonthlyPrice,
                        StartDate = DateTime.UtcNow,
                        EndDate = DateTime.UtcNow.AddYears(1),
                        Status = "Active",
                        AutoRenew = true,
                        CreatedOn = DateTime.UtcNow
                    };
                    _masterDb.TenantSubscriptions.Add(sub);
                    results.Add($"Tenant '{tenant.CompanyName}' (ID={tenant.TenantId}) → Plan '{plan.PlanName}' (₹{plan.MonthlyPrice}/mo) Active");
                }

                return Json(new
                {
                    success = true,
                    message = $"Assigned subscriptions to {results.Count} tenant(s)",
                    details = results,
                    planCount = plans.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Assign subscriptions failed");
                return Json(new { success = false, error = ex.Message });
            }
        }

        // GET /api/export/superadmin-export - Full multi-tenant export (SuperAdmin only)
        // Bypasses tenant isolation and exports ALL data from ALL companies
        // GET /api/export/download - Download full JSON backup (triggers browser download)
        [HttpGet("api/export/download")]
        public async Task<IActionResult> DownloadBackup()
        {
            if (!IsAdmin()) return Forbid();

            try
            {
                var data = new Dictionary<string, object>();

                data["users"] = _db.Users.ToList().Select(u => new { u.UserId, u.Username, u.Email, u.Phone, u.Role, u.IsActive, u.CreatedDate, u.ChannelPartnerId, u.TenantId }).ToList();
                data["user_profiles"] = _db.UserProfiles.ToList();
                try { data["agents"] = _db.Agents.ToList(); } catch { data["agents"] = new List<object>(); }
                try { data["channel_partners"] = _db.ChannelPartners.ToList(); } catch { data["channel_partners"] = new List<object>(); }
                try { data["leads"] = _db.Leads.ToList(); } catch { data["leads"] = new List<object>(); }
                try { data["properties"] = _db.Properties.ToList(); } catch { data["properties"] = new List<object>(); }
                try { data["bookings"] = _db.Bookings.ToList(); } catch { data["bookings"] = new List<object>(); }
                try { data["payments"] = _db.Payments.ToList(); } catch { data["payments"] = new List<object>(); }
                try { data["followups"] = _db.FollowUps.ToList(); } catch { data["followups"] = new List<object>(); }
                try { data["expenses"] = _db.Expenses.ToList(); } catch { data["expenses"] = new List<object>(); }
                try { data["revenues"] = _db.Revenues.ToList(); } catch { data["revenues"] = new List<object>(); }
                try { data["notifications"] = _db.Notifications.ToList(); } catch { data["notifications"] = new List<object>(); }
                try { data["testimonials"] = _db.Testimonials.ToList(); } catch { data["testimonials"] = new List<object>(); }
                try { data["settings"] = _db.Settings.ToList(); } catch { data["settings"] = new List<object>(); }
                try { data["brandings"] = _db.Branding.ToList(); } catch { data["brandings"] = new List<object>(); }
                try { data["quotations"] = _db.Quotations.ToList(); } catch { data["quotations"] = new List<object>(); }
                try { data["invoices"] = _db.Invoices.ToList(); } catch { data["invoices"] = new List<object>(); }
                try { data["role_permissions"] = _db.RolePermissions.ToList(); } catch { data["role_permissions"] = new List<object>(); }
                try { data["audit_logs"] = _db.AuditLogs.ToList(); } catch { data["audit_logs"] = new List<object>(); }
                try { data["builders"] = _db.Builders.ToList(); } catch { data["builders"] = new List<object>(); }
                try { data["bank_accounts"] = _db.BankAccounts.ToList(); } catch { data["bank_accounts"] = new List<object>(); }
                try { data["inquiries"] = _db.Inquiries.ToList(); } catch { data["inquiries"] = new List<object>(); }

                var result = new
                {
                    exportDate = DateTime.UtcNow.ToString("o"),
                    totalCollections = data.Count,
                    summary = data.ToDictionary(k => k.Key, k => (object)(k.Value is IList li ? li.Count : 0)),
                    data
                };

                var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                return File(bytes, "application/json", $"CRM_Backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup download failed");
                var errorJson = System.Text.Json.JsonSerializer.Serialize(new { success = false, error = ex.Message });
                var errorBytes = System.Text.Encoding.UTF8.GetBytes(errorJson);
                return File(errorBytes, "application/json", $"CRM_Backup_ERROR_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            }
        }

        // GET /api/export/superadmin-export - Full multi-tenant export (SuperAdmin only)
        [HttpGet("api/export/superadmin-export")]
        public async Task<IActionResult> SuperAdminExport()
        {
            if (!User.IsInRole("SuperAdmin")) return Forbid();

            try
            {
                var data = new Dictionary<string, object>();

                // ALL users across all tenants (exclude password)
                data["users"] = _db.Users.ToList().Select(u => new
                {
                    u.UserId, u.Username, u.Email, u.Phone, u.Role,
                    u.IsActive, u.CreatedDate, u.ChannelPartnerId, u.TenantId
                }).ToList();

                data["user_profiles"] = _db.UserProfiles.ToList();

                // SuperAdmins
                var superAdmins = await _masterDb.SuperAdmins.ToListAsync();
                data["super_admins"] = superAdmins.Select(s => new
                {
                    s.SuperAdminId, s.Email, s.FullName, s.IsActive, s.Role, s.LastLoginOn, s.CreatedOn
                }).ToList();

                // All tenant-scoped data
                data["agents"] = _db.Agents.ToList();
                data["channel_partners"] = _db.ChannelPartners.ToList();
                data["leads"] = _db.Leads.ToList();
                data["properties"] = _db.Properties.ToList();
                data["bookings"] = _db.Bookings.ToList();
                data["payments"] = _db.Payments.ToList();
                data["followups"] = _db.FollowUps.ToList();
                data["expenses"] = _db.Expenses.ToList();
                data["revenues"] = _db.Revenues.ToList();
                data["notifications"] = _db.Notifications.ToList();
                data["testimonials"] = _db.Testimonials.ToList();
                data["settings"] = _db.Settings.ToList();
                data["brandings"] = _db.Branding.ToList();
                data["quotations"] = _db.Quotations.ToList();
                data["invoices"] = _db.Invoices.ToList();
                data["role_permissions"] = _db.RolePermissions.ToList();
                data["audit_logs"] = _db.AuditLogs.ToList();
                data["builders"] = _db.Builders.ToList();
                data["bank_accounts"] = _db.BankAccounts.ToList();
                data["inquiries"] = _db.Inquiries.ToList();

                // Master DB
                try
                {
                    var tenants = await _masterDb.Tenants.ToListAsync();
                    data["tenants"] = tenants.Select(t => new { t.TenantId, t.CompanyName, t.Subdomain, t.Plan, t.Referral, t.IsActive, t.IsSuspended, t.MaxUsers, t.CreatedOn }).ToList();
                }
                catch { data["tenants"] = new List<object>(); }

                try
                {
                    var plans = await _masterDb.SaasPlans.ToListAsync();
                    data["saas_plans"] = plans.Select(p => new { p.PlanId, p.PlanName, p.MonthlyPrice, p.YearlyPrice, p.MaxUsers, p.MaxAgents, p.MaxLeadsPerMonth, p.MaxPartners, p.IsActive }).ToList();
                }
                catch { data["saas_plans"] = new List<object>(); }

                try
                {
                    var subs = await _masterDb.TenantSubscriptions.ToListAsync();
                    data["tenant_subscriptions"] = subs.Select(s => new { s.TenantId, s.PlanId, s.BillingCycle, s.Amount, s.Status, s.StartDate, s.EndDate, s.AutoRenew }).ToList();
                }
                catch { data["tenant_subscriptions"] = new List<object>(); }

                try
                {
                    var inqs = await _masterDb.Inquiries.ToListAsync();
                    data["inquiries"] = inqs.Select(i => new { i.InquiryId, i.CompanyName, i.ContactPerson, i.Email, i.Phone, i.SelectedPlan, i.ReferralCode, i.Status, i.CreatedOn }).ToList();
                }
                catch { data["inquiries"] = new List<object>(); }

                try
                {
                    var refs = await _masterDb.ReferralEarnings.ToListAsync();
                    data["referral_earnings"] = refs.Select(r => new { r.Id, r.TenantId, r.ReferralCode, r.Type, r.Amount, r.Description, r.IsUsed, r.ReferredTenantId, r.CreatedOn }).ToList();
                }
                catch { data["referral_earnings"] = new List<object>(); }

                try { data["email_logs"] = await _db.EmailLogs.ToListAsync(); } catch { data["email_logs"] = new List<object>(); }
                try { data["email_templates"] = await _db.EmailTemplates.ToListAsync(); } catch { data["email_templates"] = new List<object>(); }
                try { data["attendance_logs"] = await _db.AttendanceLogs.ToListAsync(); } catch { data["attendance_logs"] = new List<object>(); }
                try { data["agent_attendances"] = await _db.AgentAttendances.ToListAsync(); } catch { data["agent_attendances"] = new List<object>(); }
                try { data["partner_leads"] = await _db.PartnerLeads.ToListAsync(); } catch { data["partner_leads"] = new List<object>(); }
                try { data["site_visits"] = await _db.SiteVisits.ToListAsync(); } catch { data["site_visits"] = new List<object>(); }
                try { data["campaigns"] = await _db.Campaigns.ToListAsync(); } catch { data["campaigns"] = new List<object>(); }
                try { data["support_tickets"] = await _db.SupportTickets.ToListAsync(); } catch { data["support_tickets"] = new List<object>(); }
                try { data["whatsapp_logs"] = await _db.WhatsAppLogs.ToListAsync(); } catch { data["whatsapp_logs"] = new List<object>(); }
                try { data["modules"] = await _db.Modules.ToListAsync(); } catch { data["modules"] = new List<object>(); }
                try { data["pages"] = await _db.Pages.ToListAsync(); } catch { data["pages"] = new List<object>(); }
                try { data["permissions"] = await _db.Permissions.ToListAsync(); } catch { data["permissions"] = new List<object>(); }
                try { data["saas_settings"] = await _masterDb.SaasSetting.ToListAsync(); } catch { data["saas_settings"] = new List<object>(); }
                try { data["saas_branding"] = await _masterDb.SaasBranding.ToListAsync(); } catch { data["saas_branding"] = new List<object>(); }

                var result = new
                {
                    exportDate = DateTime.UtcNow.ToString("o"),
                    exportedBy = User.Identity?.Name ?? "SuperAdmin",
                    note = "Full multi-tenant export - all companies data",
                    totalCollections = data.Count,
                    totalDocuments = data.Values.Sum(v => v is System.Collections.IList li ? li.Count : 0),
                    summary = data.ToDictionary(k => k.Key, k => (object)(k.Value is System.Collections.IList li ? li.Count : 0)),
                    data
                };

                var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SuperAdmin export failed");
                return Json(new { success = false, error = ex.Message });
            }
        }

        // POST /api/export/seed - REMOVED: Use /api/seed/merge on SeedController instead
        // This endpoint created a conflicting set of users with @crm.app emails
        // alongside the canonical SeedController which uses @crm.com emails.
        [HttpPost("api/export/seed")]
        public async Task<IActionResult> SeedDemoData()
        {
            return Json(new { success = false, message = "This endpoint is deprecated. Use POST /api/seed/merge instead." });
        }


    }
}
