using ABCCons.Function.Models;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ABCCons.Function.Services
{
    public class RedisStateStore : IStateStore
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly TimeSpan _expiry;

        public RedisStateStore(IConnectionMultiplexer redis, IConfiguration configuration)
        {
            _redis = redis;

            // Make session TTL configurable, default to 2 hours
            int ttlHours = int.TryParse(configuration["Session:TtlHours"] ?? configuration["Session__TtlHours"], out var hours) ? hours : 2;
            _expiry = TimeSpan.FromHours(ttlHours);
        }

        public async Task<ConversationState?> GetStateAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            var db = _redis.GetDatabase();
            var json = await db.StringGetAsync($"session:{sessionId}");
            if (json.IsNullOrEmpty) return null;
            return JsonSerializer.Deserialize<ConversationState>(json.ToString());
        }

        public async Task SaveStateAsync(ConversationState state, CancellationToken cancellationToken = default)
        {
            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(state);
            await db.StringSetAsync($"session:{state.SessionId}", json, _expiry);
        }
    }
}
