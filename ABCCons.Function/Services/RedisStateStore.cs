using ABCCons.Function.Models;
using StackExchange.Redis;
using System.Text.Json;

namespace ABCCons.Function.Services
{
    public class RedisStateStore : IStateStore
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly TimeSpan _expiry = TimeSpan.FromDays(1);

        public RedisStateStore(IConnectionMultiplexer redis)
        {
            _redis = redis;
        }

        public async Task<ConversationState?> GetStateAsync(string sessionId)
        {
            var db = _redis.GetDatabase();
            var json = await db.StringGetAsync($"session:{sessionId}");
            if (json.IsNullOrEmpty) return null;
            return JsonSerializer.Deserialize<ConversationState>(json.ToString());
        }

        public async Task SaveStateAsync(ConversationState state)
        {
            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(state);
            await db.StringSetAsync($"session:{state.SessionId}", json, _expiry);
        }
    }
}
