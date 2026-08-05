using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.Models
{
    [BsonIgnoreExtraElements]
    public class NotificationModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int NotificationId { get; set; }
        
        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;
        
        [Required]
        public string Message { get; set; } = string.Empty;
        
        [Required]
        [StringLength(50)]
        public string Type { get; set; } = string.Empty; // 'LeadAdded', 'LeadAssigned', 'QuotationCreated', 'InvoiceCreated', 'FollowUpReminder', etc.
        
        public bool IsRead { get; set; } = false;
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        public int? UserId { get; set; } // Null = All Admins, Specific ID = Specific User
        
        [StringLength(500)]
        public string? Link { get; set; } // Link to related page
        
        public int? RelatedEntityId { get; set; } // ID of related Lead, Quotation, etc.
        
        [StringLength(50)]
        public string? RelatedEntityType { get; set; } // 'Lead', 'Quotation', 'Invoice', 'FollowUp'
        
        [StringLength(20)]
        public string Priority { get; set; } = "Normal"; // 'Low', 'Normal', 'High', 'Urgent'
        
        public DateTime? ExpiresOn { get; set; }
        
        public DateTime? ReadOn { get; set; }
        
        // Navigation property (ignored by MongoDB serializer)
        [BsonIgnore]
        public virtual UserModel? User { get; set; }
    }
    
    // Notification types enum for consistency
    public static class NotificationType
    {
        public const string LeadAdded = "LeadAdded";
        public const string LeadAssigned = "LeadAssigned";
        public const string QuotationCreated = "QuotationCreated";
        public const string InvoiceCreated = "InvoiceCreated";
        public const string PaymentReceived = "PaymentReceived";
        public const string FollowUpReminder = "FollowUpReminder";
        public const string FollowUpDue = "FollowUpDue";
        public const string BookingCreated = "BookingCreated";
        public const string ChannelPartnerRequest = "ChannelPartnerRequest";
        public const string SystemAlert = "SystemAlert";
    }
}
