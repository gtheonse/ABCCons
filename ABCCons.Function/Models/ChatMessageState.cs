namespace ABCCons.Function.Models
{
    public class ChatMessageState
    {
        public string Role { get; set; } = string.Empty; // "user", "assistant", "system"
        public string Content { get; set; } = string.Empty;
    }
}
