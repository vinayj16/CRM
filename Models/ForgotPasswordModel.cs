namespace CRM.Models
{
    public class ForgotPasswordModel
    {
        public int TenantId { get; set; } = 0;

        public string Email { get; set; }
    }
}
