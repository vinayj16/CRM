using CRM.Helpers;
using Microsoft.AspNetCore.Mvc;
using CRM.Services;
using CRM.Models;
using CRM.Attributes;
using System.Text;
using ClosedXML.Excel;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.Kernel.Font;
using iText.IO.Font.Constants;

namespace CRM.Controllers
{
    [RoleAuthorize("Admin")]
    public class RazorpayTransactionsController : Controller
    {
        private readonly RazorpayService _razorpayService;
        private readonly AppDbContext _context;
        private readonly ILogger<RazorpayTransactionsController> _logger;

        public RazorpayTransactionsController(RazorpayService razorpayService, AppDbContext context, ILogger<RazorpayTransactionsController> logger)
        {
            _razorpayService = razorpayService;
            _context = context;
            _logger = logger;
        }
        [Route("razorpaytransactions")]
        public IActionResult Index(string? status, string? method, DateTime? fromDate, DateTime? toDate)
        {
            // Return view immediately with loading state
            ViewBag.Status = status;
            ViewBag.Method = method;
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
            ViewBag.IsLoading = true;
            
            return View(new List<dynamic>());
        }
        
        [HttpGet]
        public async Task<IActionResult> LoadTransactions(string? status, string? method, DateTime? fromDate, DateTime? toDate)
        {
            try
            {
                // Get payments with pagination for better performance
                var payments = await _razorpayService.GetAllPaymentsAsync();
                _logger.LogInformation($"Fetched {payments.Count} payments from Razorpay API");
                
                // Apply filters
                var filteredPayments = payments.AsEnumerable();
                
                if (!string.IsNullOrEmpty(status))
                    filteredPayments = filteredPayments.Where(p => p.status?.ToString() == status);
                
                if (!string.IsNullOrEmpty(method))
                    filteredPayments = filteredPayments.Where(p => p.method?.ToString() == method);
                
                if (fromDate.HasValue)
                {
                    var fromTimestamp = ((DateTimeOffset)fromDate.Value).ToUnixTimeSeconds();
                    filteredPayments = filteredPayments.Where(p => (long)p.created_at >= fromTimestamp);
                }
                
                if (toDate.HasValue)
                {
                    var toTimestamp = ((DateTimeOffset)toDate.Value.AddDays(1)).ToUnixTimeSeconds();
                    filteredPayments = filteredPayments.Where(p => (long)p.created_at <= toTimestamp);
                }
                
                var finalPayments = filteredPayments.OrderByDescending(p => (long)p.created_at).ToList();
                _logger.LogInformation($"After filtering: {finalPayments.Count} payments");
                
                return Json(new { 
                    success = true, 
                    data = finalPayments,
                    total = payments.Count,
                    filtered = finalPayments.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Razorpay transactions");
                
                string errorMessage;
                if (ex.Message.Contains("No such host is known") || ex.Message.Contains("Network error"))
                {
                    errorMessage = "Network connectivity issue: Cannot connect to Razorpay API. Please check your internet connection.";
                }
                else
                {
                    errorMessage = $"Failed to load transactions: {ex.Message}";
                }
                
                return Json(new { success = false, message = errorMessage });
            }
        }

        [HttpGet]
        public async Task<IActionResult> TestApi()
        {
            try
            {
                var payments = await _razorpayService.GetAllPaymentsAsync();
                return Json(new { 
                    success = true, 
                    count = payments.Count, 
                    data = payments.Take(3).ToList() 
                });
            }
            catch (Exception ex)
            {
                return Json(new { 
                    success = false, 
                    error = ex.Message,
                    suggestion = "Check internet connection and Razorpay credentials"
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetPaymentDetails(string paymentId)
        {
            try
            {
                var paymentDetails = await _razorpayService.GetPaymentDetailsAsync(paymentId);
                return Json(new { success = true, data = paymentDetails });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching payment details for {paymentId}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetRefunds(string paymentId)
        {
            try
            {
                var refunds = await _razorpayService.GetRefundsAsync(paymentId);
                return Json(new { success = true, data = refunds });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching refunds for {paymentId}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ExportTransactions(string format = "excel", string? status = null, string? method = null, DateTime? fromDate = null, DateTime? toDate = null)
        {
            try
            {
                // Get all payments from Razorpay API
                var payments = await _razorpayService.GetAllPaymentsAsync();
                
                // Apply same filters as LoadTransactions
                var filteredPayments = payments.AsEnumerable();
                
                if (!string.IsNullOrEmpty(status))
                    filteredPayments = filteredPayments.Where(p => p.status?.ToString() == status);
                
                if (!string.IsNullOrEmpty(method))
                    filteredPayments = filteredPayments.Where(p => p.method?.ToString() == method);
                
                if (fromDate.HasValue)
                {
                    var fromTimestamp = ((DateTimeOffset)fromDate.Value).ToUnixTimeSeconds();
                    filteredPayments = filteredPayments.Where(p => (long)p.created_at >= fromTimestamp);
                }
                
                if (toDate.HasValue)
                {
                    var toTimestamp = ((DateTimeOffset)toDate.Value.AddDays(1)).ToUnixTimeSeconds();
                    filteredPayments = filteredPayments.Where(p => (long)p.created_at <= toTimestamp);
                }
                
                var finalPayments = filteredPayments.ToList();

                switch (format.ToLower())
                {
                    case "csv":
                        var csv = GenerateCSV(finalPayments);
                        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"RazorpayTransactions_{IndianTime.Now:yyyy-MM-dd}.csv");
                    
                    case "excel":
                        var excel = GenerateExcel(finalPayments);
                        return File(excel, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"RazorpayTransactions_{IndianTime.Now:yyyy-MM-dd}.xlsx");
                    
                    case "pdf":
                        var pdf = GeneratePDF(finalPayments);
                        return File(pdf, "application/pdf", $"RazorpayTransactions_{IndianTime.Now:yyyy-MM-dd}.pdf");
                    
                    default:
                        return BadRequest("Invalid format specified");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting Razorpay transactions");
                return BadRequest("Failed to export transactions");
            }
        }

        private string GenerateCSV(List<dynamic> payments)
        {
            var csv = "Date,Payment ID,Amount (INR),Status,Method,Email,Description\n";
            
            foreach (var payment in payments)
            {
                var date = DateTime.UnixEpoch.AddSeconds((long)payment.created_at).ToString("yyyy-MM-dd HH:mm");
                var amount = ((long)payment.amount / 100m).ToString("N2");
                var email = payment.email?.ToString() ?? "N/A";
                
                csv += $"{date}," +
                       $"{payment.id}," +
                       $"{amount}," +
                       $"{payment.status}," +
                       $"{payment.method ?? "N/A"}," +
                       $"{email}," +
                       $"Razorpay Payment\n";
            }
            
            return csv;
        }

        private byte[] GenerateExcel(List<dynamic> payments)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Razorpay Transactions");
            
            // Headers
            worksheet.Cell(1, 1).Value = "Date";
            worksheet.Cell(1, 2).Value = "Payment ID";
            worksheet.Cell(1, 3).Value = "Amount (INR)";
            worksheet.Cell(1, 4).Value = "Status";
            worksheet.Cell(1, 5).Value = "Method";
            worksheet.Cell(1, 6).Value = "Email";
            worksheet.Cell(1, 7).Value = "Description";
            
            // Style headers
            var headerRange = worksheet.Range(1, 1, 1, 7);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;
            
            // Data
            int row = 2;
            foreach (var payment in payments)
            {
                worksheet.Cell(row, 1).Value = DateTime.UnixEpoch.AddSeconds((long)payment.created_at);
                worksheet.Cell(row, 2).Value = payment.id?.ToString() ?? "";
                worksheet.Cell(row, 3).Value = (long)payment.amount / 100m;
                worksheet.Cell(row, 4).Value = payment.status?.ToString() ?? "";
                worksheet.Cell(row, 5).Value = payment.method?.ToString() ?? "N/A";
                worksheet.Cell(row, 6).Value = payment.email?.ToString() ?? "N/A";
                worksheet.Cell(row, 7).Value = "Razorpay Payment";
                row++;
            }
            
            // Auto-fit columns
            worksheet.Columns().AdjustToContents();
            
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private byte[] GeneratePDF(List<dynamic> payments)
        {
            using var stream = new MemoryStream();
            using var writer = new PdfWriter(stream);
            using var pdf = new PdfDocument(writer);
            var document = new Document(pdf);
            
            // Title
            var titleFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            document.Add(new Paragraph("Razorpay Transactions Report")
                .SetFont(titleFont)
                .SetFontSize(18)
                .SetTextAlignment(TextAlignment.CENTER));
            
            document.Add(new Paragraph($"Generated on: {IndianTime.Now:yyyy-MM-dd HH:mm}")
                .SetFontSize(10)
                .SetTextAlignment(TextAlignment.CENTER));
            
            document.Add(new Paragraph(" ")); // Space
            
            // Table
            var table = new Table(6).UseAllAvailableWidth();
            
            // Headers
            table.AddHeaderCell("Date");
            table.AddHeaderCell("Payment ID");
            table.AddHeaderCell("Amount (INR)");
            table.AddHeaderCell("Status");
            table.AddHeaderCell("Method");
            table.AddHeaderCell("Email");
            
            // Data
            foreach (var payment in payments)
            {
                table.AddCell(DateTime.UnixEpoch.AddSeconds((long)payment.created_at).ToString("yyyy-MM-dd HH:mm"));
                table.AddCell(payment.id?.ToString() ?? "");
                table.AddCell(((long)payment.amount / 100m).ToString("N2"));
                table.AddCell(payment.status?.ToString() ?? "");
                table.AddCell(payment.method?.ToString() ?? "N/A");
                table.AddCell(payment.email?.ToString() ?? "N/A");
            }
            
            document.Add(table);
            document.Close();
            
            return stream.ToArray();
        }
    }
}