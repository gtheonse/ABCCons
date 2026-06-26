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
  - `AssistantOrchestrator`: Classifies intent (`QA` vs. `Feedback`) and routes context to the target agent.
  - `DatasheetPlugin` / `FeedbackPlugin`: C# functions registered with Semantic Kernel for grounded data lookup and feedback storage.
  - `IDatasheetService`: Loads and parses local JSON files, querying properties/dimensions case-insensitively and caching products in memory.
  - `IStateStore` & `IFeedbackRepository`: Decoupled persistence interfaces supporting in-memory or Redis stores.
  - `SessionContext`: Scoped lifecycle container sharing session state across plugins and orchestrator during the request execution.

---

## Configuration & Environment Variables

Create or update the configuration in `ABCCons.Function/local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "AzureOpenAI__Endpoint": "https://<your-azure-openai-endpoint>.openai.azure.com/",
    "AzureOpenAI__ApiKey": "<your-azure-openai-api-key>",
    "AzureOpenAI__ChatDeploymentName": "<your-chat-deployment-name>",
    "ABCProducts__Path": "c:\\Users\\gonzalo_t\\source\\prepo\\ABCCons\\ABCproducts",
    "FeatureManagement__UseRedis": "false",
    "Redis__ConnectionString": "localhost:6379"
  }
}
```

### Config Matrix

| Key | Description | Default / Example |
| --- | --- | --- |
| `AzureOpenAI__Endpoint` | Azure OpenAI Resource Endpoint | `https://your-resource.openai.azure.com/` |
| `AzureOpenAI__ApiKey` | Azure OpenAI API Key credential | `your-api-key` |
| `AzureOpenAI__ChatDeploymentName` | Azure OpenAI deployment name (e.g. GPT-4o / GPT-4) | `gpt-4` |
| `ABCProducts__Path` | Path to the directory containing local product JSONs | `c:\Users\gonzalo_t\source\prepo\ABCCons\ABCproducts` |
| `FeatureManagement__UseRedis` | Enable Redis for state and feedback persistence | `false` |
| `Redis__ConnectionString` | Connection string for Redis Server | `localhost:6379` |

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
- **Tool-Only Sourcing**: The Q&A Agent is strictly instructed via its system prompt to answer using *only* facts retrieved through the `GetProductAttribute` tool.
- **Strict Abstention fallback**: If a product designation or attribute is missing, it returns exactly: `Sorry, I can’t find that information for '[designation/attribute]'. Please try another designation or attribute.`

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
  - *Input Validation*: Strict null/empty checking on the HTTP trigger payloads.
  - *Session ID Sanitization*: Trims input session IDs and automatically generates a secure random UUID when none is supplied.
  - *Configuration Management*: Azure OpenAI credentials and database connection strings are read securely from configuration/environment variables rather than being hardcoded.
- **Cloud Resiliency Patterns**:
  - *Graceful Fallback*: If `FeatureManagement:UseRedis` is enabled but the Redis server is unreachable, the system automatically falls back to in-memory state stores to avoid breaking availability.
