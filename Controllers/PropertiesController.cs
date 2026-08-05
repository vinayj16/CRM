using CRM.Helpers;
using ClosedXML.Excel;
using CRM.Models;
using CRM.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CRM.Controllers
{
    [Authorize]
    public class PropertiesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<PropertiesController> _logger;

        public PropertiesController(AppDbContext db, IWebHostEnvironment env, IHttpContextAccessor httpContextAccessor, ILogger<PropertiesController> logger)
        {
            _db = db;
            _env = env;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        private int _getCurrentUserId()
        {
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(uid, out int id)) return id;
            return 0;
        }

        private string _getCurrentUserRole()
        {
            var role = User?.FindFirst(ClaimTypes.Role)?.Value;
            return role ?? "Admin";
        }

        private (int? userId, string? role) GetUserFromToken()
        {
            string token = _httpContextAccessor.HttpContext?.Request.Cookies["jwtToken"];
            if (string.IsNullOrEmpty(token))
                return (null, null);

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            var uid = jwt.Claims.FirstOrDefault(c => c.Type == "UserId")?.Value;
            var role = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;

            if (int.TryParse(uid, out int userId))
                return (userId, role);

            return (null, role);
        }

        // GET: Properties/Index
        [PermissionAuthorize("View")]
        public IActionResult Index(string search = "", int page = 1, int pageSize = 10)
        {
            var q = _db.Properties.Where(p => p.IsActive == true).AsQueryable();
            var (UserId, Role) = GetUserFromToken();
            
            // Get current user to check if they're a partner team member
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == UserId);
            var isPartnerTeam = Role?.ToLower() == "partner" || (currentUser?.ChannelPartnerId != null);

            // Do not restrict property visibility by assignment.
            // Assignment is for ownership/tracking, but all agents/sales can view all properties.

            // Search filter
            if (!string.IsNullOrWhiteSpace(search))
            {
                q = q.Where(p => p.PropertyName.Contains(search) || 
                                 p.Location.Contains(search));
            }

            var properties = q.OrderByDescending(p => p.CreatedOn).ToList();

            // Get builders for dropdown
            ViewBag.Builders = _db.Builders.Where(b => b.IsActive == true).OrderBy(b => b.BuilderName).ToList();

            // Always provide assignee lookup so cards can display "Assigned To" names.
            // Partner users get scoped users from their own channel partner.
            var salesUsersQuery = _db.Users.Where(u => u.Role == "Sales" || u.Role == "Agent");
            if (isPartnerTeam && currentUser?.ChannelPartnerId != null)
            {
                var channelPartnerId = currentUser.ChannelPartnerId;
                salesUsersQuery = salesUsersQuery.Where(u => u.ChannelPartnerId == channelPartnerId);
            }

            var salesUsers = salesUsersQuery.ToList();
            var salesUserIds = salesUsers.Select(u => u.UserId).ToList();
            var userProfiles = _db.UserProfiles.Where(up => salesUserIds.Contains(up.UserId)).ToList();

            var executives = salesUsers.Select(u =>
            {
                var profile = userProfiles.FirstOrDefault(p => p.UserId == u.UserId);
                var fullName = profile != null
                    ? $"{profile.FirstName} {profile.LastName}".Trim()
                    : string.Empty;

                return new
                {
                    u.UserId,
                    u.Username,
                    FullName = string.IsNullOrWhiteSpace(fullName) ? u.Username : fullName
                };
            }).ToList();

            ViewBag.Executives = executives;

            var userChannelPartnerId = currentUser?.ChannelPartnerId;
            var canCreate = string.Equals(Role, "Admin", StringComparison.OrdinalIgnoreCase);
            var canDelete = string.Equals(Role, "Admin", StringComparison.OrdinalIgnoreCase);

            if (!canCreate && !string.IsNullOrWhiteSpace(Role))
            {
                var createPageIds = _db.Pages
                    .Where(p => p.Controller == "Properties" && p.Action == "Index" && p.IsActive)
                    .Select(p => p.PageId)
                    .ToList();
                var createPermId = _db.Permissions
                    .Where(p => p.PermissionName == "Create" && p.IsActive)
                    .Select(p => p.PermissionId)
                    .FirstOrDefault();
                if (createPageIds.Any() && createPermId != 0)
                {
                    canCreate = _db.RolePagePermissions
                        .Any(rpp => rpp.RoleName == Role &&
                                   createPageIds.Contains(rpp.PageId) &&
                                   rpp.PermissionId == createPermId &&
                                   rpp.IsGranted &&
                                   (rpp.ChannelPartnerId == userChannelPartnerId || rpp.ChannelPartnerId == null));
                }
            }

            if (!canDelete && !string.IsNullOrWhiteSpace(Role))
            {
                var deletePageIds = _db.Pages
                    .Where(p => p.Controller == "Properties" && p.Action == "Index" && p.IsActive)
                    .Select(p => p.PageId)
                    .ToList();
                var deletePermId = _db.Permissions
                    .Where(p => p.PermissionName == "Delete" && p.IsActive)
                    .Select(p => p.PermissionId)
                    .FirstOrDefault();
                if (deletePageIds.Any() && deletePermId != 0)
                {
                    canDelete = _db.RolePagePermissions
                        .Any(rpp => rpp.RoleName == Role &&
                                   deletePageIds.Contains(rpp.PageId) &&
                                   rpp.PermissionId == deletePermId &&
                                   rpp.IsGranted &&
                                   (rpp.ChannelPartnerId == userChannelPartnerId || rpp.ChannelPartnerId == null));
                }
            }

            // Business rule: partner-side users should not be able to create/delete properties.
            if (isPartnerTeam && (string.Equals(Role, "Agent", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(Role, "Partner", StringComparison.OrdinalIgnoreCase)))
            {
                canCreate = false;
                canDelete = false;
            }
            
            ViewBag.UserRole = Role;
            ViewBag.IsPartnerTeam = isPartnerTeam;
            ViewBag.CanCreate = canCreate;
            ViewBag.CanDelete = canDelete;

            return View(properties);
        }

        // POST: Properties/SaveProperty
        [HttpPost]
        [PermissionAuthorize("Create")]
        public async Task<IActionResult> SaveProperty()
        {
            try
            {
                var (UserId, Role) = GetUserFromToken();
                var propertyId = Request.Form["propertyId"].ToString();
                var propertyName = Request.Form["propertyName"].ToString();
                var builderName = Request.Form["builderName"].ToString();
                var location = Request.Form["location"].ToString();
                var areaSqft = Request.Form["areaSqft"].ToString();
                var price = Request.Form["price"].ToString();
                var purchaseType = Request.Form["purchaseType"].ToString();
                var flatNumber = Request.Form["flatNumber"].ToString();
                var floorNumber = Request.Form["floorNumber"].ToString();
                var unit = Request.Form["unit"].ToString();
                var propertyGroup = Request.Form["propertyGroup"].ToString();
                var inventory = Request.Form["inventory"].ToString();
                var assignedTo = Request.Form["assignedTo"].ToString();
                var propertyImage = Request.Form.Files["propertyImage"];

                if (string.IsNullOrWhiteSpace(propertyName) || string.IsNullOrWhiteSpace(builderName))
                {
                    return Json(new { success = false, message = "Property Name and Builder are required" });
                }

                byte[]? imageBytes = null;
                if (propertyImage != null && propertyImage.Length > 0)
                {
                    using (var ms = new MemoryStream())
                    {
                        await propertyImage.CopyToAsync(ms);
                        imageBytes = ms.ToArray();
                    }
                }

                if (propertyId == "0" || string.IsNullOrWhiteSpace(propertyId))
                {
                    // Find or create builder
                    var builder = await _db.Builders.FirstOrDefaultAsync(b => b.BuilderName == builderName);
                    if (builder == null)
                    {
                        builder = new BuilderModel
                        {
                            BuilderName = builderName,
                            IsActive = true,
                            CreatedOn = IndianTime.Now
                        };
                        _db.Builders.Add(builder);
                        await _db.SaveChangesAsync();
                    }

                    // Create new property
                    var property = new PropertyModel
                    {
                        PropertyName = propertyName,
                        BuilderId = builder.BuilderId,
                        Location = location,
                        AreaSqft = !string.IsNullOrWhiteSpace(areaSqft) ? decimal.Parse(areaSqft) : null,
                        Price = !string.IsNullOrWhiteSpace(price) ? decimal.Parse(price) : null,
                        PurchaseType = purchaseType,
                        FlatNumber = flatNumber,
                        FloorNumber = floorNumber,
                        Unit = unit,
                        PropertyGroup = propertyGroup,
                        Inventory = inventory,
                        AssignedTo = !string.IsNullOrWhiteSpace(assignedTo) ? int.Parse(assignedTo) : null,
                        PropertyImage = imageBytes,
                        PostedBy = UserId,
                        CreatedOn = IndianTime.Now,
                        CreatedBy = UserId,
                        IsActive = true
                    };

                    _db.Properties.Add(property);
                    await _db.SaveChangesAsync();

                    // Also save image to PropertyUploads so it appears on landing page & details
                    if (imageBytes != null && propertyImage != null)
                    {
                        _db.PropertyUploads.Add(new PropertyUploadModel
                        {
                            PropertyId = property.PropertyId,
                            FileName = propertyImage.FileName,
                            FileBytes = imageBytes,
                            ContentType = propertyImage.ContentType,
                            FileType = "Image",
                            UploadedOn = IndianTime.Now,
                            UploadedBy = UserId
                        });
                        await _db.SaveChangesAsync();
                    }

                    // Add to history
                    _db.PropertyHistory.Add(new PropertyHistoryModel
                    {
                        PropertyId = property.PropertyId,
                        Activity = "Property created",
                        ActivityDate = IndianTime.Now,
                        ExecutiveId = UserId
                    });
                    await _db.SaveChangesAsync();

                    return Json(new { success = true, message = "Property added successfully!" });
                }
                else
                {
                    // Update existing property
                    var pid = int.Parse(propertyId);
                    var property = await _db.Properties.FirstOrDefaultAsync(p => p.PropertyId == pid);
                    if (property == null)
                    {
                        return Json(new { success = false, message = "Property not found" });
                    }

                    // Find or create builder
                    var builder = await _db.Builders.FirstOrDefaultAsync(b => b.BuilderName == builderName);
                    if (builder == null)
                    {
                        builder = new BuilderModel
                        {
                            BuilderName = builderName,
                            IsActive = true,
                            CreatedOn = IndianTime.Now
                        };
                        _db.Builders.Add(builder);
                        await _db.SaveChangesAsync();
                    }

                    property.PropertyName = propertyName;
                    property.BuilderId = builder.BuilderId;
                    property.Location = location;
                    property.AreaSqft = !string.IsNullOrWhiteSpace(areaSqft) ? decimal.Parse(areaSqft) : null;
                    property.Price = !string.IsNullOrWhiteSpace(price) ? decimal.Parse(price) : null;
                    property.PurchaseType = purchaseType;
                    property.FlatNumber = flatNumber;
                    property.FloorNumber = floorNumber;
                    property.Unit = unit;
                    property.PropertyGroup = propertyGroup;
                    property.Inventory = inventory;
                    property.AssignedTo = !string.IsNullOrWhiteSpace(assignedTo) ? int.Parse(assignedTo) : null;
                    property.UpdatedOn = IndianTime.Now;
                    property.UpdatedBy = UserId;

                    if (imageBytes != null)
                    {
                        property.PropertyImage = imageBytes;

                        // Also save to PropertyUploads so it appears on landing page & details
                        if (propertyImage != null)
                        {
                            _db.PropertyUploads.Add(new PropertyUploadModel
                            {
                                PropertyId = property.PropertyId,
                                FileName = propertyImage.FileName,
                                FileBytes = imageBytes,
                                ContentType = propertyImage.ContentType,
                                FileType = "Image",
                                UploadedOn = IndianTime.Now,
                                UploadedBy = UserId
                            });
                        }
                    }

                    await _db.SaveChangesAsync();

                    // Add to history
                    _db.PropertyHistory.Add(new PropertyHistoryModel
                    {
                        PropertyId = property.PropertyId,
                        Activity = "Property updated",
                        ActivityDate = IndianTime.Now,
                        ExecutiveId = UserId
                    });
                    await _db.SaveChangesAsync();

                    return Json(new { success = true, message = "Property updated successfully!" });
                }
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException?.Message ?? ex.Message;
                return Json(new { success = false, message = "Error: " + innerMsg });
            }
        }

        // POST: Properties/Delete
        [HttpPost]
        [PermissionAuthorize("Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var (UserId, Role) = GetUserFromToken();
                var property = await _db.Properties.FirstOrDefaultAsync(p => p.PropertyId == id);
                
                if (property == null)
                {
                    return Json(new { success = false, message = "Property not found" });
                }

                // Soft delete
                property.IsActive = false;
                property.UpdatedOn = IndianTime.Now;
                property.UpdatedBy = UserId;
                await _db.SaveChangesAsync();

                // Add to history
                _db.PropertyHistory.Add(new PropertyHistoryModel
                {
                    PropertyId = property.PropertyId,
                    Activity = "Property deleted",
                    ActivityDate = IndianTime.Now,
                    ExecutiveId = UserId
                });
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Property deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // GET: Properties/GetProperty
        [HttpGet]
        public async Task<IActionResult> GetProperty(int id)
        {
            try
            {var property = await _db.Properties.FirstOrDefaultAsync(p => p.PropertyId == id);
                
                if (property == null)
                {
                    return Json(new { success = false, message = "Property not found" });
                }

                var builder = property.BuilderId > 0 ? await _db.Builders.FirstOrDefaultAsync(b => b.BuilderId == property.BuilderId) : null;

                return Json(new
                {
                    success = true,
                    propertyId = property.PropertyId,
                    propertyName = property.PropertyName,
                    builderName = builder?.BuilderName ?? "",
                    location = property.Location,
                    areaSqft = property.AreaSqft,
                    price = property.Price,
                    purchaseType = property.PurchaseType,
                    flatNumber = property.FlatNumber,
                    floorNumber = property.FloorNumber,
                    unit = property.Unit,
                    propertyGroup = property.PropertyGroup,
                    inventory = property.Inventory,
                    assignedTo = property.AssignedTo,
                    propertyImage = property.PropertyImage
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // GET: Properties/Details
        [PermissionAuthorize("View")]
        [Route("propertydetails/{id}")]
        public async Task<IActionResult> Details(string id)
        {
            var decodedId = IdObfuscator.Decode(id);
            if (decodedId == null)
            {
                return NotFound();
            }
            ViewBag.EncodedId = id;

            var property = await _db.Properties
                .FirstOrDefaultAsync(p => p.PropertyId == decodedId.Value);

            if (property == null)
            {
                return NotFound();
            }

            // Get builder info
            var builder = property.BuilderId > 0 ? await _db.Builders.FirstOrDefaultAsync(b => b.BuilderId == property.BuilderId) : null;
            ViewBag.Builder = builder;

            // Get assigned user info
            if (property.AssignedTo.HasValue && property.AssignedTo.Value > 0)
            {
                var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == property.AssignedTo.Value);
                var profile = await _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == property.AssignedTo.Value);
                ViewBag.AssignedUser = new
                {
                    UserId = user?.UserId,
                    Username = user?.Username,
                    FullName = profile != null ? $"{profile.FirstName} {profile.LastName}".Trim() : user?.Username
                };
            }

            // Get all agents assigned to this property
            var assignedAgents = await _db.PropertyAgents
                .Where(pa => pa.PropertyId == decodedId.Value && pa.IsActive == true)
                .Select(pa => pa.AgentUserId)
                .ToListAsync();

            ViewBag.AssignedAgents = assignedAgents;

            // Get document types for dropdown
            ViewBag.DocumentTypes = new List<string>
            {
                "Approval Letter",
                "NOC",
                "Plan Copy",
                "Building Plan",
                "Occupancy Certificate",
                "Title Deed",
                "Tax Receipts",
                "Encumbrance Certificate"
            };

            return View(property);
        }

        // GET: Properties/GetImages
        [HttpGet]
        public async Task<IActionResult> GetImages(int propertyId)
        {
            try
            {
                var uploads = await _db.PropertyUploads
                    .Where(u => u.PropertyId == propertyId)
                    .OrderByDescending(u => u.UploadedOn)
                    .Select(u => new
                    {
                        uploadId = u.UploadId,
                        fileName = u.FileName,
                        contentType = u.ContentType,
                        fileType = u.FileType,
                        uploadedOn = u.UploadedOn,
                        uploadedBy = _db.Users
                            .Where(user => user.UserId == u.UploadedBy)
                            .Select(user => user.Username)
                            .FirstOrDefault()
                    })
                    .ToListAsync();

                return Json(new { success = true, uploads });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // POST: Properties/UploadImage
        [HttpPost]
        public async Task<IActionResult> UploadImage(int propertyId, IFormFile file, string fileType = "Image")
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return Json(new { success = false, message = "No file uploaded" });
                }

                var (UserId, Role) = GetUserFromToken();

                byte[] fileBytes;
                using (var ms = new MemoryStream())
                {
                    await file.CopyToAsync(ms);
                    fileBytes = ms.ToArray();
                }

                var upload = new PropertyUploadModel
                {
                    PropertyId = propertyId,
                    FileName = file.FileName,
                    FileBytes = fileBytes,
                    ContentType = file.ContentType,
                    FileType = fileType,
                    UploadedOn = IndianTime.Now,
                    UploadedBy = UserId
                };

                _db.PropertyUploads.Add(upload);
                await _db.SaveChangesAsync();

                // Add to history
                _db.PropertyHistory.Add(new PropertyHistoryModel
                {
                    PropertyId = propertyId,
                    Activity = $"Image uploaded: {file.FileName}",
                    ActivityDate = IndianTime.Now,
                    ExecutiveId = UserId
                });
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Image uploaded successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // POST: Properties/DeleteImage
        [HttpPost]
        public async Task<IActionResult> DeleteImage(int uploadId)
        {
            try
            {
                var (UserId, Role) = GetUserFromToken();
                var upload = await _db.PropertyUploads.FindAsync(uploadId);

                if (upload == null)
                {
                    return Json(new { success = false, message = "Image not found" });
                }

                var fileName = upload.FileName;
                var propertyId = upload.PropertyId;

                _db.PropertyUploads.Remove(upload);
                await _db.SaveChangesAsync();

                // Add to history
                _db.PropertyHistory.Add(new PropertyHistoryModel
                {
                    PropertyId = propertyId,
                    Activity = $"Image deleted: {fileName}",
                    ActivityDate = IndianTime.Now,
                    ExecutiveId = UserId
                });
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Image deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // GET: Properties/DownloadImage
        [HttpGet]
        public async Task<IActionResult> DownloadImage(int uploadId)
        {
            var upload = await _db.PropertyUploads.FindAsync(uploadId);
            if (upload == null || upload.FileBytes == null)
            {
                return NotFound();
            }

            return File(upload.FileBytes, upload.ContentType ?? "application/octet-stream", upload.FileName ?? "download");
        }

        // GET: Properties/GetDocuments
        [HttpGet]
        public async Task<IActionResult> GetDocuments(int propertyId)
        {
            try
            {
                var documents = await _db.PropertyDocuments
                    .Where(d => d.PropertyId == propertyId)
                    .OrderByDescending(d => d.UploadedOn)
                    .Select(d => new
                    {
                        documentId = d.DocumentId,
                        documentType = d.DocumentType,
                        fileName = d.FileName,
                        contentType = d.ContentType,
                        uploadedOn = d.UploadedOn,
                        uploadedBy = _db.Users
                            .Where(user => user.UserId == d.UploadedBy)
                            .Select(user => user.Username)
                            .FirstOrDefault()
                    })
                    .ToListAsync();

                return Json(new { success = true, documents });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // POST: Properties/UploadDocument
        [HttpPost]
        public async Task<IActionResult> UploadDocument(int propertyId, string documentType, IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return Json(new { success = false, message = "No file uploaded" });
                }

                if (string.IsNullOrWhiteSpace(documentType))
                {
                    return Json(new { success = false, message = "Document type is required" });
                }

                var (UserId, Role) = GetUserFromToken();

                byte[] fileBytes;
                using (var ms = new MemoryStream())
                {
                    await file.CopyToAsync(ms);
                    fileBytes = ms.ToArray();
                }

                var document = new PropertyDocumentModel
                {
                    PropertyId = propertyId,
                    DocumentType = documentType,
                    FileName = file.FileName,
                    FileBytes = fileBytes,
                    ContentType = file.ContentType,
                    UploadedOn = IndianTime.Now,
                    UploadedBy = UserId
                };

                _db.PropertyDocuments.Add(document);
                await _db.SaveChangesAsync();

                // Add to history
                _db.PropertyHistory.Add(new PropertyHistoryModel
                {
                    PropertyId = propertyId,
                    Activity = $"Document uploaded: {documentType} - {file.FileName}",
                    ActivityDate = IndianTime.Now,
                    ExecutiveId = UserId
                });
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Document uploaded successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // POST: Properties/DeleteDocument
        [HttpPost]
        public async Task<IActionResult> DeleteDocument(int documentId)
        {
            try
            {
                var (UserId, Role) = GetUserFromToken();
                var document = await _db.PropertyDocuments.FindAsync(documentId);

                if (document == null)
                {
                    return Json(new { success = false, message = "Document not found" });
                }

                var fileName = document.FileName;
                var propertyId = document.PropertyId;

                _db.PropertyDocuments.Remove(document);
                await _db.SaveChangesAsync();

                // Add to history
                _db.PropertyHistory.Add(new PropertyHistoryModel
                {
                    PropertyId = propertyId,
                    Activity = $"Document deleted: {fileName}",
                    ActivityDate = IndianTime.Now,
                    ExecutiveId = UserId
                });
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Document deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // GET: Properties/DownloadDocument
        [HttpGet]
        public async Task<IActionResult> DownloadDocument(int documentId)
        {
            var document = await _db.PropertyDocuments.FindAsync(documentId);
            if (document == null || document.FileBytes == null)
            {
                return NotFound();
            }

            return File(document.FileBytes, document.ContentType ?? "application/octet-stream", document.FileName ?? "download");
        }

        // GET: Properties/GetFlats
        [HttpGet]
        public async Task<IActionResult> GetFlats(int propertyId, string searchBhk = "")
        {
            try
            {
                var query = _db.PropertyFlats
                    .Where(f => f.PropertyId == propertyId);

                if (!string.IsNullOrWhiteSpace(searchBhk))
                {
                    query = query.Where(f => f.BHK == searchBhk);
                }

                var flats = await query
                    .OrderBy(f => f.BlockName)
                    .ThenBy(f => f.FloorName)
                    .ThenBy(f => f.FlatName)
                    .Select(f => new
                    {
                        flatId = f.FlatId,
                        propertyId = f.PropertyId,
                        blockName = f.BlockName,
                        floorName = f.FloorName,
                        flatName = f.FlatName,
                        bhk = f.BHK,
                        propertyType = f.PropertyType,
                        propertyGroup = f.PropertyGroup,
                        areaSqft = f.AreaSqft,
                        location = f.Location,
                        bedroomCount = f.BedroomCount,
                        bathroomCount = f.BathroomCount,
                        parkingAvailable = f.ParkingAvailable,
                        flatStatus = f.FlatStatus,
                        price = f.Price
                    })
                    .ToListAsync();

                return Json(new { success = true, flats });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // POST: Properties/SaveFlat
        [HttpPost]
        public async Task<IActionResult> SaveFlat()
        {
            try
            {
                var (UserId, Role) = GetUserFromToken();
                var flatId = Request.Form["flatId"].ToString();
                var propertyId = Request.Form["propertyId"].ToString();
                var blockName = Request.Form["blockName"].ToString();
                var floorName = Request.Form["floorName"].ToString();
                var flatName = Request.Form["flatName"].ToString();
                var bhk = Request.Form["bhk"].ToString();
                var propertyType = Request.Form["propertyType"].ToString();
                var propertyGroup = Request.Form["propertyGroup"].ToString();
                var areaSqft = Request.Form["areaSqft"].ToString();
                var location = Request.Form["location"].ToString();
                var bedroomCount = Request.Form["bedroomCount"].ToString();
                var bathroomCount = Request.Form["bathroomCount"].ToString();
                var parkingAvailable = Request.Form["parkingAvailable"].ToString() == "true";
                var flatStatus = Request.Form["flatStatus"].ToString();
                var price = Request.Form["price"].ToString();
                var floorNumber = Request.Form["floorName"].ToString();
                var status = Request.Form["flatStatus"].ToString();
                var Area = !string.IsNullOrWhiteSpace(areaSqft) ? decimal.Parse(areaSqft).ToString("F2") + " sqft" : null;


                if (string.IsNullOrWhiteSpace(flatName))
                {
                    return Json(new { success = false, message = "Flat Name is required" });
                }

                if (flatId == "0" || string.IsNullOrWhiteSpace(flatId))
                {
                    // Create new flat
                    var flat = new PropertyFlatModel
                    {
                        PropertyId = int.Parse(propertyId),
                        BlockName = blockName,
                        FloorName = floorName,
                        FlatName = flatName,
                        BHK = bhk,
                        PropertyType = propertyType,
                        PropertyGroup = propertyGroup,
                        AreaSqft = !string.IsNullOrWhiteSpace(areaSqft) ? decimal.Parse(areaSqft) : null,
                        Location = location,
                        BedroomCount = !string.IsNullOrWhiteSpace(bedroomCount) ? int.Parse(bedroomCount) : null,
                        BathroomCount = !string.IsNullOrWhiteSpace(bathroomCount) ? int.Parse(bathroomCount) : null,
                        ParkingAvailable = parkingAvailable,
                        FlatStatus = flatStatus,
                        Price = !string.IsNullOrWhiteSpace(price) ? decimal.Parse(price) : null,
                        CreatedOn = IndianTime.Now,
                        CreatedBy = UserId,
                        IsActive = true,
                        FloorNumber = floorName,
                        Status=flatStatus,
                        Area = !string.IsNullOrWhiteSpace(areaSqft) ? decimal.Parse(areaSqft).ToString("F2") + " sqft" : null,
                    };

                    _db.PropertyFlats.Add(flat);
                    await _db.SaveChangesAsync();

                    // Add to history
                    _db.PropertyHistory.Add(new PropertyHistoryModel
                    {
                        PropertyId = int.Parse(propertyId),
                        Activity = $"Flat added: {blockName} - {floorName} - {flatName}",
                        ActivityDate = IndianTime.Now,
                        ExecutiveId = UserId
                    });
                    await _db.SaveChangesAsync();

                    return Json(new { success = true, message = "Flat added successfully!" });
                }
                else
                {
                    // Update existing flat
                    var fid = int.Parse(flatId);
                    var flat = await _db.PropertyFlats.FirstOrDefaultAsync(f => f.FlatId == fid);
                    if (flat == null)
                    {
                        return Json(new { success = false, message = "Flat not found" });
                    }

                    flat.BlockName = blockName;
                    flat.FloorName = floorName;
                    flat.FlatName = flatName;
                    flat.BHK = bhk;
                    flat.PropertyType = propertyType;
                    flat.PropertyGroup = propertyGroup;
                    flat.AreaSqft = !string.IsNullOrWhiteSpace(areaSqft) ? decimal.Parse(areaSqft) : null;
                    flat.Location = location;
                    flat.BedroomCount = !string.IsNullOrWhiteSpace(bedroomCount) ? int.Parse(bedroomCount) : null;
                    flat.BathroomCount = !string.IsNullOrWhiteSpace(bathroomCount) ? int.Parse(bathroomCount) : null;
                    flat.ParkingAvailable = parkingAvailable;
                    flat.FlatStatus = flatStatus;
                    flat.Price = !string.IsNullOrWhiteSpace(price) ? decimal.Parse(price) : null;
                    flat.UpdatedOn = IndianTime.Now;
                    flat.UpdatedBy = UserId;
                    flat.FloorNumber = floorName;
                    flat.Status= flatStatus;
                    flat.Area = !string.IsNullOrWhiteSpace(areaSqft) ? decimal.Parse(areaSqft).ToString("F2") + " sqft" : null;
                    await _db.SaveChangesAsync();

                    // Add to history
                    _db.PropertyHistory.Add(new PropertyHistoryModel
                    {
                        PropertyId = flat.PropertyId,
                        Activity = $"Flat updated: {blockName} - {floorName} - {flatName}",
                        ActivityDate = IndianTime.Now,
                        ExecutiveId = UserId
                    });
                    await _db.SaveChangesAsync();

                    return Json(new { success = true, message = "Flat updated successfully!" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // POST: Properties/DeleteFlat
        [HttpPost]
        public async Task<IActionResult> DeleteFlat(int flatId)
        {
            try
            {
                var (UserId, Role) = GetUserFromToken();
                var flat = await _db.PropertyFlats.FirstOrDefaultAsync(f => f.FlatId == flatId);

                if (flat == null)
                {
                    return Json(new { success = false, message = "Flat not found" });
                }

                // Soft delete
                flat.IsActive = false;
                flat.UpdatedOn = IndianTime.Now;
                flat.UpdatedBy = UserId;
                await _db.SaveChangesAsync();

                // Add to history
                _db.PropertyHistory.Add(new PropertyHistoryModel
                {
                    PropertyId = flat.PropertyId,
                    Activity = $"Flat deleted: {flat.BlockName} - {flat.FloorName} - {flat.FlatName}",
                    ActivityDate = IndianTime.Now,
                    ExecutiveId = UserId
                });
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Flat deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // GET: Properties/GetFlat
        [HttpGet]
        public async Task<IActionResult> GetFlat(int flatId)
        {
            try
            {
                var flat = await _db.PropertyFlats.FindAsync(flatId);
                if (flat == null)
                {
                    return Json(new { success = false, message = "Flat not found" });
                }

                return Json(new
                {
                    success = true,
                    flat = new
                    {
                        flatId = flat.FlatId,
                        propertyId = flat.PropertyId,
                        blockName = flat.BlockName,
                        floorName = flat.FloorName,
                        flatName = flat.FlatName,
                        bhk = flat.BHK,
                        propertyType = flat.PropertyType,
                        propertyGroup = flat.PropertyGroup,
                        areaSqft = flat.AreaSqft,
                        location = flat.Location,
                        bedroomCount = flat.BedroomCount,
                        bathroomCount = flat.BathroomCount,
                        parkingAvailable = flat.ParkingAvailable,
                        flatStatus = flat.FlatStatus,
                        price = flat.Price
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // GET: Properties/DownloadSample
        [HttpGet]
        public IActionResult DownloadSample()
        {
            using (var wb = new XLWorkbook())
            {
                // Sheet: Property Flats
                var ws = wb.Worksheets.Add("Property Flats");
                
                // Header row with styling
                ws.Cell(1, 1).Value = "Block Name";
                ws.Cell(1, 2).Value = "Floor Name";
                ws.Cell(1, 3).Value = "Flat Name";
                ws.Cell(1, 4).Value = "BHK";
                ws.Cell(1, 5).Value = "Property Type";
                ws.Cell(1, 6).Value = "Group";
                ws.Cell(1, 7).Value = "Area Sqft";
                ws.Cell(1, 8).Value = "Location";
                ws.Cell(1, 9).Value = "Bedroom Count";
                ws.Cell(1, 10).Value = "Bathroom Count";
                ws.Cell(1, 11).Value = "Parking Available (Yes/No)";
                ws.Cell(1, 12).Value = "Flat Status";
                ws.Cell(1, 13).Value = "Price";

                // Style header
                var headerRange = ws.Range(1, 1, 1, 13);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;
                headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                // Sample data row 1
                ws.Cell(2, 1).Value = "Tower A";
                ws.Cell(2, 2).Value = "1";
                ws.Cell(2, 3).Value = "101";
                ws.Cell(2, 4).Value = "2BHK";
                ws.Cell(2, 5).Value = "Flat";
                ws.Cell(2, 6).Value = "Residential";
                ws.Cell(2, 7).Value = "1360";
                ws.Cell(2, 8).Value = "Bangalore";
                ws.Cell(2, 9).Value = "2";
                ws.Cell(2, 10).Value = "3";
                ws.Cell(2, 11).Value = "Yes";
                ws.Cell(2, 12).Value = "Available";
                ws.Cell(2, 13).Value = "4500000";

                // Sample data row 2
                ws.Cell(3, 1).Value = "Tower A";
                ws.Cell(3, 2).Value = "2";
                ws.Cell(3, 3).Value = "201";
                ws.Cell(3, 4).Value = "3BHK";
                ws.Cell(3, 5).Value = "Apartment";
                ws.Cell(3, 6).Value = "Residential";
                ws.Cell(3, 7).Value = "1850";
                ws.Cell(3, 8).Value = "Bangalore";
                ws.Cell(3, 9).Value = "3";
                ws.Cell(3, 10).Value = "2";
                ws.Cell(3, 11).Value = "No";
                ws.Cell(3, 12).Value = "Booked";
                ws.Cell(3, 13).Value = "6500000";

                // Auto-fit columns
                ws.Columns().AdjustToContents();

                using (var ms = new MemoryStream())
                {
                    wb.SaveAs(ms);
                    return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Property_Flats_Upload_Sample.xlsx");
                }
            }
        }

        // POST: Properties/BulkUpload
        [HttpPost]
        [PermissionAuthorize("Create")]
        public async Task<IActionResult> BulkUpload(IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return Json(new { success = false, message = "No file uploaded" });
                }

                var (UserId, Role) = GetUserFromToken();

                using (var stream = file.OpenReadStream())
                using (var wb = new XLWorkbook(stream))
                {
                    // Process Properties sheet
                    if (wb.Worksheets.Contains("Properties"))
                    {
                        var ws = wb.Worksheet("Properties");
                        var rows = ws.RowsUsed().Skip(1); // Skip header

                        foreach (var row in rows)
                        {
                            var propertyName = row.Cell(1).GetString();
                            var builderName = row.Cell(2).GetString();

                            if (string.IsNullOrWhiteSpace(propertyName) || string.IsNullOrWhiteSpace(builderName))
                                continue;

                            // Find builder
                            var builder = await _db.Builders.FirstOrDefaultAsync(b => b.BuilderName == builderName);
                            if (builder == null) continue;

                            var property = new PropertyModel
                            {
                                PropertyName = propertyName,
                                BuilderId = builder.BuilderId,
                                Location = row.Cell(3).GetString(),
                                AreaSqft = decimal.TryParse(row.Cell(4).GetString(), out var areaSqft) ? areaSqft : null,
                                Price = decimal.TryParse(row.Cell(5).GetString(), out var price) ? price : null,
                                PurchaseType = row.Cell(6).GetString(),
                                FlatNumber = row.Cell(7).GetString(),
                                FloorNumber = row.Cell(8).GetString(),
                                Unit = row.Cell(9).GetString(),
                                PropertyGroup = row.Cell(10).GetString(),
                                Inventory = row.Cell(11).GetString(),
                                AssignedTo = int.TryParse(row.Cell(12).GetString(), out var assignedTo) ? assignedTo : null,
                                PostedBy = UserId,
                                CreatedOn = IndianTime.Now,
                                CreatedBy = UserId,
                                IsActive = true
                            };

                            _db.Properties.Add(property);
                        }

                        await _db.SaveChangesAsync();
                    }

                    // Process Property Flats sheet
                    if (wb.Worksheets.Contains("Property Flats"))
                    {
                        var ws = wb.Worksheet("Property Flats");
                        var rows = ws.RowsUsed().Skip(1); // Skip header

                        foreach (var row in rows)
                        {
                            var propertyName = row.Cell(1).GetString();
                            if (string.IsNullOrWhiteSpace(propertyName)) continue;

                            // Find property
                            var property = await _db.Properties.FirstOrDefaultAsync(p => p.PropertyName == propertyName);
                            if (property == null) continue;

                            var flat = new PropertyFlatModel
                            {
                                PropertyId = property.PropertyId,
                                BlockName = row.Cell(2).GetString(),
                                FloorName = row.Cell(3).GetString(),
                                FlatName = row.Cell(4).GetString(),
                                BHK = row.Cell(5).GetString(),
                                PropertyType = row.Cell(6).GetString(),
                                PropertyGroup = row.Cell(7).GetString(),
                                AreaSqft = decimal.TryParse(row.Cell(8).GetString(), out var areaSqft) ? areaSqft : null,
                                Location = row.Cell(9).GetString(),
                                BedroomCount = int.TryParse(row.Cell(10).GetString(), out var bedroomCount) ? bedroomCount : null,
                                BathroomCount = int.TryParse(row.Cell(11).GetString(), out var bathroomCount) ? bathroomCount : null,
                                ParkingAvailable = row.Cell(12).GetString().ToLower() == "yes",
                                FlatStatus = row.Cell(13).GetString(),
                                Price = decimal.TryParse(row.Cell(14).GetString(), out var price) ? price : null,
                                CreatedOn = IndianTime.Now,
                                CreatedBy = UserId,
                                IsActive = true
                            };

                            _db.PropertyFlats.Add(flat);
                        }

                        await _db.SaveChangesAsync();
                    }
                }

                return Json(new { success = true, message = "Bulk upload completed successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // POST: Properties/BulkUploadFlats
        [HttpPost]
        [PermissionAuthorize("Create")]
        public async Task<IActionResult> BulkUploadFlats(IFormFile file, int propertyId)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return Json(new { success = false, message = "No file uploaded" });
                }

                if (propertyId <= 0)
                {
                    return Json(new { success = false, message = "Invalid property ID" });
                }

                var (UserId, Role) = GetUserFromToken();

                int totalRows = 0;
                int successCount = 0;
                int errorCount = 0;
                List<string> errors = new List<string>();

                using (var stream = file.OpenReadStream())
                using (var wb = new XLWorkbook(stream))
                {
                    // Process Property Flats sheet
                    if (wb.Worksheets.Contains("Property Flats"))
                    {
                        var ws = wb.Worksheet("Property Flats");
                        var rows = ws.RowsUsed().Skip(1); // Skip header

                        foreach (var row in rows)
                        {
                            totalRows++;
                            try
                            {
                                var flatName = row.Cell(3).GetString();
                                if (string.IsNullOrWhiteSpace(flatName))
                                {
                                    errors.Add($"Row {totalRows + 1}: Flat Name is required");
                                    errorCount++;
                                    continue;
                                }

                                var flat = new PropertyFlatModel
                                {
                                    PropertyId = propertyId,
                                    BlockName = row.Cell(1).GetString(),
                                    FloorName = row.Cell(2).GetString(),
                                    FlatName = flatName,
                                    BHK = row.Cell(4).GetString(),
                                    PropertyType = row.Cell(5).GetString(),
                                    PropertyGroup = row.Cell(6).GetString(),
                                    AreaSqft = decimal.TryParse(row.Cell(7).GetString(), out var areaSqft) ? areaSqft : null,
                                    Location = row.Cell(8).GetString(),
                                    BedroomCount = int.TryParse(row.Cell(9).GetString(), out var bedroomCount) ? bedroomCount : null,
                                    BathroomCount = int.TryParse(row.Cell(10).GetString(), out var bathroomCount) ? bathroomCount : null,
                                    ParkingAvailable = row.Cell(11).GetString().Trim().ToLower() == "yes",
                                    FlatStatus = row.Cell(12).GetString(),
                                    Price = decimal.TryParse(row.Cell(13).GetString(), out var price) ? price : null,
                                    CreatedOn = IndianTime.Now,
                                    CreatedBy = UserId,
                                    IsActive = true
                                };

                                _db.PropertyFlats.Add(flat);
                                successCount++;
                            }
                            catch (Exception ex)
                            {
                                errors.Add($"Row {totalRows + 1}: {ex.Message}");
                                errorCount++;
                            }
                        }

                        await _db.SaveChangesAsync();
                    }
                    else
                    {
                        return Json(new { success = false, message = "Excel file must contain 'Property Flats' sheet" });
                    }
                }

                return Json(new
                {
                    success = true,
                    message = $"Bulk upload completed! {successCount} flats uploaded successfully.",
                    totalRows = totalRows,
                    successCount = successCount,
                    errorCount = errorCount,
                    errors = errors.Take(10).ToList() // Return only first 10 errors
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        [HttpGet]
        public IActionResult GetAllProperties()
        {
            try
            {
                var properties = _db.Properties
                    .Select(p => new {
                        PropertyId = p.PropertyId,
                        PropertyName = p.PropertyName
                    })
                    .OrderBy(p => p.PropertyName)
                    .ToList();

                return Json(new { success = true, properties });
            }
            catch
            {
                return Json(new { success = false, message = "Failed to load properties" });
            }
        }
    }
}
