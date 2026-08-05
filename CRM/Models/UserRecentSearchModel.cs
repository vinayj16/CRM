using System;
using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class UserRecentSearch
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int SearchId { get; set; }
        public int UserId { get; set; }
        public string SearchTerm { get; set; }
        public DateTime SearchedAt { get; set; }
    }
}