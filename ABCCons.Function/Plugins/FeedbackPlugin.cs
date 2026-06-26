using System;
using System.ComponentModel;
using System.Threading.Tasks;
using ABCCons.Function.Models;
using ABCCons.Function.Services;
using Microsoft.SemanticKernel;

namespace ABCCons.Function.Plugins
{
    public class FeedbackPlugin
    {
        private readonly IFeedbackRepository _feedbackRepository;
        private readonly SessionContext _sessionContext;

        public FeedbackPlugin(IFeedbackRepository feedbackRepository, SessionContext sessionContext)
        {
            _feedbackRepository = feedbackRepository;
            _sessionContext = sessionContext;
        }

        [KernelFunction]
        [Description("Persists user feedback (such as corrections, comments, or helpfulness ratings) regarding a product attribute.")]
        public async Task<string> SaveFeedback(
            [Description("The product designation. If not explicitly mentioned in the feedback, pass null or empty string.")] string? designation,
            [Description("The product attribute. If not explicitly mentioned in the feedback, pass null or empty string.")] string? attribute,
            [Description("The type of feedback: 'Correction', 'Helpfulness', 'Comment', etc.")] string feedbackType,
            [Description("The actual feedback text or correction details.")] string comment)
        {
            // Resolve designation and attribute from SessionContext state if missing
            string resolvedDesignation = string.IsNullOrWhiteSpace(designation)
                ? (_sessionContext.State?.LastDesignation ?? "General")
                : designation.Trim();

            string resolvedAttribute = string.IsNullOrWhiteSpace(attribute)
                ? (_sessionContext.State?.LastAttribute ?? "General")
                : attribute.Trim();

            var feedbackItem = new FeedbackItem
            {
                Id = Guid.NewGuid().ToString(),
                SessionId = _sessionContext.State?.SessionId ?? "Unknown",
                Timestamp = DateTime.UtcNow,
                Designation = resolvedDesignation,
                Attribute = resolvedAttribute,
                FeedbackType = feedbackType,
                Comment = comment
            };

            await _feedbackRepository.SaveFeedbackAsync(feedbackItem);

            // Return receipt confirmation formatted strictly per example
            return $"Thanks—your feedback for {resolvedDesignation} / {resolvedAttribute} has been saved.";
        }
    }
}
