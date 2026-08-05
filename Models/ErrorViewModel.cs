namespace CRM.Models
{
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }

        public int? StatusCode { get; set; }

        public string Title { get; set; } = "Oops! Something went wrong.";

        public string UserMessage { get; set; } = "We will fix this as soon as possible.";

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}
