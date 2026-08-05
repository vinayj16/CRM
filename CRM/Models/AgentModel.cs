using CRM.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.Models
{
    [BsonIgnoreExtraElements]
    public class AgentModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int AgentId { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        [RegularExpression(@"^[0-9]*$", ErrorMessage = "Phone number must contain only digits")]
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? AgentType { get; set; } // Salary, Hybrid, Commission
        public decimal? Salary { get; set; }
        public string? CommissionRules { get; set; }
        public string? Documents { get; set; }
        public string? Status { get; set; } = "Pending";
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        public int? ApprovedBy { get; set; }
        public DateTime? ApprovedOn { get; set; }
        public int? ChannelPartnerId { get; set; } // For linking agents to channel partners
        
        // Navigation property for documents (ignored by MongoDB serializer)
        [BsonIgnore]
        public virtual ICollection<AgentDocumentModel>? AgentDocuments { get; set; }
    }
}