using System.Collections.Generic;
using System.Threading.Tasks;
using ABCCons.Function.Models;

namespace ABCCons.Function.Services
{
    public interface IFeedbackRepository
    {
        Task SaveFeedbackAsync(FeedbackItem item);
        Task<List<FeedbackItem>> GetAllFeedbackAsync();
    }
}
