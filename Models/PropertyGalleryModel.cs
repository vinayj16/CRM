using CRM.Helpers;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    /// <summary>
    /// P2-PR1: Property Gallery for multiple images per property
    /// </summary>
    public class PropertyGalleryModel
    {
        public int TenantId { get; set; } = 0;

        [Key]
        public int GalleryId { get; set; }
        
        [Required]
        public int PropertyId { get; set; }
        
        [Required]
        [StringLength(200)]
        public string ImageTitle { get; set; } = string.Empty;
        
        [StringLength(500)]
        public string? Description { get; set; }
        
        [Required]
        public byte[] ImageData { get; set; } = Array.Empty<byte>();
        
        [Required]
        [StringLength(100)]
        public string ContentType { get; set; } = "image/jpeg";
        
        public long FileSize { get; set; }
        
        [StringLength(50)]
        public string ImageCategory { get; set; } = "General"; // General, Exterior, Interior, Amenities, Floor Plan, Location
        
        public int DisplayOrder { get; set; } = 0;
        
        public bool IsPrimary { get; set; } = false;
        
        public DateTime UploadedOn { get; set; } = IndianTime.Now;
        
        public int? UploadedBy { get; set; }
        
        public virtual PropertyModel? Property { get; set; }
    }
}
