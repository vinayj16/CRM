using CRM;
using CRM.Middleware;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Bind to the $PORT env var when set (Railway / hosted platforms) so the platform
// proxy can reach the app; fall back to 5139 for local development and mobile/WebToApp access.
var listenPort = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var port) ? port : 5139;
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 200 * 1024 * 1024; // 200MB
    options.ListenAnyIP(listenPort); // http on 0.0.0.0 - allows mobile device connections
});
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 200 * 1024 * 1024; // 200MB
});

// Add services to the container
builder.Services.AddControllersWithViews();
builder.Services.AddControllers();
builder.Services.Configure<RouteOptions>(options =>
{
    options.LowercaseUrls = true;
    options.LowercaseQueryStrings = false;
});
builder.Services.AddHttpContextAccessor();

// Add CORS for mobile app access
builder.Services.AddCors(options =>
{
    options.AddPolicy("MobileApp", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders("Content-Disposition");
    });
});
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60); // Auto-logout after 60 min idle
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Add Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/account/login";
        options.LogoutPath = "/account/logout";
        options.AccessDeniedPath = "/account/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(24); // Auto-logout after 24 hours
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
    });

// ===== MongoDB-Only Data Layer =====
// Single MongoDB context (singleton - thread-safe)
builder.Services.AddSingleton<CRM.Services.MongoDbContext>();

// Repository registry for creating typed repositories on demand
builder.Services.AddSingleton<CRM.Services.MongoRepositoryRegistry>();

// AppDbContext & MasterDbContext - MongoDB-backed replacements for EF Core contexts
builder.Services.AddScoped<CRM.AppDbContext>();
builder.Services.AddScoped<CRM.MasterDb.MasterDbContext>();

// Tenant Service using MongoDB instead of MasterDbContext
builder.Services.AddScoped<CRM.Services.ITenantService, CRM.Services.MongoDbTenantService>();


// Register existing services (will be refactored to use IMongoRepository)
builder.Services.AddScoped<CRM.Services.INotificationService, CRM.Services.NotificationService>();
builder.Services.AddScoped<CRM.Services.PermissionService>();
builder.Services.AddScoped<CRM.Services.ChannelPartnerContextService>();
builder.Services.AddScoped<CRM.Services.ViewPermissionService>();
builder.Services.AddScoped<CRM.Services.BrandingService>();
builder.Services.AddScoped<CRM.Services.FcmService>();
builder.Services.AddScoped<CRM.Services.SubscriptionService>();
builder.Services.AddScoped<CRM.Services.RazorpayService>();
builder.Services.AddScoped<CRM.Services.EmailService>();
builder.Services.AddScoped<CRM.Services.PayoutService>();
builder.Services.AddScoped<CRM.Services.PayslipService>();
builder.Services.AddHttpClient<CRM.Services.IWhatsAppService, CRM.Services.WhatsAppService>();
builder.Services.AddHttpClient();

// Chatbot & Real-time Chat Services (use MongoDB via IMongoDbService)
builder.Services.AddScoped<CRM.Services.IMongoDbService, CRM.Services.MongoDbService>();
        builder.Services.AddScoped<CRM.Services.IChatbotService, CRM.Services.ChatbotService>();
        builder.Services.AddScoped<CRM.Services.LeadScoringService>();

// SignalR for real-time chat
builder.Services.AddSignalR();

// Background Services
builder.Services.AddHostedService<CRM.Services.MonthlyPayoutBackgroundService>();
builder.Services.AddHostedService<CRM.Services.ScheduledNotificationInitializerService>();
builder.Services.AddHostedService<CRM.Services.FollowUpNotificationService>();
builder.Services.AddHostedService<CRM.Services.PendingApprovalReminderService>();
builder.Services.AddHostedService<CRM.BackgroundServices.LeadIntegrationBackgroundService>();

var app = builder.Build();

