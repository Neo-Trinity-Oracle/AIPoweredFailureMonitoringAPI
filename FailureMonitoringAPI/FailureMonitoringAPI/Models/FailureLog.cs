namespace FailureMonitoringAPI.Models
{

    public class FailureLog
    {
        public int WireId { get; set; }
        public DateTime Created { get; set; }
        public int Institution { get; set; }
        public string Status { get; set; } = string.Empty;
        public string ErrorCategory { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty;
        public float Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string? FullText { get; set; }
        public float[]? Embedding { get; set; }
    }
}
