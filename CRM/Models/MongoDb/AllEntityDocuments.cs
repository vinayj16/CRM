using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.Models.MongoDb
{
    // ===================== CORE ENTITIES =====================

    [BsonIgnoreExtraElements]
    public class UserDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Role { get; set; } = "Agent";
        public string? ResetToken { get; set; }
        public DateTime? ResetTokenExpiry { get; set; }
        public string? Phone { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public DateTime? LastActivity { get; set; }
        public int? ChannelPartnerId { get; set; }
        public string? DeviceToken { get; set; }
        public int TenantId { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class LeadDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int LeadId { get; set; }
        public string? Name { get; set; }
        public string? Contact { get; set; }
        public string? Email { get; set; }
        public string? Stage { get; set; }
        public string? Status { get; set; }
        public string? GroupName { get; set; }
        public string? Source { get; set; }
        public string? PreferredLocation { get; set; }
        public string? Sqft { get; set; }
        public string? Facing { get; set; }
        public string? Type { get; set; }
        public string? PropertyType { get; set; }
        public string? BHK { get; set; }
        public string? LocationDistance { get; set; }
        public string? Requirement { get; set; }
        public decimal? Budget { get; set; }
        public int? ExecutiveId { get; set; }
        public int? PartnerAssignedAgentUserId { get; set; }
        public DateTime? FollowUpDate { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }
        public string? Rating { get; set; }
        public string? Comments { get; set; }
        public int? ChannelPartnerId { get; set; }
        public string HandoverStatus { get; set; } = "Partner";
        public DateTime? HandoverDate { get; set; }
        public int? AdminAssignedTo { get; set; }
        public bool IsReadyToBook { get; set; } = false;
        public string? UtmSource { get; set; }
        public string? UtmMedium { get; set; }
        public string? UtmCampaign { get; set; }
        public string? UtmTerm { get; set; }
        public string? UtmContent { get; set; }
        public int TenantId { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class PropertyDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int PropertyId { get; set; }
        public string PropertyName { get; set; } = string.Empty;
        public int BuilderId { get; set; }
        public string? Developer { get; set; }
        public string? FlatNumber { get; set; }
        public string? FloorNumber { get; set; }
        public string? Unit { get; set; }
        public decimal? Price { get; set; }
        public string? PropertyGroup { get; set; }
        public int? PostedBy { get; set; }
        public decimal? AreaSqft { get; set; }
        public string? Location { get; set; }
        public string? PurchaseType { get; set; }
        public byte[]? PropertyImage { get; set; }
        public string? Inventory { get; set; }
        public int? AssignedTo { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public int? CreatedBy { get; set; }
        public DateTime? UpdatedOn { get; set; }
        public int? UpdatedBy { get; set; }
        public bool IsActive { get; set; } = true;
        public int TenantId { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class BookingDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int BookingId { get; set; }
        public string BookingNumber { get; set; } = string.Empty;
        public int LeadId { get; set; }
        public int PropertyId { get; set; }
        public int FlatId { get; set; }
        public int? QuotationId { get; set; }
        public DateTime BookingDate { get; set; } = DateTime.UtcNow;
        public decimal BookingAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public string PaymentType { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
        public DateTime? AgreementDate { get; set; }
        public string? AgreementPath { get; set; }
        public DateTime? PossessionDate { get; set; }
        public string? Notes { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }
        public int? ChannelPartnerId { get; set; }
        public int TenantId { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class PaymentDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int PaymentId { get; set; }
        public string ReceiptNumber { get; set; } = string.Empty;
        public int InvoiceId { get; set; }
        public int BookingId { get; set; }
        public int? InstallmentId { get; set; }
        public DateTime PaymentDate { get; set; } = DateTime.UtcNow;
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public string? TransactionReference { get; set; }
        public string? BankName { get; set; }
        public string? ChequeNumber { get; set; }
        public DateTime? ChequeDate { get; set; }
        public string? Notes { get; set; }
        public int? ReceivedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public int TenantId { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class QuotationDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int QuotationId { get; set; }
        public string QuotationNumber { get; set; } = string.Empty;
        public int LeadId { get; set; }
        public int PropertyId { get; set; }
        public int? FloorId { get; set; }
        public int? FlatId { get; set; }
        public DateTime QuotationDate { get; set; } = DateTime.UtcNow;
        public DateTime? ValidUntil { get; set; }
        public decimal BasePrice { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal GrandTotal { get; set; }
        public string Status { get; set; } = "Draft";
        public string? Notes { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }
        public int? ChannelPartnerId { get; set; }
        public List<QuotationItemSubDoc>? Items { get; set; }
        public int TenantId { get; set; }
    }

    public class QuotationItemSubDoc
    {
        public int ItemId { get; set; }
        public int QuotationId { get; set; }
        public string ItemType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public int Quantity { get; set; } = 1;
        public decimal Total { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class InvoiceDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int InvoiceId { get; set; }
        public string InvoiceNumber { get; set; } = string.Empty;
        public int BookingId { get; set; }
        public int? InstallmentId { get; set; }
        public DateTime InvoiceDate { get; set; } = DateTime.UtcNow;
        public DateTime DueDate { get; set; }
        public decimal Amount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public string Status { get; set; } = "Generated";
        public string? Notes { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }
        public List<InvoiceItemSubDoc>? Items { get; set; }
        public int TenantId { get; set; }
    }

    public class InvoiceItemSubDoc
    {
        public int ItemId { get; set; }
        public int InvoiceId { get; set; }
        public string Description { get; set; } = string.Empty;
        public int Quantity { get; set; } = 1;
        public decimal Rate { get; set; }
        public decimal Amount { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class NotificationDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int NotificationId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool IsRead { get; set; } = false;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public int? UserId { get; set; }
        public string? Link { get; set; }
        public int? RelatedEntityId { get; set; }
        public string? RelatedEntityType { get; set; }
        public string Priority { get; set; } = "Normal";
        public DateTime? ExpiresOn { get; set; }
        public DateTime? ReadOn { get; set; }
        public int TenantId { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class AgentDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int AgentId { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? AgentType { get; set; }
        public decimal? Salary { get; set; }
        public string? CommissionRules { get; set; }
        public string? Documents { get; set; }
        public string? Status { get; set; } = "Pending";
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public int? ApprovedBy { get; set; }
        public DateTime? ApprovedOn { get; set; }
        public int? ChannelPartnerId { get; set; }
        public int TenantId { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class ChannelPartnerDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int PartnerId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string ContactPerson { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? Address { get; set; }
        public string? CommissionScheme { get; set; }
        public string? Documents { get; set; }
        public string? Status { get; set; } = "Pending";
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public int? ApprovedBy { get; set; }
        public DateTime? ApprovedOn { get; set; }
        public int? UserId { get; set; }
        public decimal CommissionPercentage { get; set; } = 5.0m;
        public string? SubscriptionPlan { get; set; } = "Basic";
        public string? Subdomain { get; set; }
        public int TenantId { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class FollowUpDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int FollowUpId { get; set; }
        public int LeadId { get; set; }
        public string? Stage { get; set; }
        public string? Status { get; set; }
        public DateTime? FollowUpDate { get; set; }
        public string? FollowUpTime { get; set; }
        public string Comments { get; set; } = string.Empty;
        public int ExecutiveId { get; set; }
        public int? PropertyId { get; set; }
        public string? InterestStatus { get; set; }
        public string? Rating { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedOn { get; set; }
        public int? CompletedBy { get; set; }
        public string? CompletionNotes { get; set; }
        public bool? IsNotificationRead { get; set; } = false;
        public int TenantId { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class SettingsDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int SettingId { get; set; }
        public string SettingKey { get; set; } = string.Empty;
        public string? SettingValue { get; set; }
        public string? Description { get; set; }
        public string? SettingType { get; set; }
        public int? ChannelPartnerId { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public int TenantId { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class BrandingDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int BrandingId { get; set; }
        public string? CompanyLogo { get; set; }
        public string? LogoDisplayStyle { get; set; }
        public string? TwitterUrl { get; set; }
        public string? WhatsAppNumber { get; set; }
        public string? FacebookUrl { get; set; }
        public string? InstagramUrl { get; set; }
        public string? LinkedInUrl { get; set; }
        public string? AboutUsText { get; set; }
        public string? AboutUsImage { get; set; }
        public string? FooterLogo { get; set; }
        public string? CompanyInfo { get; set; }
        public string? TermsAndConditions { get; set; }
        public string? PrivacyPolicy { get; set; }
        public string? RefundPolicy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public bool IsActive { get; set; } = true;
        public int TenantId { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class ExpenseDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int ExpenseId { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime Date { get; set; } = DateTime.UtcNow;
        public int? ChannelPartnerId { get; set; }
        public int TenantId { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class RevenueDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int RevenueId { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime Date { get; set; } = DateTime.UtcNow;
        public int? ChannelPartnerId { get; set; }
        public int TenantId { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class RolePermissionDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int RolePermissionId { get; set; }
        public string RoleName { get; set; } = string.Empty;
        public bool CanCreate { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
        public bool CanView { get; set; }
        public string? AllowedModules { get; set; }
        public int? ChannelPartnerId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int TenantId { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class PermissionDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int PermissionId { get; set; }
        public string PermissionName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public int TenantId { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class WebhookLeadDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int WebhookLeadId { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Message { get; set; }
        public string? Source { get; set; }
        public bool IsProcessed { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public int TenantId { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class SubscriptionPlanDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int PlanId { get; set; }
        public string PlanName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public int DurationDays { get; set; } = 30;
        public int MaxLeads { get; set; } = 100;
        public int MaxAgents { get; set; } = 5;
        public int MaxPartners { get; set; } = 2;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public int TenantId { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class PartnerSubscriptionDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int SubscriptionId { get; set; }
        public int PartnerId { get; set; }
        public int PlanId { get; set; }
        public DateTime StartDate { get; set; } = DateTime.UtcNow;
        public DateTime EndDate { get; set; }
        public string Status { get; set; } = "Active";
        public decimal AmountPaid { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public int TenantId { get; set; }
    }

    // ===================== MASTER / SAAS ENTITIES =====================

    [BsonIgnoreExtraElements]
    public class TenantDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int TenantId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string ContactPerson { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? Subdomain { get; set; }
        public string Plan { get; set; } = "Basic";
        public int MaxUsers { get; set; } = 50;
        public bool IsActive { get; set; } = true;
        public bool IsSuspended { get; set; } = false;
        public string? SuspendedReason { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedOn { get; set; }
        public string Referral { get; set; } = string.Empty;
    }

    [BsonIgnoreExtraElements]
    public class SaasPlanDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int PlanId { get; set; }
        public string PlanName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal MonthlyPrice { get; set; }
        public decimal YearlyPrice { get; set; }
        public int MaxUsers { get; set; } = 5;
        public int MaxAgents { get; set; } = 2;
        public int MaxLeadsPerMonth { get; set; } = 500;
        public int MaxPartners { get; set; }
        public int MaxStorageGB { get; set; } = -1;
        public bool HasWhatsAppIntegration { get; set; }
        public bool HasFacebookIntegration { get; set; }
        public bool HasEmailIntegration { get; set; } = true;
        public bool HasCustomAPIAccess { get; set; }
        public bool HasAdvancedReports { get; set; }
        public bool HasCustomBranding { get; set; }
        public bool HasPrioritySupport { get; set; }
        public bool HasImpersonation { get; set; }
        public string SupportLevel { get; set; } = "Email";
        public string PlanType { get; set; } = "Basic";
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
        public bool? ShowOnLandingPage { get; set; } = true;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedOn { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class EmailDirectoryDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int DirectoryId { get; set; }
        public string Email { get; set; } = string.Empty;
        public int TenantId { get; set; }
        public bool IsPrimary { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }

    [BsonIgnoreExtraElements]
    public class SuperAdminDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int SuperAdminId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }

    [BsonIgnoreExtraElements]
    public class SaasPaymentTransactionDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int TransactionId { get; set; }
        public int TenantId { get; set; }
        public string? TenantCompanyName { get; set; }
        public string? TenantEmail { get; set; }
        public int? PlanId { get; set; }
        public string? PlanName { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "INR";
        public string PaymentMethod { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
        public string? TransactionRef { get; set; }
        public string? RazorpayOrderId { get; set; }
        public string? RazorpayPaymentId { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? PaidOn { get; set; }
        public string? InvoiceUrl { get; set; }
        public string? Notes { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class TenantSubscriptionDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int SubscriptionId { get; set; }
        public int TenantId { get; set; }
        public int PlanId { get; set; }
        public DateTime StartDate { get; set; } = DateTime.UtcNow;
        public DateTime EndDate { get; set; }
        public string Status { get; set; } = "Active";
        public decimal AmountPaid { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class LeadHistoryDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int HistoryId { get; set; }
        public int LeadId { get; set; }
        public string Activity { get; set; } = string.Empty;
        public int? ExecutiveId { get; set; }
        public DateTime ActivityDate { get; set; } = DateTime.UtcNow;
        public int TenantId { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class AuditLogDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int AuditId { get; set; }
        public int? UserId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public int? EntityId { get; set; }
        public string? OldValues { get; set; }
        public string? NewValues { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public int TenantId { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class UserProfileDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int ProfileId { get; set; }
        public int UserId { get; set; }
        public string? FullName { get; set; }
        public string? AvatarUrl { get; set; }
        public string? Bio { get; set; }
        public string? Address { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedOn { get; set; }
        public int TenantId { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class PropertyFlatDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int FlatId { get; set; }
        public int PropertyId { get; set; }
        public string? FlatName { get; set; }
        public decimal? TotalAmount { get; set; }
        public string? Status { get; set; }
        public int TenantId { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class BankAccountDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int AccountId { get; set; }
        public string? AccountHolderName { get; set; }
        public string? BankName { get; set; }
        public string? AccountNumber { get; set; }
        public string? IFSCCode { get; set; }
        public bool IsActive { get; set; } = true;
        public int TenantId { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class TestimonialDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public int TestimonialId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public int Rating { get; set; } = 5;
        public string? ImageBase64 { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public int TenantId { get; set; }
    }
}
