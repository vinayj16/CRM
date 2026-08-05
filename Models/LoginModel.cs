namespace CRM.Models
{
    public class LoginModel
    {
        public int TenantId { get; set; } = 0;

        public string Username { get; set; }
        public string Password { get; set; }
    }
}
