using CRM.Helpers;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    /// <summary>
    /// P3-A7: Two-Factor Authentication settings per user
    /// </summary>
    public class TwoFactorAuthModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int TwoFactorId { get; set; }
        
        [Required]
        public int UserId { get; set; }
        
        public bool IsEnabled { get; set; } = false;
        
        [StringLength(20)]
        public string Method { get; set; } = "SMS"; // SMS, Email, Authenticator
        
        [StringLength(500)]
        public string? SecretKey { get; set; } // For authenticator apps
        
        [StringLength(500)]
        public string? BackupCodes { get; set; } // Comma-separated backup codes
        
        public DateTime? EnabledOn { get; set; }
        
        public DateTime? LastUsedOn { get; set; }
        
        public virtual UserModel? User { get; set; }
    }

    /// <summary>
    /// P3-L8: Lead Scoring Configuration
    /// </summary>
    public class LeadScoringRuleModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int RuleId { get; set; }
        
        [Required]
        [StringLength(200)]
        public string RuleName { get; set; } = string.Empty;
        
        [StringLength(50)]
        public string Criteria { get; set; } = string.Empty; // Budget, Source, Engagement, etc.
        
        [StringLength(20)]
        public string Operator { get; set; } = "Equals"; // Equals, GreaterThan, LessThan, Contains
        
        [StringLength(200)]
        public string Value { get; set; } = string.Empty;
        
        public int ScorePoints { get; set; } = 0;
        
        public bool IsActive { get; set; } = true;
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
    }

    /// <summary>
    /// P3-CP4: Partner Hierarchy for multi-level commission
    /// </summary>
    public class PartnerHierarchyModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int HierarchyId { get; set; }
        
        [Required]
        public int PartnerId { get; set; }
        
        public int? ParentPartnerId { get; set; } // Null for top-level partners
        
        public int HierarchyLevel { get; set; } = 1; // 1 = Direct, 2 = Sub-partner, etc.
        
        public decimal CommissionPercentage { get; set; } = 0;
        
        public DateTime JoinedOn { get; set; } = IndianTime.Now;
        
        public virtual ChannelPartnerModel? Partner { get; set; }
        
        public virtual ChannelPartnerModel? ParentPartner { get; set; }
    }

    /// <summary>
    /// P3-PR4: Virtual Tour 360° links for properties
    /// </summary>
    public class VirtualTourModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int TourId { get; set; }
        
        [Required]
        public int PropertyId { get; set; }
        
        [Required]
        [StringLength(200)]
        public string TourTitle { get; set; } = string.Empty;
        
        [Required]
        [StringLength(1000)]
        public string TourUrl { get; set; } = string.Empty; // URL to 360° tour (Matterport, etc.)
        
        [StringLength(50)]
        public string TourType { get; set; } = "360Video"; // 360Video, 3DWalkthrough, VirtualReality
        
        public int ViewCount { get; set; } = 0;
        
        public bool IsActive { get; set; } = true;
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        public int? CreatedBy { get; set; }
        
        public virtual PropertyModel? Property { get; set; }
    }

    /// <summary>
    /// P3-Q5: Quotation Approval Workflow for client portal
    /// </summary>
    public class QuotationApprovalModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int ApprovalId { get; set; }
        
        [Required]
        public int QuotationId { get; set; }
        
        [Required]
        [StringLength(100)]
        public string ClientEmail { get; set; } = string.Empty;
        
        [Required]
        [StringLength(500)]
        public string ApprovalToken { get; set; } = string.Empty; // Unique link token
        
        [StringLength(20)]
        public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected, Expired
        
        public DateTime? ApprovedOn { get; set; }
        
        public string? ClientComments { get; set; }
        
        [StringLength(200)]
        public string? ClientIPAddress { get; set; }
        
        [StringLength(500)]
        public string? ClientSignature { get; set; } // Base64 signature image
        
        public DateTime ExpiresOn { get; set; }
        
        public DateTime SentOn { get; set; } = IndianTime.Now;
        
        public int? SentBy { get; set; }
        
        public virtual QuotationModel? Quotation { get; set; }
    }

    /// <summary>
    /// P3-AT1: Biometric Attendance Integration
    /// </summary>
    public class BiometricAttendanceModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int BiometricId { get; set; }
        
        [Required]
        public int AgentId { get; set; }
        
        [Required]
        [StringLength(100)]
        public string DeviceId { get; set; } = string.Empty;
        
        [Required]
        [StringLength(100)]
        public string BiometricDeviceRecordId { get; set; } = string.Empty;
        
        public DateTime PunchTime { get; set; } = IndianTime.Now;
        
        [StringLength(20)]
        public string PunchType { get; set; } = "In"; // In, Out
        
        [StringLength(200)]
        public string? LocationGPS { get; set; }
        
        public bool IsVerified { get; set; } = true;
        
        public DateTime SyncedOn { get; set; } = IndianTime.Now;
        
        public virtual AgentModel? Agent { get; set; }
    }

    /// <summary>
    /// P3-T4: Task Dependencies for Gantt chart
    /// </summary>
    public class TaskDependencyModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int DependencyId { get; set; }
        
        [Required]
        public int TaskId { get; set; }
        
        [Required]
        public int DependsOnTaskId { get; set; }
        
        [StringLength(20)]
        public string DependencyType { get; set; } = "FinishToStart"; // FinishToStart, StartToStart, FinishToFinish, StartToFinish
        
        public int LagDays { get; set; } = 0; // Delay in days
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        public virtual RecurringTaskModel? Task { get; set; }
        
        public virtual RecurringTaskModel? DependsOnTask { get; set; }
    }

    /// <summary>
    /// P3-R3: Custom Report Builder definitions
    /// </summary>
    public class CustomReportModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int ReportId { get; set; }
        
        [Required]
        [StringLength(200)]
        public string ReportName { get; set; } = string.Empty;
        
        [StringLength(500)]
        public string? Description { get; set; }
        
        [Required]
        [StringLength(50)]
        public string DataSource { get; set; } = string.Empty; // Leads, Bookings, Payments, etc.
        
        public string? ColumnsJson { get; set; } // Selected columns
        
        public string? FiltersJson { get; set; } // WHERE conditions
        
        public string? SortingJson { get; set; } // ORDER BY
        
        public string? GroupingJson { get; set; } // GROUP BY
        
        [StringLength(50)]
        public string ChartType { get; set; } = "Table"; // Table, Bar, Line, Pie, etc.
        
        public bool IsPublic { get; set; } = false;
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        public int? CreatedBy { get; set; }
        
        public DateTime? ModifiedOn { get; set; }
    }

    /// <summary>
    /// P3-W3: Zapier Integration Webhook Logs
    /// </summary>
    public class ZapierWebhookModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int ZapWebhookId { get; set; }
        
        [Required]
        [StringLength(500)]
        public string WebhookUrl { get; set; } = string.Empty;
        
        [Required]
        [StringLength(50)]
        public string TriggerEvent { get; set; } = string.Empty; // LeadCreated, BookingCreated, PaymentReceived, etc.
        
        public bool IsActive { get; set; } = true;
        
        public DateTime LastTriggeredOn { get; set; }
        
        public int TriggerCount { get; set; } = 0;
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        public int? CreatedBy { get; set; }
    }

    /// <summary>
    /// P3-W4: AI Lead Scoring History
    /// </summary>
    public class AILeadScoreModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int ScoreId { get; set; }
        
        [Required]
        public int LeadId { get; set; }
        
        public int AIScore { get; set; } = 0; // 0-100
        
        [StringLength(20)]
        public string ScoreCategory { get; set; } = "Cold"; // Hot, Warm, Cold
        
        public string? AIReasoningJson { get; set; } // JSON of factors
        
        public decimal ConversionProbability { get; set; } = 0; // 0-1
        
        public string? RecommendedActions { get; set; }
        
        public DateTime ScoredOn { get; set; } = IndianTime.Now;
        
        [StringLength(50)]
        public string AIModel { get; set; } = "v1.0";
        
        public virtual LeadModel? Lead { get; set; }
    }
}
