using ABCCons.Function.Models;
using ABCCons.Function.Services;
using Xunit;

namespace ABCCons.Tests
{
    public class StateStoreTests
    {
        [Fact]
        public async Task InMemoryStateStore_ShouldStoreAndRetrieveState()
        {
            // Arrange
            IStateStore store = new InMemoryStateStore();
            var sessionId = Guid.NewGuid().ToString();
            var state = new ConversationState
            {
                SessionId = sessionId,
                LastDesignation = "6205",
                LastAttribute = "width",
                LastAnswer = "The width of the 6205 bearing is 15 mm."
            };
            state.History.Add(new ChatMessageState { Role = "user", Content = "What is the width of 6205?" });
            state.History.Add(new ChatMessageState { Role = "assistant", Content = state.LastAnswer });

            // Act
            await store.SaveStateAsync(state);
            var retrieved = await store.GetStateAsync(sessionId);

            // Assert
            Assert.NotNull(retrieved);
            Assert.Equal(sessionId, retrieved.SessionId);
            Assert.Equal("6205", retrieved.LastDesignation);
            Assert.Equal("width", retrieved.LastAttribute);
            Assert.Equal(2, retrieved.History.Count);
            Assert.Equal("user", retrieved.History[0].Role);
            Assert.Equal("What is the width of 6205?", retrieved.History[0].Content);
        }

        [Fact]
        public async Task InMemoryFeedbackRepository_ShouldStoreAndRetrieveFeedback()
        {
            // Arrange
            IFeedbackRepository repo = new InMemoryFeedbackRepository();
            var item = new FeedbackItem
            {
                Id = Guid.NewGuid().ToString(),
                SessionId = "session-1",
                Timestamp = DateTime.UtcNow,
                Designation = "6205",
                Attribute = "width",
                FeedbackType = "Correction",
                Comment = "The width is wrong"
            };

            // Act
            await repo.SaveFeedbackAsync(item);
            var feedbackList = await repo.GetAllFeedbackAsync();

            // Assert
            Assert.Contains(feedbackList, f => f.Id == item.Id && f.Comment == "The width is wrong");
        }
    }
}