// Initialize MongoDB indexes and seed data on startup
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        // Create MongoDB indexes
        var mongoContext = scope.ServiceProvider.GetRequiredService<CRM.Services.MongoDbContext>();
        await mongoContext.EnsureIndexesAsync();                        logger.LogInformation("MongoDB indexes created successfully.");

                        // Seed Modules and Pages for permission system
                        try
                        {
                            var appDb = scope.ServiceProvider.GetRequiredService<CRM.AppDbContext>();
                            var existingModules = await appDb.Modules.CountAsync();
                            if (existingModules == 0)
                            {
                                logger.LogInformation("Seeding Modules and Pages for permission system...");

                                // Define modules
                                var modules = new List<CRM.Models.ModuleModel>
                                {
                                    new() { ModuleId = 1, ModuleName = "Dashboard", DisplayName = "Dashboard", Icon = "fas fa-tachometer-alt", SortOrder = 1, IsActive = true },
                                    new() { ModuleId = 2, ModuleName = "Leads", DisplayName = "Leads & Properties", Icon = "fas fa-folder", SortOrder = 2, IsActive = true },
                                    new() { ModuleId = 3, ModuleName = "Sales", DisplayName = "Sales", Icon = "fas fa-shopping-cart", SortOrder = 3, IsActive = true },
                                    new() { ModuleId = 4, ModuleName = "Finance", DisplayName = "Finance", Icon = "fas fa-credit-card", SortOrder = 4, IsActive = true },
                                    new() { ModuleId = 5, ModuleName = "Team", DisplayName = "Team Management", Icon = "fas fa-users", SortOrder = 5, IsActive = true },
                                    new() { ModuleId = 6, ModuleName = "Attendance", DisplayName = "Attendance", Icon = "fas fa-calendar", SortOrder = 6, IsActive = true },
                                    new() { ModuleId = 7, ModuleName = "Payouts", DisplayName = "Payouts", Icon = "fas fa-credit-card", SortOrder = 7, IsActive = true },
                                    new() { ModuleId = 8, ModuleName = "Users", DisplayName = "User Management", Icon = "fas fa-sliders", SortOrder = 8, IsActive = true },
                                    new() { ModuleId = 9, ModuleName = "Settings", DisplayName = "Settings", Icon = "fas fa-cog", SortOrder = 9, IsActive = true },
                                    new() { ModuleId = 10, ModuleName = "Subscriptions", DisplayName = "Subscriptions", Icon = "fas fa-credit-card", SortOrder = 10, IsActive = true },
                                    new() { ModuleId = 11, ModuleName = "FinancialSettings", DisplayName = "Financial Settings", Icon = "fas fa-coins", SortOrder = 11, IsActive = true },
                                    new() { ModuleId = 12, ModuleName = "Testimonials", DisplayName = "Testimonials", Icon = "fas fa-star", SortOrder = 12, IsActive = true },
                                    new() { ModuleId = 13, ModuleName = "Integrations", DisplayName = "Integrations", Icon = "fas fa-plug", SortOrder = 13, IsActive = true },
                                };

                                foreach (var m in modules) appDb.Modules.Add(m);
                                await appDb.SaveChangesAsync();

                                // Define pages for each module
                                var pages = new List<CRM.Models.PageModel>
                                {
                                    // Dashboard
                                    new() { PageId = 1, ModuleId = 1, PageName = "DashboardHome", DisplayName = "Dashboard Home", Controller = "Home", Action = "Index", SortOrder = 1, IsActive = true },
                                    new() { PageId = 2, ModuleId = 1, PageName = "SalesOverview", DisplayName = "Sales Overview", Controller = "Home", Action = "SalesOverview", SortOrder = 2, IsActive = true },
                                    new() { PageId = 3, ModuleId = 1, PageName = "TeamDashboard", DisplayName = "Team Dashboard", Controller = "Home", Action = "TeamDashboard", SortOrder = 3, IsActive = true },
                                    // Leads & Properties
                                    new() { PageId = 4, ModuleId = 2, PageName = "LeadsList", DisplayName = "Leads", Controller = "Leads", Action = "Index", SortOrder = 1, IsActive = true },
                                    new() { PageId = 5, ModuleId = 2, PageName = "SalesPipeline", DisplayName = "Sales Pipeline", Controller = "SalesPipelines", Action = "Index", SortOrder = 2, IsActive = true },
                                    new() { PageId = 6, ModuleId = 2, PageName = "Tasks", DisplayName = "Tasks", Controller = "Tasks", Action = "Index", SortOrder = 3, IsActive = true },
                                    new() { PageId = 7, ModuleId = 2, PageName = "UnassignedLeads", DisplayName = "Unassigned Leads", Controller = "WebhookLeads", Action = "Index", SortOrder = 4, IsActive = true },
                                    new() { PageId = 8, ModuleId = 2, PageName = "Properties", DisplayName = "Properties", Controller = "Properties", Action = "Index", SortOrder = 5, IsActive = true },
                                    // Sales
                                    new() { PageId = 9, ModuleId = 3, PageName = "Quotations", DisplayName = "Quotations", Controller = "Quotations", Action = "Index", SortOrder = 1, IsActive = true },
                                    new() { PageId = 10, ModuleId = 3, PageName = "Bookings", DisplayName = "Bookings", Controller = "Bookings", Action = "Index", SortOrder = 2, IsActive = true },
                                    new() { PageId = 11, ModuleId = 3, PageName = "Invoices", DisplayName = "Invoices", Controller = "Invoices", Action = "Index", SortOrder = 3, IsActive = true },
                                    new() { PageId = 12, ModuleId = 3, PageName = "Payments", DisplayName = "Payments", Controller = "Payments", Action = "Index", SortOrder = 4, IsActive = true },
                                    // Finance
                                    new() { PageId = 13, ModuleId = 4, PageName = "Expenses", DisplayName = "Expenses", Controller = "Expenses", Action = "Index", SortOrder = 1, IsActive = true },
                                    new() { PageId = 14, ModuleId = 4, PageName = "Revenue", DisplayName = "Revenue", Controller = "Revenue", Action = "Index", SortOrder = 2, IsActive = true },
                                    new() { PageId = 15, ModuleId = 4, PageName = "Profit", DisplayName = "Profit", Controller = "Profit", Action = "Index", SortOrder = 3, IsActive = true },
                                    // Team Management
                                    new() { PageId = 16, ModuleId = 5, PageName = "AgentList", DisplayName = "Agent List", Controller = "Agent", Action = "List", SortOrder = 1, IsActive = true },
                                    new() { PageId = 17, ModuleId = 5, PageName = "ChannelPartner", DisplayName = "Channel Partner", Controller = "ManageUsers", Action = "PartnerApproval", SortOrder = 2, IsActive = true },
                                    // Attendance
                                    new() { PageId = 18, ModuleId = 6, PageName = "MyAttendance", DisplayName = "My Attendance", Controller = "Attendance", Action = "Calendar", SortOrder = 1, IsActive = true },
                                    new() { PageId = 19, ModuleId = 6, PageName = "AgentAttendance", DisplayName = "Agent Attendance", Controller = "Attendance", Action = "AgentList", SortOrder = 2, IsActive = true },
                                    // Payouts
                                    new() { PageId = 20, ModuleId = 7, PageName = "AgentPayouts", DisplayName = "Agent Payouts", Controller = "AgentPayout", Action = "Index", SortOrder = 1, IsActive = true },
                                    new() { PageId = 21, ModuleId = 7, PageName = "PartnerPayouts", DisplayName = "Partner Payouts", Controller = "PartnerCommission", Action = "Index", SortOrder = 2, IsActive = true },
                                    // User Management
                                    new() { PageId = 22, ModuleId = 8, PageName = "ManageUsers", DisplayName = "Manage Users", Controller = "ManageUsers", Action = "Index", SortOrder = 1, IsActive = true },
                                    new() { PageId = 23, ModuleId = 8, PageName = "RolesManagement", DisplayName = "Roles Management", Controller = "ManageUsers", Action = "Roles", SortOrder = 2, IsActive = true },
                                    // Settings
                                    new() { PageId = 24, ModuleId = 9, PageName = "Profile", DisplayName = "My Profile", Controller = "Profile", Action = "Index", SortOrder = 1, IsActive = true },
                                    new() { PageId = 25, ModuleId = 9, PageName = "SystemSettings", DisplayName = "System Settings", Controller = "Settings", Action = "Index", SortOrder = 2, IsActive = true },
                                    // Subscriptions
                                    new() { PageId = 26, ModuleId = 10, PageName = "MyPlan", DisplayName = "My Plan", Controller = "Subscription", Action = "MyPlan", SortOrder = 1, IsActive = true },
                                    // Testimonials
                                    new() { PageId = 27, ModuleId = 12, PageName = "Testimonials", DisplayName = "Testimonials", Controller = "Testimonials", Action = "Index", SortOrder = 1, IsActive = true },
                                    // Integrations
                                    new() { PageId = 28, ModuleId = 13, PageName = "Integrations", DisplayName = "Integrations", Controller = "Integrations", Action = "Index", SortOrder = 1, IsActive = true },
                                };

                                foreach (var p in pages) appDb.Pages.Add(p);
                                await appDb.SaveChangesAsync();

                                // Seed default permissions
                                var existingPermissions = await appDb.Permissions.CountAsync();
                                if (existingPermissions == 0)
                                {
                                    var perms = new List<CRM.Models.PermissionModel>
                                    {
                                        new() { PermissionId = 1, PermissionName = "View", DisplayName = "View", SortOrder = 1, IsActive = true },
                                        new() { PermissionId = 2, PermissionName = "Create", DisplayName = "Create", SortOrder = 2, IsActive = true },
                                        new() { PermissionId = 3, PermissionName = "Edit", DisplayName = "Edit", SortOrder = 3, IsActive = true },
                                        new() { PermissionId = 4, PermissionName = "Delete", DisplayName = "Delete", SortOrder = 4, IsActive = true },
                                        new() { PermissionId = 5, PermissionName = "Export", DisplayName = "Export", SortOrder = 5, IsActive = true },
                                        new() { PermissionId = 6, PermissionName = "BulkUpload", DisplayName = "Bulk Upload", SortOrder = 6, IsActive = true },
                                        new() { PermissionId = 7, PermissionName = "Approve", DisplayName = "Approve", SortOrder = 7, IsActive = true },
                                    };
                                    foreach (var perm in perms) appDb.Permissions.Add(perm);
                                    await appDb.SaveChangesAsync();
                                    logger.LogInformation("Seeded {Count} default permissions.", perms.Count);
                                }

                                logger.LogInformation("Seeded {ModuleCount} modules and {PageCount} pages for permission system.", modules.Count, pages.Count);
                            }
                            else
                            {
                                logger.LogInformation("Modules already seeded ({Count} existing), skipping.", existingModules);
                            }

                            // Seed RolePagePermissions for Sales and Agent roles (runs every startup, checks if already seeded)
                            try
                            {
                                // Remove any stale records with _id: 0 from previous failed seed
                                var staleRecords = await appDb.RolePagePermissions.Where(r => r.Id == 0).ToListAsync();
                                if (staleRecords.Any())
                                {
                                    foreach (var stale in staleRecords) appDb.RolePagePermissions.Remove(stale);
                                    await appDb.SaveChangesAsync();
                                    logger.LogInformation("Removed {Count} stale RolePagePermission records with _id: 0", staleRecords.Count);
                                }

                                var existingRolePagePerms = await appDb.RolePagePermissions.CountAsync();
                                if (existingRolePagePerms == 0)
                                {
                                    var allPerms = await appDb.Permissions.Where(p => p.IsActive).ToListAsync();
                                    var viewPerm = allPerms.FirstOrDefault(p => p.PermissionName == "View");
                                    var createPerm = allPerms.FirstOrDefault(p => p.PermissionName == "Create");
                                    var editPerm = allPerms.FirstOrDefault(p => p.PermissionName == "Edit");
                                    if (viewPerm != null)
                                    {
                                        var allPages = await appDb.Pages.Where(p => p.IsActive).ToListAsync();
                                        int nextRppId = 1;

                                        // Sales role: View + Create + Edit on Leads, SalesPipeline, Tasks, Quotations, Bookings, Invoices, Payments
                                        var salesControllers = new[] { "Leads", "SalesPipelines", "Tasks", "Quotations", "Bookings", "Invoices", "Payments" };
                                        var salesPages = allPages.Where(p => salesControllers.Contains(p.Controller)).ToList();
                                        foreach (var page in salesPages)
                                        {
                                            appDb.RolePagePermissions.Add(new CRM.Models.RolePagePermissionModel
                                            {
                                                Id = nextRppId++, RoleName = "Sales", PageId = page.PageId,
                                                PermissionId = viewPerm.PermissionId, IsGranted = true,
                                                CreatedBy = "System", ChannelPartnerId = null
                                            });
                                            if (createPerm != null && page.Controller != "Invoices" && page.Controller != "Payments")
                                            {
                                                appDb.RolePagePermissions.Add(new CRM.Models.RolePagePermissionModel
                                                {
                                                    Id = nextRppId++, RoleName = "Sales", PageId = page.PageId,
                                                    PermissionId = createPerm.PermissionId, IsGranted = true,
                                                    CreatedBy = "System", ChannelPartnerId = null
                                                });
                                            }
                                            if (editPerm != null)
                                            {
                                                appDb.RolePagePermissions.Add(new CRM.Models.RolePagePermissionModel
                                                {
                                                    Id = nextRppId++, RoleName = "Sales", PageId = page.PageId,
                                                    PermissionId = editPerm.PermissionId, IsGranted = true,
                                                    CreatedBy = "System", ChannelPartnerId = null
                                                });
                                            }
                                        }

                                        // Agent role: View on Leads, SalesPipeline, Tasks, Properties
                                        var agentControllers = new[] { "Leads", "SalesPipelines", "Tasks", "Properties" };
                                        var agentPages = allPages.Where(p => agentControllers.Contains(p.Controller)).ToList();
                                        foreach (var page in agentPages)
                                        {
                                            appDb.RolePagePermissions.Add(new CRM.Models.RolePagePermissionModel
                                            {
                                                Id = nextRppId++, RoleName = "Agent", PageId = page.PageId,
                                                PermissionId = viewPerm.PermissionId, IsGranted = true,
                                                CreatedBy = "System", ChannelPartnerId = null
                                            });
                                        }

                                await appDb.SaveChangesAsync();
                                logger.LogInformation("Seeded default RolePagePermissions for Sales and Agent roles (by Controller lookup).");
                            }
                        }

                        // Seed RolePermissions.AllowedModules for Sales and Agent (controls sidebar visibility)
                        // IMPORTANT: MongoDbSet.Add/Remove persist immediately. SaveChangesAsync is a no-op on MongoDB.
                        try
                        {
                            // Check if Sales/Agent already have valid AllowedModules
                            var existingSalesRp = await appDb.RolePermissions.FirstOrDefaultAsync(r => r.RoleName == "Sales");
                            var existingAgentRp = await appDb.RolePermissions.FirstOrDefaultAsync(r => r.RoleName == "Agent");
                            bool needsSeed = false;

                            if (existingSalesRp == null || string.IsNullOrEmpty(existingSalesRp.AllowedModules))
                            {
                                if (existingSalesRp != null) appDb.RolePermissions.Remove(existingSalesRp);
                                appDb.RolePermissions.Add(new CRM.Models.RolePermission
                                {
                                    Id = 1, RoleName = "Sales",
                                    AllowedModules = "Dashboard,Leads & Properties,Sales,Attendance,Settings",
                                    CanView = true, CanCreate = true, CanEdit = true,
                                    CreatedAt = CRM.Helpers.IndianTime.Now
                                });
                                needsSeed = true;
                            }

                            if (existingAgentRp == null || string.IsNullOrEmpty(existingAgentRp.AllowedModules))
                            {
                                if (existingAgentRp != null) appDb.RolePermissions.Remove(existingAgentRp);
                                appDb.RolePermissions.Add(new CRM.Models.RolePermission
                                {
                                    Id = 2, RoleName = "Agent",
                                    AllowedModules = "Dashboard,Leads & Properties,Sales,Attendance,Settings",
                                    CanView = true,
                                    CreatedAt = CRM.Helpers.IndianTime.Now
                                });
                                needsSeed = true;
                            }

                            if (needsSeed)
                            {
                                logger.LogInformation("Seeded/updated RolePermissions.AllowedModules for Sales and Agent — sidebar sections now visible.");
                            }
                            else
                            {
                                logger.LogInformation("RolePermissions already exist with AllowedModules for Sales/Agent. Skipping.");
                            }
                        }
                        catch (Exception rpEx)
                        {
                            logger.LogWarning(rpEx, "Failed to seed RolePermissions. Sidebar sections may be hidden for Sales/Agent.");
                        }
                    }
                    catch (Exception rppEx)
                    {
                        logger.LogWarning(rppEx, "Failed to seed RolePagePermissions. Role-based access may be restricted.");
                    }
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Failed to seed modules/pages. Permissions page may show 'No Modules Found'.");
                        }

                        // Seed default email templates
        try
        {
            var appDb = scope.ServiceProvider.GetRequiredService<CRM.AppDbContext>();
            var existingTemplates = await appDb.EmailTemplates.CountAsync();
            if (existingTemplates == 0)
            {
                var defaultTemplates = new List<CRM.Models.EmailTemplateModel>
                {
                    new CRM.Models.EmailTemplateModel
                    {
                        TemplateName = "WelcomeEmail",
                        Subject = "Welcome to {CompanyName} - Your Account is Ready!",
                        BodyHtml = @"<div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto'>
                            <div style='background:linear-gradient(135deg,#667eea,#764ba2);padding:30px;text-align:center'>
                                <h1 style='color:white;margin:0'>Welcome to {CompanyName}!</h1>
                            </div>
                            <div style='background:#f8f9fa;padding:30px;border:1px solid #dee2e6'>
                                <h2 style='color:#333'>Hello {Name},</h2>
                                <p style='color:#555;line-height:1.6'>Your account has been created successfully. You can now log in and start using all the features.</p>
                                <div style='background:white;padding:20px;border-radius:8px;margin:20px 0;border-left:4px solid #28a745'>
                                    <p style='color:#555;margin:5px 0'><strong>Login URL:</strong> <a href='{LoginUrl}' style='color:#667eea'>{LoginUrl}</a></p>
                                </div>
                                <div style='margin:30px 0;text-align:center'>
                                    <a href='{LoginUrl}' style='display:inline-block;padding:15px 40px;background:linear-gradient(135deg,#667eea,#764ba2);color:white;text-decoration:none;border-radius:5px;font-weight:bold'>Access Dashboard</a>
                                </div>
                            </div>
                            <div style='background:#343a40;padding:20px;text-align:center;color:#adb5bd;font-size:12px'>
                                <p style='margin:5px 0'>© {Year} {CompanyName}. All rights reserved.</p>
                            </div>
                        </div>",
                        Variables = "CompanyName, Name, LoginUrl, Year",
                        IsActive = true,
                        CreatedOn = CRM.Helpers.IndianTime.Now,
                        UpdatedOn = CRM.Helpers.IndianTime.Now
                    },
                    new CRM.Models.EmailTemplateModel
                    {
                        TemplateName = "PartnerWelcome",
                        Subject = "Welcome to {CompanyName} - Channel Partner Access Granted",
                        BodyHtml = @"<div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto'>
                            <div style='background:linear-gradient(135deg,#667eea,#764ba2);padding:30px;text-align:center'>
                                <h1 style='color:white;margin:0'>Welcome to {CompanyName}!</h1>
                            </div>
                            <div style='background:#f8f9fa;padding:30px;border:1px solid #dee2e6'>
                                <h2 style='color:#333'>Hello {Name},</h2>
                                <p style='color:#555;line-height:1.6'>Congratulations! Your Channel Partner account has been created successfully.</p>
                                <div style='background:white;padding:20px;border-radius:8px;margin:20px 0;border-left:4px solid #28a745'>
                                    <h3 style='color:#28a745;margin-top:0'>Your Login Credentials</h3>
                                    <p style='color:#555;margin:5px 0'><strong>Username:</strong> {Username}</p>
                                    <p style='color:#555;margin:5px 0'><strong>Password:</strong> <code style='background:#f8f9fa;padding:4px 8px;border-radius:4px;color:#e83e8c'>{Password}</code></p>
                                    <p style='color:#555;margin:5px 0'><strong>Plan:</strong> {PlanName}</p>
                                    <p style='color:#555;margin:5px 0'><strong>Trial Ends:</strong> {TrialEndDate}</p>
                                </div>
                                <div style='background:#fff3cd;padding:15px;border-radius:8px;margin:20px 0;border-left:4px solid #ffc107'>
                                    <p style='color:#856404;margin:0'>Please change your password after first login for security purposes.</p>
                                </div>
                                <div style='margin:30px 0;text-align:center'>
                                    <a href='{LoginUrl}' style='display:inline-block;padding:15px 40px;background:linear-gradient(135deg,#667eea,#764ba2);color:white;text-decoration:none;border-radius:5px;font-weight:bold'>Access Dashboard</a>
                                </div>
                            </div>
                            <div style='background:#343a40;padding:20px;text-align:center;color:#adb5bd;font-size:12px'>
                                <p style='margin:5px 0'>© {Year} {CompanyName}. All rights reserved.</p>
                            </div>
                        </div>",
                        Variables = "CompanyName, Name, Username, Password, PlanName, TrialEndDate, LoginUrl, Year",
                        IsActive = true,
                        CreatedOn = CRM.Helpers.IndianTime.Now,
                        UpdatedOn = CRM.Helpers.IndianTime.Now
                    },
                    new CRM.Models.EmailTemplateModel
                    {
                        TemplateName = "PaymentRequired",
                        Subject = "Payment Required: {PlanName} Plan Upgrade",
                        BodyHtml = @"<div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto'>
                            <div style='background:linear-gradient(135deg,#f093fb,#f5576c);padding:30px;text-align:center'>
                                <h1 style='color:white;margin:0'>Payment Required</h1>
                            </div>
                            <div style='background:#f8f9fa;padding:30px;border:1px solid #dee2e6'>
                                <h2 style='color:#333'>Dear {Name},</h2>
                                <p style='color:#555;line-height:1.6'>Please complete the payment to activate your new plan.</p>
                                <div style='background:white;padding:20px;border-radius:8px;margin:20px 0;border-left:4px solid #f5576c'>
                                    <p style='color:#555;margin:5px 0'><strong>Plan:</strong> {PlanName}</p>
                                    <p style='color:#555;margin:5px 0'><strong>Billing:</strong> {BillingCycle}</p>
                                    <p style='color:#555;margin:5px 0'><strong>Amount:</strong> Rs. {Amount}</p>
                                </div>
                                <div style='margin:30px 0;text-align:center'>
                                    <a href='{PaymentLink}' style='display:inline-block;padding:15px 40px;background:linear-gradient(135deg,#f093fb,#f5576c);color:white;text-decoration:none;border-radius:5px;font-weight:bold'>Complete Payment</a>
                                </div>
                            </div>
                            <div style='background:#343a40;padding:20px;text-align:center;color:#adb5bd;font-size:12px'>
                                <p style='margin:5px 0'>© {Year} {CompanyName}. All rights reserved.</p>
                            </div>
                        </div>",
                        Variables = "PlanName, BillingCycle, Amount, Name, PaymentLink, CompanyName, Year",
                        IsActive = true,
                        CreatedOn = CRM.Helpers.IndianTime.Now,
                        UpdatedOn = CRM.Helpers.IndianTime.Now
                    },
                    new CRM.Models.EmailTemplateModel
                    {
                        TemplateName = "PlanChangeNotification",
                        Subject = "Your Plan Has Been Updated to {PlanName}",
                        BodyHtml = @"<div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto'>
                            <div style='background:linear-gradient(135deg,#11998e,#38ef7d);padding:30px;text-align:center'>
                                <h1 style='color:white;margin:0'>Plan Updated!</h1>
                            </div>
                            <div style='background:#f8f9fa;padding:30px;border:1px solid #dee2e6'>
                                <h2 style='color:#333'>Dear {Name},</h2>
                                <p style='color:#555;line-height:1.6'>Your subscription plan has been updated successfully.</p>
                                <div style='background:white;padding:20px;border-radius:8px;margin:20px 0;border-left:4px solid #11998e'>
                                    <p style='color:#555;margin:5px 0'><strong>New Plan:</strong> {PlanName}</p>
                                    <p style='color:#555;margin:5px 0'><strong>Billing Cycle:</strong> {BillingCycle}</p>
                                    <p style='color:#555;margin:5px 0'><strong>Amount:</strong> Rs. {Amount}</p>
                                </div>
                                <div style='margin:30px 0;text-align:center'>
                                    <a href='{DashboardUrl}' style='display:inline-block;padding:15px 40px;background:linear-gradient(135deg,#11998e,#38ef7d);color:white;text-decoration:none;border-radius:5px;font-weight:bold'>View Dashboard</a>
                                </div>
                            </div>
                            <div style='background:#343a40;padding:20px;text-align:center;color:#adb5bd;font-size:12px'>
                                <p style='margin:5px 0'>© {Year} {CompanyName}. All rights reserved.</p>
                            </div>
                        </div>",
                        Variables = "PlanName, BillingCycle, Amount, Name, DashboardUrl, CompanyName, Year",
                        IsActive = true,
                        CreatedOn = CRM.Helpers.IndianTime.Now,
                        UpdatedOn = CRM.Helpers.IndianTime.Now
                    },
                    new CRM.Models.EmailTemplateModel
                    {
                        TemplateName = "TeamMemberAdded",
                        Subject = "Welcome to {CompanyName} - You've been added as {Role}",
                        BodyHtml = @"<div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto'>
                            <div style='background:linear-gradient(135deg,#667eea,#764ba2);padding:30px;text-align:center'>
                                <h1 style='color:white;margin:0'>Welcome Aboard!</h1>
                            </div>
                            <div style='background:#f8f9fa;padding:30px;border:1px solid #dee2e6'>
                                <h2 style='color:#333'>Hello {Name},</h2>
                                <p style='color:#555;line-height:1.6'>You have been added as a <strong>{Role}</strong> to <strong>{CompanyName}</strong>.</p>
                                <div style='background:white;padding:20px;border-radius:8px;margin:20px 0;border-left:4px solid #667eea'>
                                    <p style='color:#555;margin:5px 0'><strong>Email:</strong> {Email}</p>
                                    <p style='color:#555;margin:5px 0'>Please check with your admin for your login credentials or use the 'Forgot Password' option.</p>
                                </div>
                                <div style='margin:30px 0;text-align:center'>
                                    <a href='{LoginUrl}' style='display:inline-block;padding:15px 40px;background:linear-gradient(135deg,#667eea,#764ba2);color:white;text-decoration:none;border-radius:5px;font-weight:bold'>Log In Now</a>
                                </div>
                            </div>
                            <div style='background:#343a40;padding:20px;text-align:center;color:#adb5bd;font-size:12px'>
                                <p style='margin:5px 0'>© {Year} {CompanyName}. All rights reserved.</p>
                            </div>
                        </div>",
                        Variables = "CompanyName, Name, Role, Email, LoginUrl, Year",
                        IsActive = true,
                        CreatedOn = CRM.Helpers.IndianTime.Now,
                        UpdatedOn = CRM.Helpers.IndianTime.Now
                    }
                };

                foreach (var template in defaultTemplates)
                {
                    appDb.EmailTemplates.Add(template);
                }
                await appDb.SaveChangesAsync();
                logger.LogInformation("Seeded {Count} default email templates.", defaultTemplates.Count);
            }
            else
            {
                logger.LogInformation("Email templates already seeded ({Count} existing), skipping.", existingTemplates);
            }

            // Seed exactly 4 SaaS plans (Free, Basic, Standard, Premium)
            try
            {
                var masterDb = scope.ServiceProvider.GetRequiredService<CRM.MasterDb.MasterDbContext>();
                var existingPlans = await masterDb.SaasPlans.CountAsync();
                if (existingPlans == 0)
                {
                    logger.LogInformation("Seeding 4 default SaaS plans...");
                    var now = DateTime.UtcNow;
                    masterDb.SaasPlans.Add(new CRM.MasterDb.Models.SaasSubscriptionPlanModel
                    {
                        PlanId = 1, PlanName = "Free", Description = "Get started with basic CRM features",
                        MonthlyPrice = 0m, YearlyPrice = 0m,
                        MaxUsers = 3, MaxAgents = 1, MaxLeadsPerMonth = 100, MaxPartners = 0,
                        HasWhatsAppIntegration = false, HasEmailIntegration = true, HasAdvancedReports = false,
                        HasImpersonation = false, IsActive = true, SortOrder = 1, ShowOnLandingPage = true,
                        CreatedOn = now
                    });
                    masterDb.SaasPlans.Add(new CRM.MasterDb.Models.SaasSubscriptionPlanModel
                    {
                        PlanId = 2, PlanName = "Basic Plan", Description = "Essential features for small teams",
                        MonthlyPrice = 999.00m, YearlyPrice = 9999.00m,
                        MaxUsers = 10, MaxAgents = 5, MaxLeadsPerMonth = 1000, MaxPartners = 2,
                        HasWhatsAppIntegration = true, HasEmailIntegration = true, HasAdvancedReports = true,
                        HasImpersonation = true, IsActive = true, SortOrder = 2, ShowOnLandingPage = true,
                        CreatedOn = now
                    });
                    masterDb.SaasPlans.Add(new CRM.MasterDb.Models.SaasSubscriptionPlanModel
                    {
                        PlanId = 3, PlanName = "Standard Plan", Description = "Advanced features for growing teams",
                        MonthlyPrice = 2499.00m, YearlyPrice = 24999.00m,
                        MaxUsers = 25, MaxAgents = 15, MaxLeadsPerMonth = 5000, MaxPartners = 5,
                        HasWhatsAppIntegration = true, HasEmailIntegration = true, HasFacebookIntegration = true,
                        HasAdvancedReports = true, HasCustomBranding = true, HasPrioritySupport = true,
                        HasImpersonation = true, SupportLevel = "Chat", PlanType = "Standard",
                        IsActive = true, SortOrder = 3, ShowOnLandingPage = true, CreatedOn = now
                    });
                    masterDb.SaasPlans.Add(new CRM.MasterDb.Models.SaasSubscriptionPlanModel
                    {
                        PlanId = 4, PlanName = "Premium Plan", Description = "Full access for large enterprises",
                        MonthlyPrice = 4999.00m, YearlyPrice = 49999.00m,
                        MaxUsers = -1, MaxAgents = -1, MaxLeadsPerMonth = -1, MaxPartners = -1,
                        HasWhatsAppIntegration = true, HasEmailIntegration = true, HasFacebookIntegration = true,
                        HasCustomAPIAccess = true, HasAdvancedReports = true, HasCustomBranding = true,
                        HasPrioritySupport = true, HasImpersonation = true, SupportLevel = "Dedicated", PlanType = "Premium",
                        IsActive = true, SortOrder = 4, ShowOnLandingPage = true, CreatedOn = now
                    });
                    await masterDb.SaveChangesAsync();
                    logger.LogInformation("Seeded 4 default SaaS plans (Free, Basic, Standard, Premium).");
                }
                else
                {
                    logger.LogInformation("SaaS plans already exist ({Count} plans). Merging to 4 unique plans...", existingPlans);
                    var allPlans = await masterDb.SaasPlans.OrderBy(p => p.SortOrder).ToListAsync();
                    var canonicalNames = new[] { "Free", "Basic Plan", "Standard Plan", "Premium Plan" };
                    int newPlanId = 1;
                    foreach (var canonical in canonicalNames)
                    {
                        var matches = allPlans.Where(p => p.PlanName.Equals(canonical, StringComparison.OrdinalIgnoreCase) ||
                            (canonical == "Basic Plan" && p.PlanName.Equals("Basic", StringComparison.OrdinalIgnoreCase)) ||
                            (canonical == "Standard Plan" && p.PlanName.Equals("Standard", StringComparison.OrdinalIgnoreCase)) ||
                            (canonical == "Premium Plan" && p.PlanName.Equals("Premium", StringComparison.OrdinalIgnoreCase)) ||
                            (canonical == "Free" && p.PlanName.Equals("Free", StringComparison.OrdinalIgnoreCase)))
                            .OrderBy(p => p.SortOrder)
                            .ToList();
                        if (!matches.Any())
                        {
                            masterDb.SaasPlans.Add(new CRM.MasterDb.Models.SaasSubscriptionPlanModel
                            {
                                PlanId = newPlanId,
                                PlanName = canonical,
                                Description = canonical switch
                                {
                                    "Free" => "Get started with basic CRM features",
                                    "Basic Plan" => "Essential features for small teams",
                                    "Standard Plan" => "Advanced features for growing teams",
                                    "Premium Plan" => "Full access for large enterprises",
                                    _ => ""
                                },
                                MonthlyPrice = canonical switch
                                {
                                    "Free" => 0m,
                                    "Basic Plan" => 999.00m,
                                    "Standard Plan" => 2499.00m,
                                    "Premium Plan" => 4999.00m,
                                    _ => 0m
                                },
                                YearlyPrice = canonical switch
                                {
                                    "Free" => 0m,
                                    "Basic Plan" => 9999.00m,
                                    "Standard Plan" => 24999.00m,
                                    "Premium Plan" => 49999.00m,
                                    _ => 0m
                                },
                                MaxUsers = canonical switch
                                {
                                    "Free" => 3,
                                    "Basic Plan" => 10,
                                    "Standard Plan" => 25,
                                    "Premium Plan" => -1,
                                    _ => 0
                                },
                                MaxAgents = canonical switch
                                {
                                    "Free" => 1,
                                    "Basic Plan" => 5,
                                    "Standard Plan" => 15,
                                    "Premium Plan" => -1,
                                    _ => 0
                                },
                                MaxLeadsPerMonth = canonical switch
                                {
                                    "Free" => 100,
                                    "Basic Plan" => 1000,
                                    "Standard Plan" => 5000,
                                    "Premium Plan" => -1,
                                    _ => 0
                                },
                                MaxPartners = canonical switch
                                {
                                    "Free" => 0,
                                    "Basic Plan" => 2,
                                    "Standard Plan" => 5,
                                    "Premium Plan" => -1,
                                    _ => 0
                                },
                                HasWhatsAppIntegration = canonical == "Basic Plan" || canonical == "Standard Plan" || canonical == "Premium Plan",
                                HasEmailIntegration = true,
                                HasFacebookIntegration = canonical == "Standard Plan" || canonical == "Premium Plan",
                                HasAdvancedReports = canonical == "Basic Plan" || canonical == "Standard Plan" || canonical == "Premium Plan",
                                HasCustomBranding = canonical == "Standard Plan" || canonical == "Premium Plan",
                                HasPrioritySupport = canonical == "Standard Plan" || canonical == "Premium Plan",
                                HasImpersonation = canonical == "Basic Plan" || canonical == "Standard Plan" || canonical == "Premium Plan",
                                SupportLevel = canonical == "Premium Plan" ? "Dedicated" : (canonical == "Standard Plan" ? "Chat" : "Email"),
                                PlanType = canonical == "Premium Plan" ? "Premium" : (canonical == "Standard Plan" ? "Standard" : (canonical == "Basic Plan" ? "Basic" : "Free")),
                                IsActive = true,
                                SortOrder = newPlanId,
                                ShowOnLandingPage = true,
                                CreatedOn = DateTime.UtcNow
                            });
                            newPlanId++;
                            continue;
                        }
                        var primary = matches.First();
                        var duplicates = matches.Skip(1).ToList();
                        if (duplicates.Any())
                        {
                            masterDb.SaasPlans.RemoveRange(duplicates);
                        }
                        primary.PlanName = canonical;
                        primary.PlanId = newPlanId;
                        primary.SortOrder = newPlanId;
                        newPlanId++;
                    }
                    var extras = masterDb.SaasPlans.Where(p => !canonicalNames.Contains(p.PlanName) || p.PlanId > 4 || p.SortOrder > 4).ToList();
                    if (extras.Any())
                    {
                        masterDb.SaasPlans.RemoveRange(extras);
                    }
                    await masterDb.SaveChangesAsync();
                    logger.LogInformation("Plan cleanup complete. Current plans: {Count}", await masterDb.SaasPlans.CountAsync());
                }

                // Assign active subscriptions to ALL existing tenants (fixes "No Active Plan" issue)
                try
                {
                    var allTenants = await masterDb.Tenants.ToListAsync();
                    var allPlans = (await masterDb.SaasPlans.OrderBy(p => p.SortOrder).ToListAsync()).ToList();
                    var existingSubs = await masterDb.TenantSubscriptions.ToListAsync();
                    int nextSubId = existingSubs.Any() ? existingSubs.Max(s => s.SubscriptionId) + 1 : 1;
                    int assignedCount = 0;

                    foreach (var tenant in allTenants)
                    {
                        var hasActive = existingSubs.Any(s => s.TenantId == tenant.TenantId && (s.Status == "Active" || s.Status == "Trial"));
                        if (hasActive) continue;

                        // Tenant 1 (primary) gets Premium, others get Basic Plan
                        var plan = tenant.TenantId <= 1 && allPlans.Count >= 4
                            ? allPlans[3]  // Premium Plan
                            : allPlans.Count >= 2 ? allPlans[1] : allPlans.First();  // Basic Plan

                        masterDb.TenantSubscriptions.Add(new CRM.MasterDb.Models.TenantSubscriptionModel
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
                        });
                        assignedCount++;
                        logger.LogInformation("Assigned subscription: Tenant '{Tenant}' (ID={Tid}) → Plan '{Plan}' (₹{Price}/mo)",
                            tenant.CompanyName, tenant.TenantId, plan.PlanName, plan.MonthlyPrice);
                    }

                    if (assignedCount > 0)
                    {
                        await masterDb.SaveChangesAsync();
                        logger.LogInformation("Assigned active subscriptions to {Count} tenant(s). 'No Active Plan' issue fixed.", assignedCount);
                    }
                    else
                    {
                        logger.LogInformation("All {Count} tenants already have active subscriptions.", allTenants.Count);
                    }

                    // Fix users with TenantId=0 - assign them to Tenant 1
                    // Use raw MongoDB UpdateMany to avoid MongoDbSet.GetDocumentId() limitations for UserModel
                    var userFilter = MongoDB.Driver.Builders<CRM.Models.UserModel>.Filter.Eq(u => u.TenantId, 0);
                    var userUpdate = MongoDB.Driver.Builders<CRM.Models.UserModel>.Update.Set(u => u.TenantId, 1);
                    var usersUpdateResult = appDb.Users.Collection.UpdateMany(userFilter, userUpdate);
                    if (usersUpdateResult.ModifiedCount > 0)
                    {
                        logger.LogInformation("Fixed {Count} users with TenantId=0 → TenantId=1.", usersUpdateResult.ModifiedCount);
                    }

                    // Also ensure Tenant 0 (catch-all) has a subscription if any user needs it
                    var hasZeroSub = existingSubs.Any(s => s.TenantId == 0 && s.Status == "Active");
                    if (!hasZeroSub && allTenants.Any() && allPlans.Any())
                    {
                        var zeroPlan = allPlans.Count >= 4 ? allPlans[3] : allPlans.First(); // Premium or first
                        masterDb.TenantSubscriptions.Add(new CRM.MasterDb.Models.TenantSubscriptionModel
                        {
                            SubscriptionId = nextSubId++,
                            TenantId = 0,
                            PlanId = zeroPlan.PlanId,
                            BillingCycle = "Monthly",
                            Amount = zeroPlan.MonthlyPrice,
                            StartDate = DateTime.UtcNow,
                            EndDate = DateTime.UtcNow.AddYears(1),
                            Status = "Active",
                            AutoRenew = true,
                            CreatedOn = DateTime.UtcNow
                        });
                        await masterDb.SaveChangesAsync();
                        logger.LogInformation("Created catch-all subscription for TenantId=0 (legacy users).");
                    }
                }
                catch (Exception subEx)
                {
                    logger.LogWarning(subEx, "Failed to assign tenant subscriptions. 'No Active Plan' may appear.");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to seed/merge SaaS plans. App will continue.");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to seed email templates. App will continue.");
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "MongoDB initialization error. App will start but data may be unavailable.");
    }
}

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api"),
    appBuilder =>
    {
        appBuilder.UseStatusCodePagesWithReExecute("/home/statuscode/{0}");
    });

