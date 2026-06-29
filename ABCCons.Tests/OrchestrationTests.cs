using ABCCons.Function.Models;
using ABCCons.Function.Orchestration;
using ABCCons.Function.Plugins;
using ABCCons.Function.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Xunit;

namespace ABCCons.Tests
{
    public class OrchestrationTests
    {
        private class MockChatCompletion : IChatCompletionService
        {
            public string IntentResponse { get; set; } = "QA";
            public string AgentResponse { get; set; } = "The width of the 6205 bearing is 15 mm.";

            public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

            private bool _isFirstCall = true;

            public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
                ChatHistory chatHistory,
                PromptExecutionSettings? executionSettings = null,
                Kernel? kernel = null,
                System.Threading.CancellationToken cancellationToken = default)
            {
                // First call in ProcessMessageAsync is ClassifyIntentAsync
                if (_isFirstCall)
                {
                    _isFirstCall = false;
                    return new List<ChatMessageContent> { new ChatMessageContent(AuthorRole.Assistant, IntentResponse) };
                }

                // If the agent is executed, it can run function calling. In a mock, we return the final agent response.
                // To simulate function calling, if a kernel is provided and has a plugin, we can call it.
                if (kernel != null && IntentResponse == "QA")
                {
                    // Call plugin directly to simulate the LLM's function call updating the state
                    if (kernel.Plugins.TryGetPlugin("DatasheetPlugin", out var plugin) || 
                        kernel.Plugins.TryGetPlugin("ABCCons_Function_Plugins_DatasheetPlugin", out plugin))
                    {
                        if (plugin.TryGetFunction("GetProductDatasheet", out var function))
                        {
                            await function.InvokeAsync(kernel, new KernelArguments
                            {
                                ["designation"] = "6205",
                                ["attributeName"] = "Width"
                            });
                        }
                    }
                }
                else if (kernel != null && IntentResponse == "Feedback")
                {
                    if (kernel.Plugins.TryGetPlugin("FeedbackPlugin", out var plugin) ||
                        kernel.Plugins.TryGetPlugin("ABCCons_Function_Plugins_FeedbackPlugin", out plugin))
                    {
                        if (plugin.TryGetFunction("SaveFeedback", out var function))
                        {
                            await function.InvokeAsync(kernel, new KernelArguments
                            {
                                ["designation"] = "6205",
                                ["attribute"] = "width",
                                ["feedbackType"] = "Correction",
                                ["comment"] = "The width is wrong"
                            });
                        }
                    }
                }

                return new List<ChatMessageContent> { new ChatMessageContent(AuthorRole.Assistant, AgentResponse) };
            }

