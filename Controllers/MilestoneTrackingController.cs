using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using CRM.Models;
using Microsoft.Extensions.Logging;

namespace CRM.Controllers
{
    public class MilestoneTrackingController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ILogger<MilestoneTrackingController> _logger;
        public MilestoneTrackingController(AppDbContext db, ILogger<MilestoneTrackingController> logger)
        {
            _db = db;
            _logger = logger;
        }

        public IActionResult Index(string search = "")
        {
            var milestones = (from installment in _db.PaymentInstallments.AsQueryable()
                              join plan in _db.PaymentPlans.AsQueryable() on installment.PlanId equals plan.PlanId
                              join booking in _db.Bookings.AsQueryable() on plan.BookingId equals booking.BookingId
                              join lead in _db.Leads.AsQueryable() on booking.LeadId equals lead.LeadId
                              join property in _db.Properties.AsQueryable() on booking.PropertyId equals property.PropertyId
                              join flat in _db.PropertyFlats.AsQueryable() on booking.FlatId equals flat.FlatId
                              select new MilestoneTrackingViewModel
                              {
                                  LeadName = lead.Name,
                                  Project = property.PropertyName,
                                  Flat = flat.FlatName + (flat.BHK != null ? " - " + flat.BHK : ""),
                                  Milestone = installment.MilestoneName,
                                  DueDate = installment.DueDate,
                                  Amount = installment.Amount,
                                  Paid = installment.PaidAmount,
                                  Status = installment.Status
                              }).ToList();

            if (!string.IsNullOrEmpty(search))
            {
                search = search.ToLower();
                milestones = milestones.Where(m =>
                    (m.LeadName != null && m.LeadName.ToLower().Contains(search)) ||
                    (m.Project != null && m.Project.ToLower().Contains(search)) ||
                    (m.Flat != null && m.Flat.ToLower().Contains(search)) ||
                    (m.Milestone != null && m.Milestone.ToLower().Contains(search)) ||
                    (m.Status != null && m.Status.ToLower().Contains(search))
                ).ToList();
            }

            return View(milestones);
        }
    }

    public class MilestoneTrackingViewModel
    {
    public string LeadName { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public string Flat { get; set; } = string.Empty;
    public string Milestone { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }
    public decimal Paid { get; set; }
    public string Status { get; set; } = string.Empty;
    }
}
