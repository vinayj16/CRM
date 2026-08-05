using CRM.Attributes;
using CRM.Helpers;
using CRM.Models;
using CRM.Services;
using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;

namespace CRM.Controllers
{
    [RoleAuthorize("Admin", "Partner", "Agent", "Sales")]
    public class AgentController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly SubscriptionService _subscriptionService;
        private readonly ILogger<AgentController> _logger;

        public AgentController(AppDbContext context, IWebHostEnvironment environment, IHttpContextAccessor httpContextAccessor, SubscriptionService subscriptionService, ILogger<AgentController> logger)
        {
            _context = context;
            _environment = environment;
            _httpContextAccessor = httpContextAccessor;
            _subscriptionService = subscriptionService;
            _logger = logger;
        }

        private (int? UserId, string? Role, int? ChannelPartnerId) GetCurrentUserContext()
        {
            var token = _httpContextAccessor.HttpContext?.Request.Cookies["jwtToken"];
            if (string.IsNullOrEmpty(token)) return (null, null, null);

            var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);
            var userIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            var role = jwt.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role)?.Value;

            if (!int.TryParse(userIdClaim, out int userId)) return (null, role, null);

            var user = _context.Users.FirstOrDefault(u => u.UserId == userId);
            return (userId, role, user?.ChannelPartnerId);
        }

        [HttpGet]
        public IActionResult CheckEmailExists(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return Json(new { exists = false });
            }

            var exists = _context.Agents.Any(a => a.Email == email) || _context.Users.Any(u => u.Email == email);
            return Json(new { exists = exists });
        }

        [HttpGet]
        public IActionResult Onboard()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Onboard(AgentModel model, List<IFormFile> DocumentFiles, List<string> DocumentNames, List<string> DocumentTypes)
        {
            try
            {
                // Remove validation for Documents field as we're using DocumentFiles now
                ModelState.Remove("Documents");
                ModelState.Remove("AgentDocuments");

                if (ModelState.IsValid)
                {
                    model.Status = "Pending";
                    model.CreatedOn = IndianTime.Now;

                    // Auto-assign ChannelPartnerId if current user is Partner
                    var (userId, role, channelPartnerId) = GetCurrentUserContext();
                    if (role?.ToLower() == "partner")
                    {
                        model.ChannelPartnerId = channelPartnerId;

                        // Check subscription limits for partners
                        var (canAdd, message) = await _subscriptionService.CanAddAgentAsync(channelPartnerId.Value);
                        if (!canAdd)
                        {
                            return Ok(new
                            {
                                success = false,
                                message = message,
                                showUpgrade = true,
                                upgradeUrl = "/Subscription/MyPlan"
                            });
                        }
                    }

                    _context.Agents.Add(model);
                    await _context.SaveChangesAsync();

                    // Handle file uploads - save to database with names and types
                    if (DocumentFiles != null && DocumentFiles.Any())
                    {
                        for (int i = 0; i < DocumentFiles.Count; i++)
                        {
                            var file = DocumentFiles[i];
                            if (file.Length > 0)
                            {
                                // Read file into byte array
                                using (var memoryStream = new MemoryStream())
                                {
                                    await file.CopyToAsync(memoryStream);
                                    var fileContent = memoryStream.ToArray();

                                    var documentName = DocumentNames != null && i < DocumentNames.Count ? DocumentNames[i] : Path.GetFileName(file.FileName);
                                    var documentType = DocumentTypes != null && i < DocumentTypes.Count ? DocumentTypes[i] : "General";

                                    var document = new AgentDocumentModel
                                    {
                                        AgentId = model.AgentId,
                                        FileName = Path.GetFileName(file.FileName),
                                        DocumentName = documentName,
                                        DocumentType = documentType,
                                        FileContent = fileContent,
                                        FileSize = file.Length,
                                        ContentType = file.ContentType,
                                        UploadedOn = IndianTime.Now
                                    };
                                    _context.AgentDocuments.Add(document);
                                }
                            }
                        }
                        await _context.SaveChangesAsync();
                    }

                    return Ok(new { success = true, message = "Agent onboarded successfully" });
                }

                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return Ok(new { success = false, message = "Validation failed", errors = errors });
            }
            catch (Exception ex)
            {
                return Ok(new { success = false, message = "Error: " + ex.Message });
            }
        }

        [HttpGet]
        /// <summary>
        /// Redirect /agent and /agent/index to /agent/list since AgentController uses List as the default action.
        /// </summary>
        public IActionResult Landing() => RedirectToAction("List");
        public IActionResult Index() => RedirectToAction("List");

        [HttpGet]
        public IActionResult List()
        {
            var (userId, role, channelPartnerId) = GetCurrentUserContext();

            IQueryable<AgentModel> agentsQuery = _context.Agents.AsQueryable();

            if (role?.ToLower() == "partner" && channelPartnerId.HasValue)
            {
                // Partner sees only their agents
                agentsQuery = agentsQuery.Where(a => a.ChannelPartnerId == channelPartnerId.Value);
            }
            else if (role?.ToLower() == "admin")
            {
                // Admin sees only their agents (agents with null ChannelPartnerId)
                agentsQuery = agentsQuery.Where(a => a.ChannelPartnerId == null);
            }

            var agents = agentsQuery
                .Select(a => new AgentModel
                {
                    AgentId = a.AgentId,
                    FullName = a.FullName ?? "",
                    Email = a.Email ?? "",
                    Phone = a.Phone ?? "",
                    Address = a.Address ?? "",
                    AgentType = a.AgentType ?? "",
                    Salary = a.Salary ?? 0,
                    CommissionRules = a.CommissionRules ?? "None (0% of sale)",
                    Documents = a.Documents ?? "",
                    Status = a.Status ?? "Pending",
                    CreatedOn = a.CreatedOn,
                    ApprovedBy = a.ApprovedBy,
                    ApprovedOn = a.ApprovedOn,
                    ChannelPartnerId = a.ChannelPartnerId
                })
                .ToList();
            return View(agents);
        }

        [HttpPost]
        public async Task<IActionResult> Update(AgentModel model)
        {
            var agent = await _context.Agents.FindAsync(model.AgentId);
            if (agent == null)
            {
                return NotFound();
            }

            // Update agent properties
            agent.FullName = model.FullName;
            agent.Email = model.Email;
            agent.Phone = model.Phone;
            agent.Address = model.Address;
            agent.AgentType = model.AgentType;
            agent.Salary = model.Salary;
            agent.CommissionRules = model.CommissionRules;

            await _context.SaveChangesAsync();

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Ok(new { success = true, message = "Agent updated successfully" });
            }

            return RedirectToAction("List");
        }

        [HttpPost]
        public async Task<IActionResult> Approve(int agentId)
        {
            var agent = _context.Agents.Find(agentId);
            if (agent == null)
            {
                TempData["Error"] = "Agent not found";
                return RedirectToAction("List");
            }

            // Check subscription limits before approving
            if (agent.ChannelPartnerId.HasValue)
            {
                var (canAdd, message) = await _subscriptionService.CanAddAgentAsync(agent.ChannelPartnerId.Value);
                if (!canAdd)
                {
                    // Set TempData for SweetAlert upgrade prompt
                    TempData["ShowUpgrade"] = true;
                    TempData["UpgradePrompt"] = message;
                    return RedirectToAction("List");
                }
            }

            agent.Status = "Approved";
            agent.ApprovedBy = 1;
            agent.ApprovedOn = IndianTime.Now;
            await _context.SaveChangesAsync();

            // Auto-create or activate user
            var username = agent.FullName ?? agent.Email ?? $"Agent_{agentId}";
            var email = agent.Email ?? "";
            var phone = agent.Phone ?? "";

            // Check if user already exists (e.g. self-registered via /Account/Register)
            var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (existingUser != null)
            {
                existingUser.IsActive = true;
                existingUser.ChannelPartnerId = agent.ChannelPartnerId;
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Agent approved and account activated for {existingUser.Username}.";

                // Create UserProfile if not exists
                var existingProfile = await _context.UserProfiles
                    .FirstOrDefaultAsync(p => p.UserId == existingUser.UserId);

                if (existingProfile == null)
                {
                    _context.UserProfiles.Add(new Models.UserProfile
                    {
                        UserId = existingUser.UserId,
                        Username = existingUser.Username,
                        Email = existingUser.Email,
                        FirstName = agent.FullName,
                        PhoneNumber = agent.Phone,
                        Location = agent.AgentType
                    });

                    await _context.SaveChangesAsync();
                }
            }
            else
            {
                var password = GenerateAgentPassword(email, phone);
                var user = new UserModel
                {
                    Username = username,
                    Email = email,
                    // Password = password, // Plain text - uncomment this and comment below to disable hashing
                    Password = Helpers.PasswordHelper.HashPassword(password), // Hashed password
                    Role = "Agent",
                    Phone = phone,
                    IsActive = true,
                    CreatedDate = IndianTime.Now,
                    ChannelPartnerId = agent.ChannelPartnerId
                };
                _context.Users.Add(user);
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Agent approved successfully. Username: {username}, Password: {password}";
                // Create UserProfile with agent details
                _context.UserProfiles.Add(new Models.UserProfile
                {
                    UserId = user.UserId,
                    Username = username,
                    Email = agent.Email,
                    FirstName = agent.FullName,
                    PhoneNumber = agent.Phone,
                    Location = agent.AgentType
                });

                await _context.SaveChangesAsync();

            }
            return RedirectToAction("List");
        }

        private string GenerateAgentPassword(string email, string phone)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(phone))
            {
                return "Agent@123"; // Default password if email/phone missing
            }
            string first4 = email.Length >= 4 ? email.Substring(0, 4).ToUpper() : email.ToUpper();
            string last4 = phone.Length >= 4 ? phone.Substring(phone.Length - 4) : phone;
            return $"{first4}@{last4}";
        }

        [HttpPost]
        public async Task<IActionResult> Reject(int agentId)
        {
            var agent = _context.Agents.Find(agentId);
            if (agent != null)
            {
                agent.Status = "Rejected";
                agent.ApprovedBy = 1;
                agent.ApprovedOn = IndianTime.Now;
                await _context.SaveChangesAsync();
                TempData["Success"] = "Agent rejected successfully.";
            }
            return RedirectToAction("List");
        }

        [HttpGet]
        [Route("agentdetails/{id}")]
        public IActionResult Details(string id)
        {
            var decodedId = IdObfuscator.Decode(id);
            if (decodedId == null)
            {
                return NotFound();
            }
            ViewBag.EncodedId = id;
            var (userId, role, channelPartnerId) = GetCurrentUserContext();

            // If id is 0, get the current user's agent record
            if (decodedId.Value == 0 && userId.HasValue)
            {
                var currentUser = _context.Users.FirstOrDefault(u => u.UserId == userId.Value);
                AgentModel currentAgent;

                // For admin agents (ChannelPartnerId = NULL), match by email only
                if (currentUser.ChannelPartnerId == null)
                {
                    currentAgent = _context.Agents
                        .Where(a => a.Email == currentUser.Email && a.ChannelPartnerId == null)
                        .OrderByDescending(a => a.CreatedOn)
                        .FirstOrDefault();
                }
                else
                {
                    // For partner agents, match by email AND ChannelPartnerId
                    currentAgent = _context.Agents
                        .Where(a => a.Email == currentUser.Email && a.ChannelPartnerId == currentUser.ChannelPartnerId)
                        .OrderByDescending(a => a.CreatedOn)
                        .FirstOrDefault();
                }

                if (currentAgent != null)
                {
                    id = IdObfuscator.Encode(currentAgent.AgentId);
                }
                else
                {
                    TempData["Error"] = "Agent profile not found. Please contact your administrator to create your agent profile.";
                    return RedirectToAction("Index", "Profile");
                }
            }

            var agent = _context.Agents.FirstOrDefault(a => a.AgentId == decodedId.Value);

            if (agent == null)
            {
                TempData["Error"] = "Agent profile not found";
                return RedirectToAction("Index", "Profile");
            }

            // Check access: Admin/Partner can view their agents, Agent can only view their own
            if (role?.ToLower() == "agent" || role?.ToLower() == "sales")
            {
                var currentUser = _context.Users.FirstOrDefault(u => u.UserId == userId);
                AgentModel currentAgent;

                if (currentUser.ChannelPartnerId == null)
                {
                    currentAgent = _context.Agents
                        .Where(a => a.Email == currentUser.Email && a.ChannelPartnerId == null)
                        .OrderByDescending(a => a.CreatedOn)
                        .FirstOrDefault();
                }
                else
                {
                    currentAgent = _context.Agents
                        .Where(a => a.Email == currentUser.Email && a.ChannelPartnerId == currentUser.ChannelPartnerId)
                        .OrderByDescending(a => a.CreatedOn)
                        .FirstOrDefault();
                }

                if (currentAgent == null || currentAgent.AgentId != decodedId.Value)
                {
                    TempData["Error"] = "Access denied";
                    return RedirectToAction("Index", "Profile");
                }
            }

            ViewBag.Documents = _context.AgentDocuments
                .Where(d => d.AgentId == decodedId.Value)
                .OrderByDescending(d => d.UploadedOn)
                .ToList();

            return View(agent);
        }

        [HttpPost]
        public async Task<IActionResult> UploadDocument(int agentId, string documentName, string documentType, IFormFile documentFile)
        {
            if (documentFile == null || documentFile.Length == 0)
            {
                return BadRequest("No file uploaded");
            }

            var agent = await _context.Agents.FindAsync(agentId);
            if (agent == null)
            {
                return NotFound("Agent not found");
            }

            // Check storage limits for partner
            if (agent.ChannelPartnerId.HasValue)
            {
                var (canUpload, message, currentUsage, limit) = await _subscriptionService.CanUploadFileAsync(agent.ChannelPartnerId.Value, documentFile.Length);
                if (!canUpload)
                {
                    return BadRequest(new
                    {
                        error = message,
                        showUpgrade = true,
                        upgradeUrl = "/Subscription/MyPlan"
                    });
                }
            }

            // Read file into byte array
            byte[] fileContent;
            using (var memoryStream = new MemoryStream())
            {
                await documentFile.CopyToAsync(memoryStream);
                fileContent = memoryStream.ToArray();
            }

            var document = new AgentDocumentModel
            {
                AgentId = agentId,
                FileName = Path.GetFileName(documentFile.FileName),
                DocumentName = documentName ?? Path.GetFileName(documentFile.FileName),
                DocumentType = documentType ?? "General",
                FileContent = fileContent,
                FileSize = documentFile.Length,
                ContentType = documentFile.ContentType,
                UploadedOn = IndianTime.Now
            };

            _context.AgentDocuments.Add(document);
            await _context.SaveChangesAsync();

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Ok(new { success = true, message = "Document uploaded successfully" });
            }

            return RedirectToAction("Details", new { id = agentId });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteDocument(int documentId)
        {
            var document = await _context.AgentDocuments.FindAsync(documentId);
            if (document == null)
            {
                return NotFound();
            }

            var agentId = document.AgentId;

            // Just remove from database - no physical file to delete
            _context.AgentDocuments.Remove(document);
            await _context.SaveChangesAsync();

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Ok(new { success = true, message = "Document deleted successfully" });
            }

            return RedirectToAction("Details", new { id = agentId });
        }
        [HttpGet]
        //[HttpGet("DownloadAllDocuments/{agentId}")]
        [Route("/agent/downloadalldocuments/{agentId}")]
        public async Task<IActionResult> DownloadAllDocuments(string agentId)
        {
            var decodedId = IdObfuscator.Decode(agentId);
            if (decodedId == null)
            {
                return NotFound();
            }

            ViewBag.EncodedId = agentId;
            var documents = await _context.AgentDocuments.Where(d => d.AgentId == decodedId.Value).ToListAsync();
            if (documents == null || !documents.Any())
            {
                return NotFound();
            }
            using (var memoryStream = new MemoryStream())
            {
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
                {
                    foreach (var doc in documents)
                    {
                        var entry = archive.CreateEntry(doc.FileName);
                        using (var entryStream = entry.Open())
                        {
                            await entryStream.WriteAsync(doc.FileContent, 0, doc.FileContent.Length);
                        }
                    }
                }
                return File(memoryStream.ToArray(), "application/zip", $"Agent_{agentId}_Documents.zip");
            }
        }

        [HttpGet]
        public async Task<IActionResult> DownloadDocument(int documentId)
        {
            var document = await _context.AgentDocuments.FindAsync(documentId);
            if (document == null)
            {
                return NotFound();
            }

            return File(document.FileContent, document.ContentType, document.FileName);
        }

        // P0-D3: Document Verification Endpoints
        [HttpPost]
        public async Task<IActionResult> ApproveDocument(int documentId)
        {
            try
            {
                var (userId, role, channelPartnerId) = GetCurrentUserContext();
                if (!userId.HasValue)
                {
                    return Json(new { success = false, message = "User not authenticated" });
                }

                var document = await _context.AgentDocuments.FindAsync(documentId);
                if (document == null)
                {
                    return Json(new { success = false, message = "Document not found" });
                }

                document.VerificationStatus = "Approved";
                document.VerifiedBy = userId.Value;
                document.VerifiedOn = IndianTime.Now;
                document.RejectionReason = null;

                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Document approved successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> RejectDocument(int documentId, string reason)
        {
            try
            {
                var (userId, role, channelPartnerId) = GetCurrentUserContext();
                if (!userId.HasValue)
                {
                    return Json(new { success = false, message = "User not authenticated" });
                }

                if (string.IsNullOrWhiteSpace(reason))
                {
                    return Json(new { success = false, message = "Rejection reason is required" });
                }

                var document = await _context.AgentDocuments.FindAsync(documentId);
                if (document == null)
                {
                    return Json(new { success = false, message = "Document not found" });
                }

                document.VerificationStatus = "Rejected";
                document.VerifiedBy = userId.Value;
                document.VerifiedOn = IndianTime.Now;
                document.RejectionReason = reason;

                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Document rejected successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAgent(int id)
        {
            var agent = await _context.Agents.FindAsync(id);
            if (agent == null)
            {
                return Json(new { success = false, message = "Agent not found" });
            }

            return Json(new
            {
                success = true,
                agent = new
                {
                    fullName = agent.FullName,
                    email = agent.Email,
                    phone = agent.Phone,
                    address = agent.Address,
                    agentType = agent.AgentType,
                    salary = agent.Salary,
                    commissionRules = agent.CommissionRules
                }
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int agentId)
        {
            try
            {
                var agent = await _context.Agents.FindAsync(agentId);
                if (agent == null)
                {
                    TempData["Error"] = "Agent not found.";
                    return RedirectToAction("List");
                }

                // Delete agent documents first
                var documents = _context.AgentDocuments.Where(d => d.AgentId == agentId);
                _context.AgentDocuments.RemoveRange(documents);

                // Delete the agent
                _context.Agents.Remove(agent);
                await _context.SaveChangesAsync();

                TempData["Success"] = "Agent deleted successfully.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "An error occurred while deleting the agent.";
            }

            return RedirectToAction("List");
        }

        [HttpPost]
        public async Task<IActionResult> UpdateAgent(int id, AgentModel model, List<IFormFile> DocumentFiles, List<string> DocumentNames, List<string> DocumentTypes)
        {
            try
            {
                var agent = await _context.Agents.FindAsync(id);
                if (agent == null)
                {
                    return Ok(new { success = false, message = "Agent not found" });
                }

                agent.FullName = model.FullName;
                agent.Email = model.Email;
                agent.Phone = model.Phone;
                agent.Address = model.Address;
                agent.AgentType = model.AgentType;
                agent.Salary = model.Salary;
                agent.CommissionRules = model.CommissionRules;

                await _context.SaveChangesAsync();

                if (DocumentFiles != null && DocumentFiles.Any())
                {
                    for (int i = 0; i < DocumentFiles.Count; i++)
                    {
                        var file = DocumentFiles[i];
                        if (file.Length > 0)
                        {
                            using (var memoryStream = new MemoryStream())
                            {
                                await file.CopyToAsync(memoryStream);
                                var fileContent = memoryStream.ToArray();

                                var documentName = DocumentNames != null && i < DocumentNames.Count ? DocumentNames[i] : Path.GetFileName(file.FileName);
                                var documentType = DocumentTypes != null && i < DocumentTypes.Count ? DocumentTypes[i] : "General";

                                var document = new AgentDocumentModel
                                {
                                    AgentId = agent.AgentId,
                                    FileName = Path.GetFileName(file.FileName),
                                    DocumentName = documentName,
                                    DocumentType = documentType,
                                    FileContent = fileContent,
                                    FileSize = file.Length,
                                    ContentType = file.ContentType,
                                    UploadedOn = IndianTime.Now
                                };
                                _context.AgentDocuments.Add(document);
                            }
                        }
                    }
                    await _context.SaveChangesAsync();
                }

                return Ok(new { success = true, message = "Agent updated successfully" });
            }
            catch (Exception ex)
            {
                return Ok(new { success = false, message = "Error: " + ex.Message });
            }
        }
    }
}