// HTTPS redirection disabled: app binds ONLY to http://localhost:5139
app.UseStaticFiles();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/chatHub") || context.Request.Path.StartsWithSegments("/chathub"))
    {
        await next();
        return;
    }

    var pathValue = context.Request.Path.Value;
        if (!string.IsNullOrEmpty(pathValue) && pathValue.Any(char.IsUpper))
        {
            var lowerPath = pathValue.ToLowerInvariant();

            if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            {
                var queryString = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : string.Empty;
                context.Response.Redirect($"{lowerPath}{queryString}", permanent: true);
                return;
            }

            context.Request.Path = lowerPath;
        }

        await next();
    });

// Middleware: Redirect single-segment controller URLs (e.g., /leads -> /leads/index)
// This handles the default route pattern {controller=Home}/{action=Landing}/{id?}
// where most controllers don't have a Landing() action
app.Use(async (context, next) =>
{
    await next();    if (context.Response.StatusCode == 404)
        {
            var path = context.Request.Path.Value?.Trim('/');
            if (!string.IsNullOrEmpty(path) && 
                !path.Contains('/') &&  // single segment only
                !path.Contains('.') &&  // skip file-like paths (favicon.ico, etc.)
                !path.Equals("home", StringComparison.OrdinalIgnoreCase) &&
                !path.Equals("account", StringComparison.OrdinalIgnoreCase) &&  // Account routes via Login action
                !path.Equals("chathub", StringComparison.OrdinalIgnoreCase) &&
                !path.Equals("api", StringComparison.OrdinalIgnoreCase) &&
                (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)))
            {
                // Single-segment GET/HEAD URL that 404'd — likely a controller without a Landing action
                context.Response.Redirect($"/{path}/Index{context.Request.QueryString}");
            }
        }
    });

app.UseCors("MobileApp");
app.UseRouting();
app.UseSession();

app.UseAuthentication();
app.UseSubdomainDetection();
app.UseAuthorization();
app.UseMaintenanceMode();
app.UseSaasTenantLimits();

app.MapHub<CRM.Hubs.RealTimeChatHub>("/chatHub");

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Landing}/{id?}");app.Run();