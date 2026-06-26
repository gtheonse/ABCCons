using System.Threading.Tasks;
using ABCCons.Function.Models;

namespace ABCCons.Function.Services
{
    public interface IStateStore
    {
        Task<ConversationState?> GetStateAsync(string sessionId);
        Task SaveStateAsync(ConversationState state);
    }
}
