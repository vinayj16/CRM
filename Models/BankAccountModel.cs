using CRM.Helpers;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CRM.Models
{
    [BsonIgnoreExtraElements]
    public class BankAccountModel
    {
        public int TenantId { get; set; } = 0;

        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        [BsonIgnoreIfNull]
        public ObjectId? MongoId { get; set; }

        [BsonIgnore]
        [Key]
        [Column("AccountId")]
        public int Id
        {
            get => AccountId;
            set => AccountId = value;
        }

        [BsonElement("AccountId")]
        public int AccountId { get; set; }
        
        [Required(ErrorMessage = "Please enter account holder name")]
        [StringLength(100)]
        public string AccountHolderName { get; set; } = string.Empty;
        
        [Required(ErrorMessage = "Please enter account number")]
        [StringLength(30)]
        [RegularExpression("^[0-9]+$", ErrorMessage = "Account number must contain digits only")]
        public string AccountNumber { get; set; } = string.Empty;
        
        [Required(ErrorMessage = "Please enter bank name")]
        [StringLength(100)]
        public string BankName { get; set; } = string.Empty;
        
        [Required(ErrorMessage = "Please enter IFSC code")]
        [StringLength(11, MinimumLength = 11, ErrorMessage = "IFSC code must be exactly 11 characters")]
        [RegularExpression("^[A-Za-z0-9]{11}$", ErrorMessage = "IFSC code must be 11 alpha-numeric characters")]
        public string IFSCCode { get; set; } = string.Empty;
        
        public string? BranchName { get; set; }
        
        public string? AccountType { get; set; } // Savings, Current, etc.
        
        public bool IsActive { get; set; } = false;
        
        public DateTime CreatedOn { get; set; } = IndianTime.Now;
        
        public DateTime? UpdatedOn { get; set; }
    }
}