# ABCCons - Mini Product Assistant

A C# Azure Function (HTTP Trigger) serving as a stateful product assistant built with **Microsoft Semantic Kernel (C#)**, **Azure OpenAI**, and **Redis**. 

The assistant reads bearing attributes from local JSON datasheets (located in the `ABCproducts` folder) using function calling, classifies intents, manages session states across turns, and handles user feedback.

---

## Technical Stack & Architecture

- **Runtime**: .NET 8.0 & C#
- **Execution Model**: Azure Functions Isolated Worker Model (standard for .NET 8/10)
- **AI Framework**: Microsoft Semantic Kernel 1.x
- **Services & Decoupled Architecture**:
  - `AssistantFunction`: Exposes a single HTTP POST endpoint (`/api/Assistant`). Validates inputs and manages session lifecycles.
  - `AssistantOrchestrator`: Classifies intent (`QA` vs. `Feedback`) and routes context to the target agent. Upgraded to use modern `FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()`.
  - `DatasheetPlugin` / `FeedbackPlugin`: C# functions registered with Semantic Kernel. `DatasheetPlugin` exposes `GetProductDatasheet` to return the full product JSON, delegating matching, synonyms, and structured array traversal to the LLM (Option A).
  - `IDatasheetService`: Loads and caches local JSON files, acting as a simplified key-to-JSON retriever.
  - `IPromptProvider` & `FileSystemPromptProvider`: Dynamic prompt provider that externalizes prompts as markdown files under `/Prompts` (`ClassificationPrompt.md`, `QaSystemPrompt.md`, `FeedbackSystemPrompt.md`).
  - `IStateStore` & `IFeedbackRepository`: Decoupled persistence interfaces supporting in-memory or Redis stores.
  - `SessionContext`: Scoped lifecycle container sharing session state (such as active designations/attributes) across plugins and orchestrator during the request execution.

---

## Configuration & Environment Variables

Create or update the configuration in `ABCCons.Function/local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "AzureOpenAI:Endpoint": "https://<your-azure-openai-endpoint>.openai.azure.com/",
    "AzureOpenAI:ApiKey": "<your-azure-openai-api-key>",
    "AzureOpenAI:ModelName": "<your-model-name>",
    "AzureOpenAI:ChatDeploymentName": "<your-chat-deployment-name>",
    "AzureOpenAI:API_Version": "2024-12-01-preview",
    "AzureOpenAI:MaxTokens": "500",
    "ABCProducts:Path": "c:\\Users\\gonzalo_t\\source\\prepo\\ABCCons\\ABCproducts",
    "FeatureManagement:UseRedis": "false",
    "Redis:ConnectionString": "localhost:6379",
    "Session:SigningKey": "your-secure-32-char-hmac-signing-key-here",
    "RateLimiting:PermitLimit": "10",
    "RateLimiting:WindowSeconds": "60",
    "RateLimiting:QueueLimit": "2"
  },
  "Host": {
    "LocalHttpPort": 7072,
    "functionTimeout": "00:10:00"
  }
}
```

### Config Matrix

| Key | Description | Default / Example |
| --- | --- | --- |
| `AzureOpenAI:Endpoint` | Azure OpenAI Resource Endpoint | `https://your-resource.openai.azure.com/` |
| `AzureOpenAI:ApiKey` | Azure OpenAI API Key credential | `your-api-key` |
| `AzureOpenAI:ModelName` | Azure OpenAI Model name | `gpt-5.2-chat` |
| `AzureOpenAI:ChatDeploymentName` | Azure OpenAI deployment name | `gpt-4` |
| `AzureOpenAI:API_Version` | Azure OpenAI API Version | `2024-12-01-preview` |
| `AzureOpenAI:MaxTokens` | Maximum completion token count for LLM responses | `500` |
| `ABCProducts:Path` | Path to the directory containing local product JSONs | `c:\Users\gonzalo_t\source\prepo\ABCCons\ABCproducts` |
| `FeatureManagement:UseRedis` | Enable Redis for state and feedback persistence | `false` |
| `Redis:ConnectionString` | Connection string for Redis Server | `localhost:6379` |
| `Session:SigningKey` | HMAC-SHA256 cryptographic signing key for session verification | `your-secure-signing-key` |
| `RateLimiting:PermitLimit` | Maximum requests allowed per time window per client IP | `10` |
| `RateLimiting:WindowSeconds` | Duration of the fixed rate-limiting window in seconds | `60` |
| `RateLimiting:QueueLimit` | Maximum requests queued when the limit is reached | `2` |

