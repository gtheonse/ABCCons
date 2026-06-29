using ABCCons.Function.Models;
using System.Collections.Concurrent;

namespace ABCCons.Function.Services
{
    public class InMemoryStateStore : IStateStore
    {
        private readonly ConcurrentDictionary<string, ConversationState> _store = new();

        public Task<ConversationState?> GetStateAsync(string sessionId)
        {
            _store.TryGetValue(sessionId, out var state);
            return Task.FromResult(state);
        }

        public Task SaveStateAsync(ConversationState state)
        {
            _store[state.SessionId] = state;
            return Task.CompletedTask;
        }
    }
}
