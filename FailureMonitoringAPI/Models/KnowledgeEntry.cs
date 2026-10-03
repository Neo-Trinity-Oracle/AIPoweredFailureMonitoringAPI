namespace FailureMonitoringAPI.Models
{
    public class KnowledgeEntry
    {
        public string Id { get; set; } = string.Empty;

        public string Question { get; set; } = string.Empty;

        public string AiAnswer { get; set; } = string.Empty;

        public string RootCause { get; set; } = string.Empty;

        public string Resolution { get; set; } = string.Empty;

        public bool Helpful { get; set; }

        public int HelpfulCount { get; set; }

        public double Confidence { get; set; }

        public DateTime CreatedOn { get; set; }

        public string Category { get; set; } = string.Empty;

        public float[]? Embedding { get; set; }
    }
}