---

## How to Run & Test

### 1. Running Unit Tests
A comprehensive suite of unit tests covers datasheet lookup, state management, history limits, and orchestration routing. To execute:
```bash
dotnet test ABCCons.slnx
```

### 2. Running the Azure Function Locally
Make sure you have Azure Functions Core Tools installed.
```bash
cd ABCCons.Function
func start
```
The function will expose a single endpoint: `POST http://localhost:7072/api/Assistant`

### 3. Swagger & OpenAPI Documentation
The application is integrated with Azure Functions OpenAPI extensions (ASP.NET Core Web API style). When running locally, you can access:
- **Swagger UI**: [http://localhost:7072/api/swagger/ui](http://localhost:7072/api/swagger/ui)
- **OpenAPI JSON Spec**: [http://localhost:7072/api/swagger.json](http://localhost:7072/api/swagger.json)

### 4. Sample curls and Conversation Scenarios

#### Scenario A: Initial Question (QA Intent)
Ask about a bearing's attribute. The system will load the datasheet, resolve synonyms, and cache the bearing metadata.
```bash
curl -X POST http://localhost:7072/api/Assistant \
  -H "Content-Type: application/json" \
  -d '{"sessionId": "session-123", "message": "What is the width of 6205?"}'
```
**Response:**
```json
{
  "sessionId": "session-123",
  "response": "The width of the 6205 bearing is 15 mm."
}
```

#### Scenario B: Follow-up Question (QA Intent + State Retrieval)
Ask a follow-up question (e.g., about diameter) without specifying the bearing name. The orchestrator retrieves the prior state from the state store (Redis or In-Memory) and resolves the target bearing.
```bash
curl -X POST http://localhost:7072/api/Assistant \
  -H "Content-Type: application/json" \
  -d '{"sessionId": "session-123", "message": "what is its bore diameter?"}'
```
**Response:**
```json
{
  "sessionId": "session-123",
  "response": "The bore diameter of the 6205 bearing is 25 mm."
}
```

#### Scenario C: Submitting Feedback (Feedback Intent)
Provide feedback or corrections. The orchestrator classifies the message as `Feedback` and routes it to the Feedback agent. The agent logs the entry, resolving missing context (such as bearing or attribute name) from the session history.
```bash
curl -X POST http://localhost:7072/api/Assistant \
  -H "Content-Type: application/json" \
  -d '{"sessionId": "session-123", "message": "feedback: the width values look correct"}'
```
**Response:**
```json
{
  "sessionId": "session-123",
  "response": "Thanks—your feedback for 6205 / width has been saved."
}
```

---

## Caching, Hallucination Reduction, & State

### 1. Hallucination Controls (Groundedness)
- **Tool-Only Sourcing**: The Q&A Agent is strictly instructed via its system prompt to answer using *only* facts retrieved through the `GetProductDatasheet` tool (Option A).
- **Strict Abstention fallback**: If a product designation or attribute is missing, it returns exactly: `Sorry, I can’t find that information for '[designation]'. Please try another designation or attribute.`

### 2. State & Caching
- **State Preservation**: The `SessionContext` keeps track of the active user session. If a query is a follow-up (e.g. *"what about its diameter?"*), the context from the previous turn resolves the missing product context.
- **Token Control**: Conversation history in `ConversationState.History` is capped to a rolling size of **10 messages (5 turns)** to prevent token bloat and context window overflows.
- **Caching**: The `DatasheetService` parses JSON datasheets on startup and caches them in memory. Repeated attribute queries read from the cache instead of the filesystem.

---

## AI Validation Notes

Applied the following best practices during the design and code implementation phases:

- **SOLID Principles**:
  - *Single Responsibility*: Decoupled API parsing, business lookup logic, and state storage.
  - *Dependency Injection*: Leveraged .NET Dependency Injection to manage all lifetimes (`Singleton` for data, `Scoped` for state contexts, and `Transient` for kernel instances).
  - *Interface Segregation*: Swapped between `InMemory` and `Redis` stores via `IStateStore` and `IFeedbackRepository` abstractions.
- **OWASP Security Baselines**:
  - *Authorization Level*: Hardened API HTTP trigger endpoint from `AuthorizationLevel.Anonymous` to `AuthorizationLevel.Function` to require API key validation in production.
  - *Input Validation*: Added message length enforcement (max 2000 characters) and rejected non-printable/control characters to mitigate buffer/context overflow issues.
  - *Session Ownership Signature*: Introduced cryptographically signed session tokens (`uuid.signature` format using HMAC-SHA256) to verify session integrity and prevent session hijacking/enumeration.
  - *Prompt Injection Prevention (LLM01 / AI-1)*: Dynamic variables inside the classification prompt are wrapped with `<user_input>` delimiters, and the user's message has delimiters stripped beforehand to prevent tag-escaping injections.
  - *Anti-Disclosure Rules*: Configured LLM system prompts to reject jailbreak queries attempting to leak prompt configurations.
  - *Model DoS Mitigation*: Constrained output tokens with `MaxTokens` execution settings.
  - *Security Unit Testing*: Added a dedicated security unit test suite in [SecurityTests.cs](file:///c:/Users/gonzalo_t/source/prepo/ABCCons/ABCCons.Tests/SecurityTests.cs) verifying message lengths, control characters, signed session validation, and injection sanitization.
  - *Rate Limiting (DoS Mitigation)*: Implemented a fixed-window rate limiter as Azure Functions Worker middleware (`RateLimitingMiddleware`), partitioned by client IP. Returns HTTP 429 with `Retry-After` header when the limit is exceeded. Defaults: 10 requests per 60-second window, configurable via `RateLimiting:*` settings.
- **Cloud Resiliency Patterns**:
  - *Graceful Fallback*: If `FeatureManagement:UseRedis` is enabled but the Redis server is unreachable, the system automatically falls back to in-memory state stores to avoid breaking availability.

---

## Pending Security & Hardening Actions

The following security hardening actions are identified as pending to achieve full alignment with OWASP Top 10 and OWASP LLM security baselines:

1. **Secret Key Revocation & History Purge**: Plaintext keys previously committed must be rotated in Azure, and historical commits purged via `git-filter-repo`. Future deployments should use Azure Key Vault references (`@Microsoft.KeyVault(SecretUri=...)`).
2. **Stored Prompt Injection Mitigation (LLM01)**: Sanitize and filter conversation history retrieved from the state store before appending it back to the active `ChatHistory` on consecutive turns.
3. **Excessive Agency Safeguards (LLM06)**: Restrict the maximum automatic plugin execution loops (re-entrancy limits) permitted within Semantic Kernel to prevent infinite loop exploitation.
4. **Data Minimization in Tool Outputs (LLM02)**: Implement field-level projection filters in the datasheet plugin to strip internal-only attributes (e.g., margins, wholesale pricing, supplier details) before passing data to the LLM.
5. **Error/Path Leakage Sanitization**: Ensure filesystem paths are stripped from error messages in `FileSystemPromptProvider` before logging to prevent path disclosure.
6. **Redis Key Namespacing**: Add environment prefixes (e.g., `dev:session:*`) to state keys to prevent collision in shared-instance deployments.
7. **Supply Chain Security**: Enable NuGet lock files (`packages.lock.json`) and integrate vulnerability scanning (`dotnet list package --vulnerable`) into CI/CD workflows.


