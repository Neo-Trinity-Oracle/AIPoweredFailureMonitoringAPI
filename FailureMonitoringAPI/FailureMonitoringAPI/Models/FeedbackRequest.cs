namespace FailureMonitoringAPI.Models
{
    public class FeedbackRequest
    {
        public string Question { get; set; } = string.Empty;

        public bool Helpful { get; set; }
    }
}