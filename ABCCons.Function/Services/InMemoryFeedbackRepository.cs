using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ABCCons.Function.Models;

namespace ABCCons.Function.Services
{
    public class InMemoryFeedbackRepository : IFeedbackRepository
    {
        private readonly ConcurrentBag<FeedbackItem> _feedbackList = new();

        public Task SaveFeedbackAsync(FeedbackItem item)
        {
            _feedbackList.Add(item);
            return Task.CompletedTask;
        }

        public Task<List<FeedbackItem>> GetAllFeedbackAsync()
        {
            return Task.FromResult(_feedbackList.ToList());
        }
    }
}
