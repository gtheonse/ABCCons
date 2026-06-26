using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using ABCCons.Function.Models;
using ABCCons.Function.Orchestration;
using ABCCons.Function.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace ABCCons.Function
{
    public class AssistantFunction
    {
        private readonly AssistantOrchestrator _orchestrator;
        private readonly IStateStore _stateStore;
        private readonly ILogger<AssistantFunction> _logger;

        public AssistantFunction(
            AssistantOrchestrator orchestrator,
            IStateStore stateStore,
            ILogger<AssistantFunction> logger)
        {
            _orchestrator = orchestrator;
            _stateStore = stateStore;
            _logger = logger;
        }

        [Function("Assistant")]
        [OpenApiOperation(operationId: "RunAssistant", tags: new[] { "Assistant" }, Summary = "Query bearing attributes or provide feedback", Description = "Routes questions to Q&A agent and feedback to Feedback agent based on classification.")]
        [OpenApiSecurity("function_key", SecuritySchemeType.ApiKey, Name = "code", In = OpenApiSecurityLocationType.Query)]
        [OpenApiRequestBody("application/json", typeof(AssistantRequest), Description = "The request payload containing message content and optional Session ID.")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(AssistantResponse), Description = "Concise agent response and current Session ID.")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "text/plain", bodyType: typeof(string), Description = "The request body was empty or invalid.")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.InternalServerError, contentType: "text/plain", bodyType: typeof(string), Description = "An internal error occurred during processing.")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
        {
            _logger.LogInformation("Processing Assistant HTTP request (ASP.NET Core Web API style).");

            // 1. Read Request Payload
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            
            // Input validation
            if (string.IsNullOrWhiteSpace(requestBody))
            {
                return new BadRequestObjectResult("Request body cannot be empty.");
            }

            // Standard JSON deserialization
            AssistantRequest? assistantRequest = null;
            try
            {
                assistantRequest = JsonSerializer.Deserialize<AssistantRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException)
            {
                return new BadRequestObjectResult("Invalid JSON payload.");
            }

            if (assistantRequest == null || string.IsNullOrWhiteSpace(assistantRequest.Message))
            {
                return new BadRequestObjectResult("The 'message' property is required and cannot be empty.");
            }

            // 2. Resolve or generate Session ID
            string? sessionId = assistantRequest.SessionId;
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                sessionId = Guid.NewGuid().ToString();
                _logger.LogInformation("Generated new SessionId: {SessionId}", sessionId);
            }
            else
            {
                // Basic sanitization
                sessionId = sessionId.Trim();
            }

            // 3. Retrieve or create State
            var state = await _stateStore.GetStateAsync(sessionId);
            if (state == null)
            {
                state = new ConversationState { SessionId = sessionId };
                _logger.LogInformation("Created new ConversationState for session: {SessionId}", sessionId);
            }

            // 4. Process through Orchestrator
            string answer;
            try
            {
                answer = await _orchestrator.ProcessMessageAsync(state, assistantRequest.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message for session {SessionId}", sessionId);
                return new ObjectResult("An error occurred while processing your request.")
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError
                };
            }

            // 5. Save updated State
            await _stateStore.SaveStateAsync(state);

            // 6. Build and return Response
            var assistantResponse = new AssistantResponse
            {
                SessionId = sessionId,
                Response = answer
            };

            return new OkObjectResult(assistantResponse);
        }
    }
}