            public IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
                ChatHistory chatHistory,
                PromptExecutionSettings? executionSettings = null,
                Kernel? kernel = null,
                System.Threading.CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }
        }

        [Fact]
        public async Task ProcessMessageAsync_ShouldRouteToQAAndStoreState()
        {
            // Arrange
            var mockChat = new MockChatCompletion { IntentResponse = "QA", AgentResponse = "The width of the 6205 bearing is 15 mm." };
            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.Services.AddSingleton<IChatCompletionService>(mockChat);
            var kernel = kernelBuilder.Build();

            var datasheetService = new DatasheetService(
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "ABCProducts__Path", "../../../../ABCproducts" }
                }).Build(),
                NullLogger<DatasheetService>.Instance
            );

            var sessionContext = new SessionContext();
            var feedbackRepo = new InMemoryFeedbackRepository();

            var datasheetPlugin = new DatasheetPlugin(datasheetService, sessionContext);
            var feedbackPlugin = new FeedbackPlugin(feedbackRepo, sessionContext);

            var orchestrator = new AssistantOrchestrator(
                kernel,
                datasheetPlugin,
                feedbackPlugin,
                sessionContext,
                NullLogger<AssistantOrchestrator>.Instance
            );

            var state = new ConversationState { SessionId = "session-test" };

            // Act
            var response = await orchestrator.ProcessMessageAsync(state, "What is the width of 6205?");

            // Assert
            Assert.Equal("The width of the 6205 bearing is 15 mm.", response);
            Assert.Equal("6205", state.LastDesignation);
            Assert.Equal("Width", state.LastAttribute);
            Assert.Equal("The width of the 6205 bearing is 15 mm.", state.LastAnswer);
            Assert.Equal(2, state.History.Count); // User + Assistant messages
        }

        [Fact]
        public async Task ProcessMessageAsync_ShouldCapHistoryAtTenMessages()
        {
            // Arrange
            var mockChat = new MockChatCompletion { IntentResponse = "QA", AgentResponse = "Dummy answer." };
            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.Services.AddSingleton<IChatCompletionService>(mockChat);
            var kernel = kernelBuilder.Build();

            var datasheetService = new DatasheetService(
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "ABCProducts__Path", "../../../../ABCproducts" }
                }).Build(),
                NullLogger<DatasheetService>.Instance
            );

            var sessionContext = new SessionContext();
            var feedbackRepo = new InMemoryFeedbackRepository();

            var datasheetPlugin = new DatasheetPlugin(datasheetService, sessionContext);
            var feedbackPlugin = new FeedbackPlugin(feedbackRepo, sessionContext);

            var orchestrator = new AssistantOrchestrator(
                kernel,
                datasheetPlugin,
                feedbackPlugin,
                sessionContext,
                NullLogger<AssistantOrchestrator>.Instance
            );

            var state = new ConversationState { SessionId = "session-test" };

            // Act - Add 6 turns (12 messages)
            for (int i = 0; i < 6; i++)
            {
                // Reset mock for each turn
                mockChat = new MockChatCompletion { IntentResponse = "QA", AgentResponse = $"Answer {i}" };
                var turnKernelBuilder = Kernel.CreateBuilder();
                turnKernelBuilder.Services.AddSingleton<IChatCompletionService>(mockChat);
                var turnKernel = turnKernelBuilder.Build();

                var turnOrchestrator = new AssistantOrchestrator(
                    turnKernel,
                    datasheetPlugin,
                    feedbackPlugin,
                    sessionContext,
                    NullLogger<AssistantOrchestrator>.Instance
                );

                await turnOrchestrator.ProcessMessageAsync(state, $"Question {i}");
            }

            // Assert
            Assert.Equal(10, state.History.Count); // Capped at 10 messages
        }

        [Fact]
        public async Task ProcessMessageAsync_ShouldRouteToFeedbackAndSaveCorrectly()
        {
            // Arrange
            var mockChat = new MockChatCompletion { IntentResponse = "Feedback", AgentResponse = "Thanks—your feedback for 6205 / width has been saved." };
            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.Services.AddSingleton<IChatCompletionService>(mockChat);
            var kernel = kernelBuilder.Build();

            var datasheetService = new DatasheetService(
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "ABCProducts__Path", "../../../../ABCproducts" }
                }).Build(),
                NullLogger<DatasheetService>.Instance
            );

            var sessionContext = new SessionContext();
            var feedbackRepo = new InMemoryFeedbackRepository();

            var datasheetPlugin = new DatasheetPlugin(datasheetService, sessionContext);
            var feedbackPlugin = new FeedbackPlugin(feedbackRepo, sessionContext);

            var orchestrator = new AssistantOrchestrator(
                kernel,
                datasheetPlugin,
                feedbackPlugin,
                sessionContext,
                NullLogger<AssistantOrchestrator>.Instance
            );

            var state = new ConversationState 
            { 
                SessionId = "session-test",
                LastDesignation = "6205",
                LastAttribute = "width"
            };

            // Act
            var response = await orchestrator.ProcessMessageAsync(state, "That last width was wrong.");

            // Assert
            Assert.Equal("Thanks—your feedback for 6205 / width has been saved.", response);
            var feedbackList = await feedbackRepo.GetAllFeedbackAsync();
            Assert.Single(feedbackList);
            Assert.Equal("6205", feedbackList[0].Designation);
            Assert.Equal("width", feedbackList[0].Attribute);
            Assert.Equal("The width is wrong", feedbackList[0].Comment);
        }
    }
}
