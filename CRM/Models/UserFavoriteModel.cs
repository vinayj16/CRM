using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class UserFavorite
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int FavoriteId { get; set; }
        public int UserId { get; set; }
        public string PageName { get; set; }
        public string PageUrl { get; set; }
        public string PageIcon { get; set; }
        public string PageColor { get; set; }
    }
}