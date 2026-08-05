using System.Collections.Generic;
using CRM.Models;

namespace CRM.ViewModels
{
    public class ExpenseRevenueProfitViewModel
    {
        public List<ExpenseModel> Expenses { get; set; } = new List<ExpenseModel>();
        public List<RevenueModel> Revenues { get; set; } = new List<RevenueModel>();
        public decimal TotalExpenses { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal Profit { get; set; }
    }
}
