using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.Models
{
    public class LeadModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int LeadId { get; set; }

        [Required(ErrorMessage = "Name is required")]
        public string? Name { get; set; }

        [RegularExpression(@"^[0-9]*$", ErrorMessage = "Contact number must contain only digits")]
        public string? Contact { get; set; }
        public string? Email { get; set; }
        public string? Stage { get; set; }
        public string? Status { get; set; }
        public string? GroupName { get; set; }
        public string? Source { get; set; }
        public string? PreferredLocation { get; set; }
        public string? Location { get; set; }
        public string? LeadType { get; set; }
        [BsonSerializer(typeof(FlexibleStringSerializer))]
        public string? Budget { get; set; }
        [BsonSerializer(typeof(FlexibleStringSerializer))]
        public string? Sqft { get; set; }
        public string? Facing { get; set; }
        public string? Type { get; set; }
        public string? PropertyType { get; set; }
        public string? BHK { get; set; }
        public string? LocationDistance { get; set; }
        public string? Requirement { get; set; }
        public int? ExecutiveId { get; set; }
        // Preserves partner-side assignment even when ExecutiveId is reassigned by admin handover.
        public int? PartnerAssignedAgentUserId { get; set; }
        public DateTime? FollowUpDate { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        public DateTime? ModifiedOn { get; set; }
        public string? Rating { get; set; }
        public string? Comments { get; set; }
        public int? ChannelPartnerId { get; set; } // For tracking lead ownership
        
        // Partner Handover System
        public string HandoverStatus { get; set; } = "Partner"; // Partner, ReadyToBook, HandedOver
        public DateTime? HandoverDate { get; set; }
        public int? AdminAssignedTo { get; set; }
        public bool IsReadyToBook { get; set; } = false;
        
        // P1-L4: UTM Tracking for marketing campaigns
        [StringLength(100)]
        public string? UtmSource { get; set; }
        [StringLength(100)]
        public string? UtmMedium { get; set; }
        [StringLength(100)]
        public string? UtmCampaign { get; set; }
        [StringLength(100)]
        public string? UtmTerm { get; set; }
        [StringLength(100)]
        public string? UtmContent { get; set; }
        
        // P0-D2: Optimistic Concurrency Control
        [Timestamp]
        public byte[]? RowVersion { get; set; }
    }
}
