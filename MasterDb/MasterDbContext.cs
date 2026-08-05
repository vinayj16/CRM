using CRM.Services;
using CRM.MasterDb.Models;
using CRM.Models;

namespace CRM.MasterDb
{
    /// <summary>
    /// MongoDB-backed replacement for the old EF Core MasterDbContext.
    /// Provides MongoDbSet for master/SaaS entity types.
    /// </summary>
    public class MasterDbContext
    {
        private readonly AppDbContext _appDb;

        public MasterDbContext(MongoDbContext mongo, AppDbContext appDb)
        {
            _appDb = appDb;
        }

        /// <summary>
        /// SaveChanges - no-op in MongoDB.
        /// </summary>
        public int SaveChanges() => 0;

        /// <summary>
        /// SaveChangesAsync - no-op in MongoDB.
        /// </summary>
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        /// <summary>
        /// Database facade for transaction support (MongoDB no-op).
        /// </summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public DbFacade Database => new DbFacade();

        // Master/SaaS entity collections - delegate to AppDbContext
        public MongoDbSet<TenantModel> Tenants => _appDb.Tenants;
        public MongoDbSet<SaasSubscriptionPlanModel> SaasPlans => _appDb.SaasPlans;
        public MongoDbSet<SuperAdminModel> SuperAdmins => _appDb.SuperAdmins;
        public MongoDbSet<SaasPaymentTransactionModel> SaasPaymentTransactions => _appDb.SaasPaymentTransactions;
        public MongoDbSet<TenantSubscriptionModel> TenantSubscriptions => _appDb.TenantSubscriptions;
        public MongoDbSet<EmailDirectoryModel> EmailDirectory => _appDb.EmailDirectory;
        public MongoDbSet<InquiryFormModel> InquiryForms => _appDb.InquiryForms;
        public MongoDbSet<InquiryModel> Inquiries => _appDb.Inquiries;
        public MongoDbSet<SaasBrandingModel> SaasBrandings => _appDb.SaasBrandings;
        public MongoDbSet<SaasSettingsModel> SaasSettings => _appDb.SaasSettings;
        public MongoDbSet<SaasPaymentConfigModel> SaasPaymentConfigs => _appDb.SaasPaymentConfigs;
        public MongoDbSet<PageModel> Pages => _appDb.Pages;
        public MongoDbSet<InquiryViewModel> InquiryViewModels => _appDb.InquiryViewModels;
        public MongoDbSet<ErrorViewModel> ErrorViewModels => _appDb.ErrorViewModels;

        // Singular aliases
        public MongoDbSet<SaasSettingsModel> SaasSetting => SaasSettings;
        public MongoDbSet<SaasPaymentConfigModel> SaasPaymentConfig => SaasPaymentConfigs;
        public MongoDbSet<SaasBrandingModel> SaasBranding => SaasBrandings;
        public MongoDbSet<ReferralEarningModel> ReferralEarnings => _appDb.ReferralEarnings;
    }
}
