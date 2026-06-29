namespace ABCCons.Function.Models
{
    public class FeedbackItem
    {
        public string Id { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Designation { get; set; } = string.Empty;
        public string Attribute { get; set; } = string.Empty;
        public string FeedbackType { get; set; } = string.Empty; // e.g. "Correction", "Helpfulness", "Comment"
        public string Comment { get; set; } = string.Empty;
    }
}
