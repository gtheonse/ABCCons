using System.Threading;
using System.Threading.Tasks;
using ABCCons.Function.Models;

namespace ABCCons.Function.Services
{
    public interface IStateStore
    {
        Task<ConversationState?> GetStateAsync(string sessionId, CancellationToken cancellationToken = default);
        Task SaveStateAsync(ConversationState state, CancellationToken cancellationToken = default);
    }
}
