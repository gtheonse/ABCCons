using ABCCons.Function.Models;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ABCCons.Function.Services
{
    public class InMemoryStateStore : IStateStore
    {
        private readonly ConcurrentDictionary<string, ConversationState> _store = new();

        public Task<ConversationState?> GetStateAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            _store.TryGetValue(sessionId, out var state);
            return Task.FromResult(state);
        }

        public Task SaveStateAsync(ConversationState state, CancellationToken cancellationToken = default)
        {
            _store[state.SessionId] = state;
            return Task.CompletedTask;
        }
    }
}
