using ABCCons.Function.Models;
using System.Collections.Concurrent;

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
