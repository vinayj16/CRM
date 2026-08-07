using MongoDB.Driver;
using CRM.Models;
using CRM.MasterDb.Models;
using CRM.Models.Chatbot;
using CRM.Services;
using Microsoft.AspNetCore.Http;

namespace CRM
{
    /// <summary>
    /// MongoDB-backed DbContext replacement.
    /// Provides MongoDbSet{T} properties for all entity types,
    /// wrapping IMongoCollection{T} with EF Core-compatible instance methods.
    /// </summary>
    public class AppDbContext
    {
        private readonly MongoDbContext _mongo;
        private readonly ITenantService? _tenantService;
        private readonly IHttpContextAccessor? _httpContextAccessor;

        public AppDbContext(MongoDbContext mongo, ITenantService? tenantService = null, IHttpContextAccessor? httpContextAccessor = null)
        {
            _mongo = mongo;
            _tenantService = tenantService;
            _httpContextAccessor = httpContextAccessor;
        }

        /// <summary>
        /// Database facade for transaction support (MongoDB no-op).
        /// </summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public DbFacade Database => new DbFacade();

        /// <summary>
        /// SaveChanges - no-op in MongoDB since operations are auto-saved.
        /// </summary>
        public int SaveChanges() => 0;

        /// <summary>
        /// SaveChangesAsync - no-op in MongoDB since operations are auto-saved.
        /// </summary>
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        /// <summary>
        /// Gets a typed MongoDbSet for the given collection name.
        /// </summary>
        private MongoDbSet<T> Set<T>(string name) where T : class
            => new MongoDbSet<T>(_mongo.GetCollection<T>(name), _tenantService, _httpContextAccessor);

        // ===================== TENANT-SCOPED COLLECTIONS =====================

