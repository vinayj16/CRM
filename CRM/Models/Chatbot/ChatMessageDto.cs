using System;

namespace CRM.Models.Chatbot
{
    public class ChatMessageDto
    {
        public int TenantId { get; set; } = 0;

        public string Sender { get; set; } = string.Empty; // "User", "Bot", "Agent"
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string ConversationId { get; set; } = string.Empty;
        public string? UserId { get; set; }
    }
}
