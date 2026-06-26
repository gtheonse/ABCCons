using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ABCCons.Function;
using ABCCons.Function.Models;
using ABCCons.Function.Orchestration;
using ABCCons.Function.Plugins;
using ABCCons.Function.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Xunit;

namespace ABCCons.Tests
{
    public class SecurityTests
    {
        private class MockChatCompletion : IChatCompletionService
        {
            public string IntentResponse { get; set; } = "QA";
            public string AgentResponse { get; set; } = "The width of the 6205 bearing is 15 mm.";
            public List<string> InvokedPrompts { get; } = new List<string>();

            public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

            private bool _isFirstCall = true;

            public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
                ChatHistory chatHistory,
                PromptExecutionSettings? executionSettings = null,
                Kernel? kernel = null,
                System.Threading.CancellationToken cancellationToken = default)
            {
                // Track user messages passed to inspect sanitization
                foreach (var message in chatHistory)
                {
                    if (message.Content != null)
                    {
                        InvokedPrompts.Add(message.Content);
                    }
                }

                if (_isFirstCall)
                {
                    _isFirstCall = false;
                    return new List<ChatMessageContent> { new ChatMessageContent(AuthorRole.Assistant, IntentResponse) };
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

        private (AssistantFunction Function, MockChatCompletion MockChat) SetupFunction(string signingKey = "TestSigningKey12345!")
        {
            var inMemorySettings = new Dictionary<string, string?>
            {
                { "ABCProducts__Path", "../../../../ABCproducts" },
                { "Session:SigningKey", signingKey }
            };

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            var mockChat = new MockChatCompletion();
            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.Services.AddSingleton<IChatCompletionService>(mockChat);
            var kernel = kernelBuilder.Build();

            var datasheetService = new DatasheetService(configuration, NullLogger<DatasheetService>.Instance);
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

            var stateStore = new InMemoryStateStore();

            var function = new AssistantFunction(
                orchestrator,
                stateStore,
                configuration,
                NullLogger<AssistantFunction>.Instance
            );

            return (function, mockChat);
        }

        private HttpRequest CreateMockRequest(AssistantRequest payload)
        {
            var context = new DefaultHttpContext();
            var request = context.Request;
            var json = JsonSerializer.Serialize(payload);
            request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
            return request;
        }

        [Fact]
        public async Task Run_ShouldReturnBadRequest_WhenMessageExceedsMaxLength()
        {
            // Arrange
            var (function, _) = SetupFunction();
            var longMessage = new string('A', 2001);
            var payload = new AssistantRequest { Message = longMessage };
            var request = CreateMockRequest(payload);

            // Act
            var result = await function.Run(request);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("Message length exceeds the maximum limit", badRequestResult.Value?.ToString());
        }

        [Fact]
        public async Task Run_ShouldReturnBadRequest_WhenMessageContainsControlCharacters()
        {
            // Arrange
            var (function, _) = SetupFunction();
            var payload = new AssistantRequest { Message = "Hello\u0000World" };
            var request = CreateMockRequest(payload);

            // Act
            var result = await function.Run(request);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("Message contains invalid control characters", badRequestResult.Value?.ToString());
        }

        [Fact]
        public async Task Run_ShouldGenerateSignedSessionId_WhenSessionIdIsEmpty()
        {
            // Arrange
            var (function, _) = SetupFunction();
            var payload = new AssistantRequest { Message = "What is the width of 6205?" };
            var request = CreateMockRequest(payload);

            // Act
            var result = await function.Run(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<AssistantResponse>(okResult.Value);
            
            Assert.NotNull(response.SessionId);
            Assert.Contains(".", response.SessionId);

            // Verify the generated ID structure: guid.signature
            var parts = response.SessionId.Split('.');
            Assert.Equal(2, parts.Length);
            Assert.True(Guid.TryParse(parts[0], out _));
        }

        [Fact]
        public async Task Run_ShouldAcceptRequest_WhenSessionIdHasValidSignature()
        {
            // Arrange
            var (function, _) = SetupFunction();
            
            // First run to get a valid signed SessionId
            var payload1 = new AssistantRequest { Message = "First query" };
            var request1 = CreateMockRequest(payload1);
            var result1 = await function.Run(request1);
            var okResult1 = Assert.IsType<OkObjectResult>(result1);
            var response1 = Assert.IsType<AssistantResponse>(okResult1.Value);
            var validSessionId = response1.SessionId;

            // Second run with the valid SessionId
            var payload2 = new AssistantRequest { Message = "Second query", SessionId = validSessionId };
            var request2 = CreateMockRequest(payload2);

            // Act
            var result2 = await function.Run(request2);

            // Assert
            Assert.IsType<OkObjectResult>(result2);
        }

        [Fact]
        public async Task Run_ShouldRejectSession_WhenSessionIdHasInvalidSignature()
        {
            // Arrange
            var (function, _) = SetupFunction();
            var invalidSessionId = Guid.NewGuid().ToString() + ".InvalidSignatureStringHere=";
            var payload = new AssistantRequest { Message = "Test query", SessionId = invalidSessionId };
            var request = CreateMockRequest(payload);

            // Act
            var result = await function.Run(request);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("Invalid session ID or signature", badRequestResult.Value?.ToString());
        }

        [Fact]
        public async Task Run_ShouldRejectSession_WhenSessionIdIsUnsignedGuid()
        {
            // Arrange
            var (function, _) = SetupFunction();
            var rawGuid = Guid.NewGuid().ToString();
            var payload = new AssistantRequest { Message = "Test query", SessionId = rawGuid };
            var request = CreateMockRequest(payload);

            // Act
            var result = await function.Run(request);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("Invalid session ID or signature", badRequestResult.Value?.ToString());
        }
    }
}