        public MongoDbSet<UserModel> Users => Set<UserModel>("users");
        public MongoDbSet<LeadModel> Leads => Set<LeadModel>("leads");
        public MongoDbSet<PropertyModel> Properties => Set<PropertyModel>("properties");
        public MongoDbSet<BookingModel> Bookings => Set<BookingModel>("bookings");
        public MongoDbSet<PaymentModel> Payments => Set<PaymentModel>("payments");
        public MongoDbSet<QuotationModel> Quotations => Set<QuotationModel>("quotations");
        public MongoDbSet<InvoiceModel> Invoices => Set<InvoiceModel>("invoices");
        public MongoDbSet<NotificationModel> Notifications => Set<NotificationModel>("notifications");
        public MongoDbSet<FollowUpModel> FollowUps => Set<FollowUpModel>("followups");
        public MongoDbSet<AgentModel> Agents => Set<AgentModel>("agents");
        public MongoDbSet<AgentDocumentModel> AgentDocuments => Set<AgentDocumentModel>("agent_documents");
        public MongoDbSet<ChannelPartnerModel> ChannelPartners => Set<ChannelPartnerModel>("channel_partners");
        public MongoDbSet<ChannelPartnerDocumentModel> ChannelPartnerDocuments => Set<ChannelPartnerDocumentModel>("channel_partner_documents");
        public MongoDbSet<SettingsModel> Settings => Set<SettingsModel>("settings");
        public MongoDbSet<BrandingModel> Branding => Set<BrandingModel>("branding");
        public MongoDbSet<ExpenseModel> Expenses => Set<ExpenseModel>("expenses");
        public MongoDbSet<RevenueModel> Revenues => Set<RevenueModel>("revenues");
        public MongoDbSet<RolePermission> RolePermissions => Set<RolePermission>("role_permissions");
        public MongoDbSet<PermissionModel> Permissions => Set<PermissionModel>("permissions");
        public MongoDbSet<LeadLogModel> LeadLogs => Set<LeadLogModel>("lead_logs");
        public MongoDbSet<LeadNoteModel> LeadNotes => Set<LeadNoteModel>("lead_notes");
        public MongoDbSet<LeadHistoryModel> LeadHistories => Set<LeadHistoryModel>("lead_histories");
        public MongoDbSet<WebhookLeadModel> WebhookLeads => Set<WebhookLeadModel>("webhook_leads");
        public MongoDbSet<EmailTemplateModel> EmailTemplates => Set<EmailTemplateModel>("email_templates");
        public MongoDbSet<EmailSettingModel> EmailSettings => Set<EmailSettingModel>("email_settings");
        public MongoDbSet<EmailLogModel> EmailLogs => Set<EmailLogModel>("email_logs");
        public MongoDbSet<UserProfile> UserProfiles => Set<UserProfile>("user_profiles");
        public MongoDbSet<PaymentTransactionModel> PaymentTransactions => Set<PaymentTransactionModel>("payment_transactions");
        public MongoDbSet<PaymentPlanModel> PaymentPlans => Set<PaymentPlanModel>("payment_plans");
        public MongoDbSet<PropertyFlatModel> PropertyFlats => Set<PropertyFlatModel>("property_flats");
        public MongoDbSet<PropertyDocumentModel> PropertyDocuments => Set<PropertyDocumentModel>("property_documents");
        public MongoDbSet<PropertyGalleryModel> PropertyGallery => Set<PropertyGalleryModel>("property_gallery");
        public MongoDbSet<BookingAmendmentModel> BookingAmendments => Set<BookingAmendmentModel>("booking_amendments");
        public MongoDbSet<BuilderModel> Builders => Set<BuilderModel>("builders");
        public MongoDbSet<AgentAttendanceModel> AgentAttendances => Set<AgentAttendanceModel>("agent_attendances");
        public MongoDbSet<AgentPayoutModel> AgentPayouts => Set<AgentPayoutModel>("agent_payouts");
        public MongoDbSet<AgentCommissionLogModel> AgentCommissionLogs => Set<AgentCommissionLogModel>("agent_commission_logs");
        public MongoDbSet<ChannelPartnerCommissionLogModel> ChannelPartnerCommissionLogs => Set<ChannelPartnerCommissionLogModel>("channel_partner_commission_logs");
        public MongoDbSet<PartnerCommissionModel> PartnerCommissions => Set<PartnerCommissionModel>("partner_commissions");
        public MongoDbSet<PartnerLeadModel> PartnerLeads => Set<PartnerLeadModel>("partner_leads");
        public MongoDbSet<PartnerPayoutModel> PartnerPayouts => Set<PartnerPayoutModel>("partner_payouts");
        public MongoDbSet<PartnerSubscriptionModel> PartnerSubscriptions => Set<PartnerSubscriptionModel>("partner_subscriptions");
        public MongoDbSet<SubscriptionPlanModel> SubscriptionPlans => Set<SubscriptionPlanModel>("subscription_plans");
        public MongoDbSet<SubscriptionAddonModel> SubscriptionAddons => Set<SubscriptionAddonModel>("subscription_addons");
        public MongoDbSet<LeadIntegrationConfigModel> LeadIntegrationConfigs => Set<LeadIntegrationConfigModel>("lead_integration_configs");
        public MongoDbSet<LeaveRequestModel> LeaveRequests => Set<LeaveRequestModel>("leave_requests");
        public MongoDbSet<AttendanceLogModel> AttendanceLogs => Set<AttendanceLogModel>("attendance_logs");
        public MongoDbSet<AuditLogModel> AuditLogs => Set<AuditLogModel>("audit_logs");
        public MongoDbSet<UserFavorite> UserFavorites => Set<UserFavorite>("user_favorites");
        public MongoDbSet<UserRecentSearch> UserRecentSearches => Set<UserRecentSearch>("user_recent_searches");
        public MongoDbSet<UserSettings> UserSettings => Set<UserSettings>("user_settings");
        public MongoDbSet<UserDashboardSetting> UserDashboardSettings => Set<UserDashboardSetting>("user_dashboard_settings");
        public MongoDbSet<ProjectInterest> ProjectInterests => Set<ProjectInterest>("project_interests");
        public MongoDbSet<DuplicateLeadModel> DuplicateLeads => Set<DuplicateLeadModel>("duplicate_leads");
        public MongoDbSet<RolePagePermissionModel> RolePagePermissions => Set<RolePagePermissionModel>("role_page_permissions");
        public MongoDbSet<ModuleModel> Modules => Set<ModuleModel>("modules");
        public MongoDbSet<PropertyAgentModel> PropertyAgents => Set<PropertyAgentModel>("property_agents");
        public MongoDbSet<PropertyHistoryModel> PropertyHistories => Set<PropertyHistoryModel>("property_histories");
        public MongoDbSet<PropertyUploadModel> PropertyUploads => Set<PropertyUploadModel>("property_uploads");
        public MongoDbSet<LeadUploadModel> LeadUploads => Set<LeadUploadModel>("lead_uploads");
        public MongoDbSet<LeadHandoverAuditModel> LeadHandoverAudits => Set<LeadHandoverAuditModel>("lead_handover_audits");
        public MongoDbSet<NotificationPreferenceModel> NotificationPreferences => Set<NotificationPreferenceModel>("notification_preferences");
        public MongoDbSet<ReferralEarningModel> ReferralEarnings => Set<ReferralEarningModel>("referral_earnings");
        public MongoDbSet<TestimonialModel> Testimonials => Set<TestimonialModel>("testimonials");
        public MongoDbSet<BankAccountModel> BankAccounts => Set<BankAccountModel>("bank_accounts");
        public MongoDbSet<PaymentGatewayModel> PaymentGateways => Set<PaymentGatewayModel>("payment_gateways");
        public MongoDbSet<QuotationItemModel> QuotationItems => Set<QuotationItemModel>("quotation_items");
        public MongoDbSet<InvoiceItemModel> InvoiceItems => Set<InvoiceItemModel>("invoice_items");
        public MongoDbSet<BookingDocumentModel> BookingDocuments => Set<BookingDocumentModel>("booking_documents");
        public MongoDbSet<PaymentInstallmentModel> PaymentInstallments => Set<PaymentInstallmentModel>("payment_installments");
        public MongoDbSet<WebhookRetryQueueModel> WebhookRetryQueue => Set<WebhookRetryQueueModel>("webhook_retry_queue");
        public MongoDbSet<WhatsAppLogModel> WhatsAppLogs => Set<WhatsAppLogModel>("whatsapp_logs");
        public MongoDbSet<SupportTicketModel> Tickets => Set<SupportTicketModel>("support_tickets");
        public MongoDbSet<SupportTicketModel> SupportTickets => Set<SupportTicketModel>("support_tickets");

