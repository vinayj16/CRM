using CRM.Helpers;
using Microsoft.AspNetCore.Mvc;
using CRM.Attributes;
using CRM.Models;

namespace CRM.Controllers
{
    [RoleAuthorize("Admin")]
    public class FinancialController : Controller
    {        private readonly AppDbContext _context;
        private readonly ILogger<FinancialController> _logger;
        public FinancialController(AppDbContext context, ILogger<FinancialController> logger)
        {
            _context = context;
            _logger = logger;
        }
        [Route("paymentgateways")]
        public async Task<IActionResult> PaymentGateways()
        {
            var gateways = await _context.PaymentGateways.ToListAsync();
            return View(gateways);
        }

        [HttpPost]
        public async Task<IActionResult> SavePaymentGateway(PaymentGatewayModel model)
        {
            try
            {
                model.GatewayName = (model.GatewayName ?? string.Empty).Trim();
                model.KeyId = (model.KeyId ?? string.Empty).Trim();
                model.KeySecret = (model.KeySecret ?? string.Empty).Trim();
                model.WebhookSecret = (model.WebhookSecret ?? string.Empty).Trim();

                var existing = await _context.PaymentGateways
                    .FirstOrDefaultAsync(g => g.GatewayName == model.GatewayName);

                if (existing != null)
                {
                    existing.KeyId = model.KeyId;
                    existing.KeySecret = model.KeySecret;
                    existing.WebhookSecret = model.WebhookSecret;
                    existing.IsActive = model.IsActive;
                    existing.UpdatedOn = IndianTime.Now;
                }
                else
                {
                    _context.PaymentGateways.Add(model);
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Payment gateway saved successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
        [Route("bankAccounts")]
        public async Task<IActionResult> BankAccounts()
        {
            var accounts = await _context.BankAccounts.OrderByDescending(b => b.IsActive).ToListAsync();
            return View(accounts);
        }

        [HttpPost]
        public async Task<IActionResult> SaveBankAccount(BankAccountModel model)
        {
            try
            {
                model.AccountNumber = (model.AccountNumber ?? string.Empty).Replace(" ", string.Empty).Trim();
                model.IFSCCode = (model.IFSCCode ?? string.Empty).Trim().ToUpperInvariant();

                if (!ModelState.IsValid)
                {
                    var firstError = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .FirstOrDefault();

                    return Json(new { success = false, message = firstError ?? "Please check bank account details." });
                }

                if (model.IsActive)
                {
                    var activeAccounts = await _context.BankAccounts.Where(b => b.IsActive && b.AccountId != model.Id).ToListAsync();
                    foreach (var account in activeAccounts)
                    {
                        account.IsActive = false;
                        account.UpdatedOn = IndianTime.Now;
                        _context.BankAccounts.Update(account);
                    }
                }

                if (model.Id == 0)
                {
                    var existingAccounts = await _context.BankAccounts.ToListAsync();
                    var nextAccountId = existingAccounts.Any() ? existingAccounts.Max(a => a.AccountId) + 1 : 1;
                    model.AccountId = nextAccountId;
                    _context.BankAccounts.Add(model);
                }
                else
                {
                    var existing = await _context.BankAccounts.FirstOrDefaultAsync(b => b.AccountId == model.Id);
                    if (existing != null)
                    {
                        existing.AccountHolderName = model.AccountHolderName;
                        existing.AccountNumber = model.AccountNumber;
                        existing.BankName = model.BankName;
                        existing.IFSCCode = model.IFSCCode;
                        existing.BranchName = model.BranchName;
                        existing.AccountType = model.AccountType;
                        existing.IsActive = model.IsActive;
                        existing.UpdatedOn = IndianTime.Now;
                        _context.BankAccounts.Update(existing);
                    }
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Bank account saved successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteBankAccount(int id)
        {
            try
            {
                var account = await _context.BankAccounts.FirstOrDefaultAsync(b => b.AccountId == id);
                if (account != null)
                {
                    _context.BankAccounts.Remove(account);
                    await _context.SaveChangesAsync();
                }
                return Json(new { success = true, message = "Bank account deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}