using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.Models
{
    [BsonIgnoreExtraElements]
    public class SupportTicketModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int TicketId { get; set; }

        [Required]
        [StringLength(200)]
        public string Subject { get; set; } = string.Empty;

        [Required]
        public string Description { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string Category { get; set; } = "General"; // General, Technical, Billing, Feature Request, Bug Report

        [Required]
        [StringLength(30)]
        public string Priority { get; set; } = "Normal"; // Low, Normal, High, Urgent

        [Required]
        [StringLength(30)]
        public string Status { get; set; } = "Open"; // Open, InProgress, Waiting, Resolved, Closed

        public int? CreatedBy { get; set; }

        public int? CreatedByUserId { get; set; }

        [StringLength(100)]
        public string? CreatedByUsername { get; set; }

        [StringLength(100)]
        public string? CreatedByName { get; set; }

        [StringLength(100)]
        public string? CreatedByEmail { get; set; }

        public int? AssignedTo { get; set; }

        [StringLength(100)]
        public string? AssignedToName { get; set; }

        public DateTime CreatedOn { get; set; } = IndianTime.Now;

        public DateTime? ModifiedOn { get; set; }

        public DateTime? ResolvedOn { get; set; }

        public DateTime? ClosedOn { get; set; }

        [StringLength(500)]
        public string? Resolution { get; set; }

        [StringLength(500)]
        public string? AdminNotes { get; set; }

        public int? RelatedEntityId { get; set; }

        [StringLength(50)]
        public string? RelatedEntityType { get; set; } // Lead, Booking, Payment

        public int? ChannelPartnerId { get; set; }

        [StringLength(200)]
        public string? AttachmentPath { get; set; }

        public bool IsCustomerPortal { get; set; } = false;

        public string? PortalToken { get; set; } // For customer portal access

        // Ticket conversation / replies
        public List<TicketReplyModel> Replies { get; set; } = new List<TicketReplyModel>();
    }

    [BsonIgnoreExtraElements]
    public class TicketReplyModel
    {
        public int TenantId { get; set; } = 0;

        public int ReplyId { get; set; }

        [Required]
        public string Message { get; set; } = string.Empty;

        public int? UserId { get; set; }

        [StringLength(100)]
        public string? UserName { get; set; }

        [StringLength(100)]
        public string? UserEmail { get; set; }

        public bool IsStaff { get; set; } = false;

        public bool IsFromCustomer { get; set; } = false;

        [StringLength(200)]
        public string? AttachmentPath { get; set; }

        public DateTime CreatedOn { get; set; } = IndianTime.Now;
    }
}
