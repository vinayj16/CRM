namespace CRM.Models
{
    public class ChatMessageRequest
    {
        public int TenantId { get; set; } = 0;

        public string SessionId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class LeadCaptureRequest
    {
        public int TenantId { get; set; } = 0;

        public string SessionId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? Email { get; set; }
    }
}
