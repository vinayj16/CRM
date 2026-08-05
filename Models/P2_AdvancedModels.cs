using CRM.Helpers;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    /// <summary>
    /// P2-T1: Task Templates for reusable task definitions
    /// </summary>
    public class TaskTemplateModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int TemplateId { get; set; }
        
        [Required]
        [StringLength(200)]
        public string TemplateName { get; set; } = string.Empty;
        
        [Required]
        [StringLength(500)]
        public string TaskTitle { get; set; } = string.Empty;
        
        public string? Description { get; set; }
        
        [StringLength(20)]
        public string Priority { get; set; } = "Medium"; // Low, Medium, High, Urgent
        
        [StringLength(50)]
        public string Category { get; set; } = "General";
        
        public int EstimatedDurationHours { get; set; } = 1;
        
        [StringLength(20)]
        public string DefaultAssigneeRole { get; set; } = "Sales"; // Admin, Sales, Agent, Partner
        
        public bool IsActive { get; set; } = true;
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        public int? CreatedBy { get; set; }
    }

    /// <summary>
    /// P2-T2: Recurring Tasks with schedule definition
    /// </summary>
    public class RecurringTaskModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int RecurringTaskId { get; set; }
        
        [Required]
        [StringLength(200)]
        public string TaskTitle { get; set; } = string.Empty;
        
        public string? Description { get; set; }
        
        [Required]
        [StringLength(20)]
        public string RecurrencePattern { get; set; } = "Daily"; // Daily, Weekly, Monthly, Yearly
        
        public int RecurrenceInterval { get; set; } = 1; // Every X days/weeks/months
        
        public DateTime StartDate { get; set; } = IndianTime.Now;
        
        public DateTime? EndDate { get; set; }
        
        [StringLength(20)]
        public string Priority { get; set; } = "Medium";
        
        public int? AssignedTo { get; set; }
        
        public bool IsActive { get; set; } = true;
        
        public DateTime? LastGeneratedDate { get; set; }
        
        public DateTime? NextGenerationDate { get; set; }
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        public int? CreatedBy { get; set; }
    }

    /// <summary>
    /// P2-Q2: Quotation Templates for quick quote generation
    /// </summary>
    public class QuotationTemplateModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int TemplateId { get; set; }
        
        [Required]
        [StringLength(200)]
        public string TemplateName { get; set; } = string.Empty;
        
        [StringLength(500)]
        public string? Description { get; set; }
        
        public string? ItemsJson { get; set; } // JSON array of line items
        
        public string? TermsAndConditions { get; set; }
        
        public decimal DiscountPercentage { get; set; } = 0;
        
        public decimal TaxPercentage { get; set; } = 0;
        
        public int ValidityDays { get; set; } = 30;
        
        public bool IsActive { get; set; } = true;
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        public int? CreatedBy { get; set; }
        
        public DateTime? ModifiedOn { get; set; }
    }

    /// <summary>
    /// P2-Q3: Quotation Version History
    /// </summary>
    public class QuotationVersionModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int VersionId { get; set; }
        
        [Required]
        public int QuotationId { get; set; }
        
        public int VersionNumber { get; set; }
        
        public decimal TotalAmount { get; set; }
        
        public string? ItemsJson { get; set; } // Snapshot of line items
        
        public string? NotesJson { get; set; } // Snapshot of notes/terms
        
        [StringLength(200)]
        public string? ChangeReason { get; set; }
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        public int? CreatedBy { get; set; }
        
        public virtual QuotationModel? Quotation { get; set; }
    }

    /// <summary>
    /// P2-I4: Recurring Invoice for subscription/rental properties
    /// </summary>
    public class RecurringInvoiceModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int RecurringInvoiceId { get; set; }
        
        public int? CustomerId { get; set; } // LeadId or BookingId
        
        [Required]
        [StringLength(200)]
        public string InvoiceDescription { get; set; } = string.Empty;
        
        [Required]
        public decimal Amount { get; set; }
        
        [Required]
        [StringLength(20)]
        public string Frequency { get; set; } = "Monthly"; // Weekly, Monthly, Quarterly, Annually
        
        public DateTime StartDate { get; set; } = IndianTime.Now;
        
        public DateTime? EndDate { get; set; }
        
        public DateTime? LastGeneratedDate { get; set; }
        
        public DateTime? NextGenerationDate { get; set; }
        
        public int GeneratedInvoiceCount { get; set; } = 0;
        
        public bool IsActive { get; set; } = true;
        
        public bool AutoSendEmail { get; set; } = false;
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        public int? CreatedBy { get; set; }
    }
}