        // ===================== NEW MODULES (Site Visits, Scoring, Campaigns, Legal, Inventory) =====================
        public MongoDbSet<SiteVisitModel> SiteVisits => Set<SiteVisitModel>("site_visits");
        public MongoDbSet<LeadScoreModel> LeadScores => Set<LeadScoreModel>("lead_scores");
        public MongoDbSet<CampaignModel> Campaigns => Set<CampaignModel>("campaigns");
        public MongoDbSet<LegalCaseModel> LegalCases => Set<LegalCaseModel>("legal_cases");
        public MongoDbSet<InventoryUnitModel> InventoryUnits => Set<InventoryUnitModel>("inventory_units");

        // Singular aliases (code convention)
        public MongoDbSet<LeadHistoryModel> LeadHistory => LeadHistories;
        public MongoDbSet<PropertyHistoryModel> PropertyHistory => PropertyHistories;
        public MongoDbSet<AgentAttendanceModel> AgentAttendance => AgentAttendances;
        public MongoDbSet<AttendanceLogModel> AttendanceLog => AttendanceLogs;
        public MongoDbSet<LeadHandoverAuditModel> LeadHandoverAudit => LeadHandoverAudits;
        public MongoDbSet<FollowUpModel> LeadFollowUps => FollowUps;

        // ===================== CHAT / CHATBOT COLLECTIONS =====================

