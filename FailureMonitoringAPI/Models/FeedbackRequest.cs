namespace FailureMonitoringAPI.Models
{
    public class FeedbackRequest
    {
        // Preferred: exact document id, returned by /Logs/search as KnowledgeId.
        public string Id { get; set; } = string.Empty;

        // Fallback for older clients that only have the question text.
        public string Question { get; set; } = string.Empty;

        public bool Helpful { get; set; }
    }
}