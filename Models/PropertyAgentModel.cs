using CRM.Helpers;
using System;
using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class PropertyAgentModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int PropertyAgentId { get; set; }
        
        [Required]
        public int PropertyId { get; set; }
        
        [Required]
        public int AgentUserId { get; set; }
        
        public DateTime AssignedOn { get; set; } = IndianTime.Now;
        
        public int? AssignedBy { get; set; }
        
        public bool IsActive { get; set; } = true;
    }
}