        public MongoDbSet<ChatbotConversation> ChatbotConversations => Set<ChatbotConversation>("chatbot_conversations");
        public MongoDbSet<ChatbotMessage> ChatbotMessages => Set<ChatbotMessage>("chatbot_messages");
        public MongoDbSet<ChatbotKnowledge> ChatbotKnowledge => Set<ChatbotKnowledge>("chatbot_knowledge");
        public MongoDbSet<ChatSessionModel> ChatSessions => Set<ChatSessionModel>("chat_sessions");
        public MongoDbSet<ChatLogModel> ChatLogs => Set<ChatLogModel>("chat_logs");
        public MongoDbSet<ChatIntentModel> ChatIntents => Set<ChatIntentModel>("chat_intents");
        public MongoDbSet<ChatbotSettings> ChatbotSettings => Set<ChatbotSettings>("chatbot_settings");
        public MongoDbSet<ChatAgent> ChatAgents => Set<ChatAgent>("chat_agents");
        public MongoDbSet<RealTimeChatMessage> RealTimeChatMessages => Set<RealTimeChatMessage>("real_time_chat_messages");
        public MongoDbSet<CompanyMessageModel> CompanyMessages => Set<CompanyMessageModel>("company_messages");
        public MongoDbSet<AgentChatStatus> AgentChatStatus => Set<AgentChatStatus>("agent_chat_status");
        public MongoDbSet<ChatConversationAssignment> ChatConversationAssignments => Set<ChatConversationAssignment>("chat_conversation_assignments");
        public MongoDbSet<ChatNotification> ChatNotifications => Set<ChatNotification>("chat_notifications");
        public MongoDbSet<ChatMessageMetrics> ChatMessageMetrics => Set<ChatMessageMetrics>("chat_message_metrics");

        // ===================== MASTER / SAAS COLLECTIONS =====================

        public MongoDbSet<TenantModel> Tenants => Set<TenantModel>("tenants");
        public MongoDbSet<EmailDirectoryModel> EmailDirectory => Set<EmailDirectoryModel>("email_directory");
        public MongoDbSet<SaasSubscriptionPlanModel> SaasPlans => Set<SaasSubscriptionPlanModel>("saas_plans");
        public MongoDbSet<SuperAdminModel> SuperAdmins => Set<SuperAdminModel>("super_admins");
        public MongoDbSet<SaasPaymentTransactionModel> SaasPaymentTransactions => Set<SaasPaymentTransactionModel>("saas_payment_transactions");
        public MongoDbSet<TenantSubscriptionModel> TenantSubscriptions => Set<TenantSubscriptionModel>("tenant_subscriptions");
        public MongoDbSet<InquiryFormModel> InquiryForms => Set<InquiryFormModel>("inquiry_forms");
        public MongoDbSet<InquiryModel> Inquiries => Set<InquiryModel>("inquiries");
        public MongoDbSet<SaasBrandingModel> SaasBrandings => Set<SaasBrandingModel>("saas_brandings");
        public MongoDbSet<SaasSettingsModel> SaasSettings => Set<SaasSettingsModel>("saas_settings");
        public MongoDbSet<SaasPaymentConfigModel> SaasPaymentConfigs => Set<SaasPaymentConfigModel>("saas_payment_configs");
        public MongoDbSet<PageModel> Pages => Set<PageModel>("pages");

        // Maintenance logs
        public MongoDbSet<MaintenanceLogModel> MaintenanceLogs => Set<MaintenanceLogModel>("maintenance_logs");

        // Additional shared collections
        public MongoDbSet<InquiryViewModel> InquiryViewModels => Set<InquiryViewModel>("inquiry_view_models");
        public MongoDbSet<ErrorViewModel> ErrorViewModels => Set<ErrorViewModel>("error_view_models");

        /// <summary>
        /// Entry wrapper for compatibility with EF Core's Entry() pattern.
        /// </summary>
        public EntryWrapper<T> Entry<T>(T entity) where T : class
            => new EntryWrapper<T>(entity);
    }
}
