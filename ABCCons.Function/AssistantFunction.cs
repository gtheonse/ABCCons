using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;
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
        private readonly string _signingKey;

        public AssistantFunction(
            AssistantOrchestrator orchestrator,
            IStateStore stateStore,
            Microsoft.Extensions.Configuration.IConfiguration configuration,
            ILogger<AssistantFunction> logger)
        {
            _orchestrator = orchestrator;
            _stateStore = stateStore;
            _logger = logger;
            _signingKey = configuration["Session:SigningKey"] 
                          ?? configuration["Session__SigningKey"] 
                          ?? "DefaultSecureFallbackSigningKey_PleaseChangeInProduction_12345!";
        }

        private string SignSessionId(string sessionId)
        {
            using (var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(_signingKey)))
            {
                var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sessionId));
                return $"{sessionId}.{Convert.ToBase64String(hash)}";
            }
        }

        private bool TryVerifySessionId(string signedSessionId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? rawSessionId)
        {
            rawSessionId = null;
            if (string.IsNullOrWhiteSpace(signedSessionId)) return false;

            int dotIndex = signedSessionId.LastIndexOf('.');
            if (dotIndex <= 0 || dotIndex == signedSessionId.Length - 1) return false;

            var uuid = signedSessionId.Substring(0, dotIndex);
            var signature = signedSessionId.Substring(dotIndex + 1);

            if (!Guid.TryParse(uuid, out _)) return false;

            using (var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(_signingKey)))
            {
                var expectedHash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(uuid));
                var expectedSignature = Convert.ToBase64String(expectedHash);

                if (string.Equals(signature, expectedSignature, StringComparison.Ordinal))
                {
                    rawSessionId = uuid;
                    return true;
                }
            }

            return false;
        }

        [Function("Assistant")]
        [OpenApiOperation(operationId: "RunAssistant", tags: new[] { "Assistant" }, Summary = "Query bearing attributes or provide feedback", Description = "Routes questions to Q&A agent and feedback to Feedback agent based on classification.")]
        [OpenApiSecurity("function_key", SecuritySchemeType.ApiKey, Name = "code", In = OpenApiSecurityLocationType.Query)]
        [OpenApiRequestBody("application/json", typeof(AssistantRequest), Description = "The request payload containing message content and optional Session ID.")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(AssistantResponse), Description = "Concise agent response and current Session ID.")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "text/plain", bodyType: typeof(string), Description = "The request body was empty or invalid.")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.InternalServerError, contentType: "text/plain", bodyType: typeof(string), Description = "An internal error occurred during processing.")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Processing Assistant HTTP request (ASP.NET Core Web API style).");

            // 1. Read Request Payload
            string requestBody;
            using (var reader = new StreamReader(req.Body))
            {
                requestBody = await reader.ReadToEndAsync(cancellationToken);
            }
            
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

            // Enforce max message length
            if (assistantRequest.Message.Length > 2000)
            {
                return new BadRequestObjectResult("Message length exceeds the maximum limit of 2000 characters.");
            }

            // Enforce control characters check
            foreach (char c in assistantRequest.Message)
            {
                if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t')
                {
                    return new BadRequestObjectResult("Message contains invalid control characters.");
                }
            }

            // 2. Resolve or generate Session ID
            string? sessionId = assistantRequest.SessionId;
            string rawSessionId;

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                rawSessionId = Guid.NewGuid().ToString();
                sessionId = SignSessionId(rawSessionId);
                _logger.LogInformation("Generated new signed SessionId: {SessionId}", sessionId);
            }
            else
            {
                sessionId = sessionId.Trim();
                if (!TryVerifySessionId(sessionId, out string? verifiedRawId))
                {
                    _logger.LogWarning("Invalid session signature. Rejecting session.");
                    return new BadRequestObjectResult("Invalid session ID or signature.");
                }
                rawSessionId = verifiedRawId;
            }

            // 3. Retrieve or create State
            var state = await _stateStore.GetStateAsync(rawSessionId, cancellationToken);
            if (state == null)
            {
                state = new ConversationState { SessionId = rawSessionId };
                _logger.LogInformation("Created new ConversationState for session: {SessionId}", rawSessionId);
            }

            // 4. Process through Orchestrator
            string answer;
            try
            {
                answer = await _orchestrator.ProcessMessageAsync(state, assistantRequest.Message, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message for session {SessionId}", rawSessionId);
                return new ObjectResult("An error occurred while processing your request.")
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError
                };
            }

            // 5. Save updated State
            await _stateStore.SaveStateAsync(state, cancellationToken);

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
