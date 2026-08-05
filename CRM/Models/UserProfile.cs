using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;

namespace CRM.Models
{
    [BsonIgnoreExtraElements]
    public class UserProfile
    {
        public int TenantId { get; set; } = 0;

        [Key]
        [BsonIgnore]
        public int Id { get; set; }
        public int UserId { get; set; }

        [Required]
        public string Username { get; set; }

        [Required, EmailAddress]
        public string Email { get; set; }

        public string? Location { get; set; }
        public string? Address { get; set; }

        public byte[]? ProfileImage { get; set; }
        public string? ProfileImagePath { get; set; }


        ///////////////adding
        ///
        public string? FirstName { get; set; }
        public string? LastName { get; set; }

        public string? PhoneNumber { get; set; }
        public string? Country { get; set; }
        public string? State { get; set; }
        public string? City { get; set; }
        public string? PostalCode { get; set; }

        public int? Age { get; set; }
        public string? DOB { get; set; }
        public string? Gender { get; set; }
        public string? Designation { get; set; }
        public string? EmployeeId { get; set; }
    }
}
