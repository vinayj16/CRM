using CRM.Helpers;
using System.ComponentModel.DataAnnotations;

namespace CRM.Models
{
    public class TestimonialModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int TestimonialId { get; set; }

        [Required(ErrorMessage = "Name is required")]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(100)]
        public string? ClientName { get; set; }

        [StringLength(100)]
        public string? Designation { get; set; }

        [StringLength(100)]
        public string? Company { get; set; }

        [Required(ErrorMessage = "Tag/Designation is required")]
        [StringLength(100)]
        public string Tag { get; set; } = string.Empty;

        [Required(ErrorMessage = "Content is required")]
        [StringLength(500)]
        public string Content { get; set; } = string.Empty;

        [Required(ErrorMessage = "Rating is required")]
        [Range(1, 5)]
        public int Rating { get; set; }

        public string? ImageBase64 { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedOn { get; set; } = IndianTime.Now;
    }
}
