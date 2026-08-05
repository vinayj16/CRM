using System.Text.Json.Serialization;

namespace CRM.Models.Chatbot
{
    public class ChatbotResponse
    {
        public int TenantId { get; set; } = 0;

        public string Response { get; set; } = string.Empty;
        public string Intent { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public string? PropertyQuery { get; set; }
        public bool ShouldTransferToAgent { get; set; }
        public int? AssignedAgentId { get; set; }
        public string? AssignedAgentName { get; set; }
        public bool ShouldCreateLead { get; set; }
        public int? GeneratedLeadId { get; set; }
    }

    public static class ChatIntents
    {
        public const string UNKNOWN = "unknown";
        public const string GENERAL_QUERY = "general_query";
        public const string PROPERTY_SEARCH = "property_search";
        public const string LEAD_GENERATION = "lead_generation";
        public const string APPOINTMENT_BOOKING = "appointment_booking";
        public const string PRICING_QUERY = "pricing_query";
        public const string AGENT_REQUEST = "agent_request";
        public const string AMENITY_QUERY = "amenity_query";
        public const string LOCATION_QUERY = "location_query";
    }

    public class IntentAnalytics
    {
        public int TenantId { get; set; } = 0;

        public string Intent { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percentage { get; set; }
    }

    public class ChatAnalytics
    {
        public int TenantId { get; set; } = 0;

        public int TotalSessions { get; set; }
        public int TotalMessages { get; set; }
        public int LeadsGenerated { get; set; }
        public List<IntentAnalytics> TopIntents { get; set; } = new();
        public double AverageMessagesPerSession { get; set; }
        public int AgentTransfers { get; set; }
    }

    public class OpenRouterResponse
    {
        public int TenantId { get; set; } = 0;

        public Choice[] choices { get; set; } = new Choice[0];
    }

    public class Choice
    {
        public int TenantId { get; set; } = 0;

        public ChatMessage message { get; set; } = new ChatMessage();
    }

    public class ChatMessage
    {
        public int TenantId { get; set; } = 0;

        public string role { get; set; } = string.Empty;
        public string content { get; set; } = string.Empty;
    }
}
