using System.Collections.Generic;

namespace ABCCons.Function.Models
{
    public class ConversationState
    {
        public string SessionId { get; set; } = string.Empty;
        public string? LastDesignation { get; set; }
        public string? LastAttribute { get; set; }
        public string? LastAnswer { get; set; }
        public List<ChatMessageState> History { get; set; } = new();
    }
}
