using CRM.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Controllers
{
    [Authorize]
    public class TestimonialsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ILogger<TestimonialsController> _logger;

        public TestimonialsController(AppDbContext db, ILogger<TestimonialsController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // GET: /Testimonials or /Testimonials/Index
        // Renders the existing Settings/Testimonials.cshtml view
        // CRUD operations (Save/Delete/Get) are handled by SettingsController
        [HttpGet]
        public IActionResult Index()
        {
            var testimonials = _db.Testimonials
                .OrderByDescending(t => t.CreatedOn)
                .ToList();
            return View("~/Views/Settings/Testimonials.cshtml", testimonials);
        }
    }
}
