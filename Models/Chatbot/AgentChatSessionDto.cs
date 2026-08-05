using System;

namespace CRM.Models.Chatbot
{
    public class AgentChatSessionDto
    {
        public int TenantId { get; set; } = 0;

        public string SessionId { get; set; } = string.Empty;
        public string LeadName { get; set; } = "Visitor";
        public DateTime StartedAt { get; set; }
        public string LastMessagePreview { get; set; } = string.Empty;
        public int UnreadCount { get; set; }
    }
}
