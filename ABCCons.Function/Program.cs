using ABCCons.Function.Middleware;
using ABCCons.Function.Orchestration;
using ABCCons.Function.Plugins;
using ABCCons.Function.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
using StackExchange.Redis;

namespace ABCCons.Function
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var host = new HostBuilder()
                .ConfigureFunctionsWebApplication(workerApp =>
                {
                    // Register rate limiting middleware in the Functions Worker pipeline
                    workerApp.UseMiddleware<RateLimitingMiddleware>();
                })
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                          .AddEnvironmentVariables();
                })
                .ConfigureServices((context, services) =>
                {
                    var configuration = context.Configuration;

                    // 1. Scoped session context to share state with plugins during the request
                    services.AddScoped<SessionContext>();

                    // 2. Singleton Datasheet Service (handles loading and caching)
                    services.AddSingleton<IDatasheetService, DatasheetService>();

                    // 3. Conditional state store and feedback persistence: Redis vs In-Memory
                    var useRedisStr = configuration["FeatureManagement:UseRedis"] 
                                      ?? configuration["USE_REDIS"];
                    
                    bool useRedis = bool.TryParse(useRedisStr, out var result) && result;

                    if (useRedis)
                    {
                        var redisConnectionString = configuration["Redis:ConnectionString"] 
                                                   ?? "localhost:6379";
                        
                        try
                        {
                            var redis = ConnectionMultiplexer.Connect(redisConnectionString);
                            services.AddSingleton<IConnectionMultiplexer>(redis);
                            services.AddSingleton<IStateStore, RedisStateStore>();
                            services.AddSingleton<IFeedbackRepository, RedisFeedbackRepository>();
                        }
                        catch (Exception ex)
                        {
                            // In a production environment, we could fail startup or fallback.
                            // We fallback to InMemory for robustness and log it.
                            Console.WriteLine($"Error connecting to Redis: {ex.Message}. Falling back to In-Memory.");
                            services.AddSingleton<IStateStore, InMemoryStateStore>();
                            services.AddSingleton<IFeedbackRepository, InMemoryFeedbackRepository>();
                        }
                    }
                    else
                    {
                        services.AddSingleton<IStateStore, InMemoryStateStore>();
                        services.AddSingleton<IFeedbackRepository, InMemoryFeedbackRepository>();
                    }

                    // 4. Register Semantic Kernel with Azure OpenAI
                    services.AddTransient<Kernel>(sp =>
                    {
                        var endpoint = configuration["AzureOpenAI:Endpoint"];
                        var apiKey = configuration["AzureOpenAI:ApiKey"];
                        var chatDeploymentName = configuration["AzureOpenAI:ChatDeploymentName"];
                        var apiVersion = configuration["AzureOpenAI:API_Version"];

                        // Validation check for mandatory configurations
                        if (string.IsNullOrWhiteSpace(endpoint))
                        {
                            throw new InvalidOperationException("Configuration error: 'AzureOpenAI:Endpoint' is required but missing or empty.");
                        }
                        if (string.IsNullOrWhiteSpace(apiKey))
                        {
                            throw new InvalidOperationException("Configuration error: 'AzureOpenAI:ApiKey' is required but missing or empty.");
                        }
                        if (string.IsNullOrWhiteSpace(chatDeploymentName))
                        {
                            throw new InvalidOperationException("Configuration error: 'AzureOpenAI:ChatDeploymentName' is required but missing or empty.");
                        }

                        var kernelBuilder = Kernel.CreateBuilder();
                        
                        // We configure Chat Completion
                        kernelBuilder.AddAzureOpenAIChatCompletion(
                            deploymentName: chatDeploymentName,
                            endpoint: endpoint,
                            apiKey: apiKey,
                            apiVersion: string.IsNullOrWhiteSpace(apiVersion) ? null : apiVersion  
                        );

                        return kernelBuilder.Build();
                    });

                    // 5. Register Plugins, Prompt Provider, and Orchestrator
                    services.AddSingleton<IPromptProvider, FileSystemPromptProvider>();
                    services.AddScoped<DatasheetPlugin>();
                    services.AddScoped<FeedbackPlugin>();
                    services.AddScoped<AssistantOrchestrator>();
                })
                .Build();

            host.Run();
        }
    }
}

