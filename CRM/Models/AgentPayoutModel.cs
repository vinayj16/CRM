using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    public class AgentPayoutModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int PayoutId { get; set; }
        
        [Required]
        public int AgentId { get; set; }
        
        [Required]
        public string Month { get; set; } // Format: "Nov-2024"
        
        [Required]
        public int Year { get; set; }
        
        public decimal BaseSalary { get; set; } = 0;
        
        public decimal AttendanceDeduction { get; set; } = 0;
        
        public decimal CommissionAmount { get; set; } = 0;
        
        public decimal FinalPayout { get; set; } = 0;
        
        public int TotalSales { get; set; } = 0;
        
        public int WorkingDays { get; set; } = 0;
        
        public int PresentDays { get; set; } = 0;
        
        public string Status { get; set; } = "Pending"; // Pending, Processed, Paid
        
        public string Type { get; set; } = "Monthly"; // Monthly, Bonus, Adjustment
        
        public decimal Amount { get; set; } = 0; // Total payout amount
        
        public string Period { get; set; } = ""; // Format: "Nov-2025"
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        public DateTime? ProcessedOn { get; set; }
        
        // Navigation properties
        public AgentModel? Agent { get; set; }
    }
}