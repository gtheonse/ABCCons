using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using ABCCons.Function.Models;
using StackExchange.Redis;

namespace ABCCons.Function.Services
{
    public class RedisFeedbackRepository : IFeedbackRepository
    {
        private readonly IConnectionMultiplexer _redis;
        private const string FeedbackListKey = "assistant:feedback";

        public RedisFeedbackRepository(IConnectionMultiplexer redis)
        {
            _redis = redis;
        }

        public async Task SaveFeedbackAsync(FeedbackItem item)
        {
            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(item);
            await db.ListRightPushAsync(FeedbackListKey, json);
        }

        public async Task<List<FeedbackItem>> GetAllFeedbackAsync()
        {
            var db = _redis.GetDatabase();
            var values = await db.ListRangeAsync(FeedbackListKey);
            var list = new List<FeedbackItem>();
            foreach (var val in values)
            {
                if (!val.IsNullOrEmpty)
                {
                    var item = JsonSerializer.Deserialize<FeedbackItem>(val.ToString());
                    if (item != null)
                    {
                        list.Add(item);
                    }
                }
            }
            return list;
        }
    }
}
