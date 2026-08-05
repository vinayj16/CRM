using CRM.Helpers;
using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class BrandingModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int BrandingId { get; set; }
        
        // Header/Navbar
        public string? CompanyLogo { get; set; } // Base64 image
        public string? LogoDisplayStyle { get; set; } // "LogoOnly", "LogoWithName", "NameOnly"
        
        // Social Media Links
        public string? TwitterUrl { get; set; }
        public string? WhatsAppNumber { get; set; }
        public string? FacebookUrl { get; set; }
        public string? InstagramUrl { get; set; }
        public string? LinkedInUrl { get; set; }
        
        // About Us Section
        public string? AboutUsText { get; set; }
        public string? AboutUsImage { get; set; } // Base64 image
        
        // Footer
        public string? FooterLogo { get; set; } // Base64 image (smaller version)
        public string? CompanyInfo { get; set; }
        public string? TermsAndConditions { get; set; }
        public string? PrivacyPolicy { get; set; }
        public string? RefundPolicy { get; set; }
        
        // Metadata
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        public DateTime? ModifiedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public bool IsActive { get; set; } = true;
    }
}