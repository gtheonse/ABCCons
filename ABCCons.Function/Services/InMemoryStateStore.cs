using System.Collections.Concurrent;
using System.Threading.Tasks;
using ABCCons.Function.Models;

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
