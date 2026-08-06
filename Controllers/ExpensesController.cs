using CRM.Attributes;
using CRM.Helpers;
using CRM.Models;
using CRM.ViewModels;
using Microsoft.AspNetCore.Mvc;
using System.Linq;

namespace CRM.Controllers
{
    public class ExpensesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ILogger<ExpensesController> _logger;
        public ExpensesController(AppDbContext db, ILogger<ExpensesController> logger) { _db = db; _logger = logger; }
        [PermissionAuthorize("View")]
        public IActionResult Index()
        {
            var role = User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(uid, out int userId);
            var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
            var channelPartnerId = currentUser?.ChannelPartnerId;

            var expensesQuery = _db.Expenses.AsQueryable();
            if (role?.ToLower() == "partner")
                expensesQuery = expensesQuery.Where(e => e.ChannelPartnerId == channelPartnerId);
            else if (role?.ToLower() == "admin")
                expensesQuery = expensesQuery.Where(e => e.ChannelPartnerId == null);

            var expenses = expensesQuery.ToList();
            return View(expenses);
        }

        // GET: Expenses/Details/{id}

        [Route("expensedetails/{id}")]
        public IActionResult Details(string id)
        {
            var decodedId = IdObfuscator.Decode(id);
            if (decodedId == null)
            {
                return NotFound();
            }
            ViewBag.EncodedId = id;
            var expense = _db.Expenses.FirstOrDefault(e => e.ExpenseId == decodedId.Value);
            if (expense == null)
            {
                return NotFound();
            }
            return View(expense);
        }

        // GET: Expenses/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Expenses/Create
        [HttpPost]
        public IActionResult Create(ExpenseModel model)
        {
            if (ModelState.IsValid)
            {
                var role = User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
                var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(uid, out int userId);
                var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
                
                if (role?.ToLower() == "partner")
                    model.ChannelPartnerId = currentUser?.ChannelPartnerId;
                
                _db.Expenses.Add(model);
                _db.SaveChanges();
                return RedirectToAction("Index");
            }
            return View(model);
        }

        // POST: Expenses/CreateModal (AJAX)
        [HttpPost]
        public JsonResult CreateModal([FromForm] ExpenseModel model)
        {
            if (ModelState.IsValid)
            {
                var role = User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
                var uid = User?.FindFirst("UserId")?.Value ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(uid, out int userId);
                var currentUser = _db.Users.FirstOrDefault(u => u.UserId == userId);
                
                if (role?.ToLower() == "partner")
                    model.ChannelPartnerId = currentUser?.ChannelPartnerId;
                
                _db.Expenses.Add(model);
                _db.SaveChanges();
                return Json(new { success = true });
            }
            return Json(new { success = false, message = "Invalid data" });
        }

        // GET: Expenses/GetExpense/{id} (AJAX)
        [HttpGet]
        public JsonResult GetExpense(int id)
        {
            var expense = _db.Expenses.FirstOrDefault(e => e.ExpenseId == id);
            if (expense == null)
                return Json(new { success = false, message = "Expense not found" });
            
            return Json(new { 
                success = true, 
                data = new {
                    expenseId = expense.ExpenseId,
                    type = expense.Type,
                    description = expense.Description,
                    amount = expense.Amount
                }
            });
        }

        // POST: Expenses/EditModal (AJAX)
        [HttpPost]
        public JsonResult EditModal([FromForm] ExpenseModel model)
        {
            if (ModelState.IsValid)
            {
                var expense = _db.Expenses.FirstOrDefault(e => e.ExpenseId == model.ExpenseId);
                if (expense == null)
                    return Json(new { success = false, message = "Expense not found" });
                
                expense.Type = model.Type;
                expense.Description = model.Description;
                expense.Amount = model.Amount;
                
                _db.Expenses.Update(expense);
                _db.SaveChanges();
                return Json(new { success = true });
            }
            return Json(new { success = false, message = "Invalid data" });
        }

        // GET: Expenses/Delete/{id}
        public IActionResult Delete(int id)
        {
            var expense = _db.Expenses.FirstOrDefault(e => e.ExpenseId == id);
            if (expense == null)
            {
                return NotFound();
            }
            return View(expense);
        }

        // POST: Expenses/Delete/{id}
        [HttpPost, ActionName("Delete")]
        public IActionResult DeleteConfirmed(int id)
        {
            var expense = _db.Expenses.FirstOrDefault(e => e.ExpenseId == id);
            if (expense == null)
            {
                return NotFound();
            }
            _db.Expenses.Remove(expense);
            _db.SaveChanges();
            return RedirectToAction("Index");
        }

        // POST: Expenses/DeleteExpense (AJAX)
        [HttpPost]
        public JsonResult DeleteExpense([FromForm]int expenseId)
        {
            var expense = _db.Expenses.FirstOrDefault(e => e.ExpenseId == expenseId);
            if (expense == null)
            {
                return Json(new { success = false, message = "Expense not found." });
            }
            _db.Expenses.Remove(expense);
            _db.SaveChanges();
            return Json(new { success = true });
        }
    }
}

