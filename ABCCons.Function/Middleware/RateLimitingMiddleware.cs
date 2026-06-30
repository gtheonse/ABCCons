using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Threading.RateLimiting;


namespace ABCCons.Function.Middleware
{
    /// <summary>
    /// Azure Functions Worker middleware that enforces a fixed-window rate limit
    /// partitioned by client IP address. Returns HTTP 429 when the limit is exceeded.
    /// </summary>
    public class RateLimitingMiddleware : IFunctionsWorkerMiddleware
    {
        private readonly PartitionedRateLimiter<string> _limiter;
        private readonly ILogger<RateLimitingMiddleware> _logger;

        public RateLimitingMiddleware(IConfiguration configuration, ILogger<RateLimitingMiddleware> logger)
        {
            _logger = logger;

            int permitLimit = int.TryParse(configuration["RateLimiting:PermitLimit"], out var pl) ? pl : 10;
            int windowSeconds = int.TryParse(configuration["RateLimiting:WindowSeconds"], out var ws) ? ws : 60;
            int queueLimit = int.TryParse(configuration["RateLimiting:QueueLimit"], out var ql) ? ql : 2;

            _limiter = PartitionedRateLimiter.Create<string, string>(partitionKey =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: partitionKey,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromSeconds(windowSeconds),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = queueLimit
                    }));

            _logger.LogInformation(
                "Rate limiting configured: {PermitLimit} requests per {WindowSeconds}s window, queue limit {QueueLimit}.",
                permitLimit, windowSeconds, queueLimit);
        }

        /// <summary>
        /// Resolves the HttpContext from the FunctionContext. Override in tests
        /// to provide a mock HttpContext without requiring the full ASP.NET Core
        /// integration pipeline.
        /// </summary>
        protected virtual HttpContext? GetHttpContext(FunctionContext context)
        {
            return context.GetHttpContext();
        }

        public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            // Access the underlying ASP.NET Core HttpContext
            var httpContext = GetHttpContext(context);
            if (httpContext == null)
            {
                // Non-HTTP trigger (e.g., timer, queue) — skip rate limiting
                await next(context);
                return;
            }

            // Partition by client IP (prefer X-Forwarded-For for load-balanced environments)
            var clientIp = httpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedValues)
                ? forwardedValues.FirstOrDefault()?.Split(',').FirstOrDefault()?.Trim() ?? "unknown"
                : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            using var lease = await _limiter.AcquireAsync(clientIp, 1, context.CancellationToken);

            if (lease.IsAcquired)
            {
                await next(context);
            }
            else
            {
                _logger.LogWarning("Rate limit exceeded for client IP: {ClientIp}", clientIp);

                httpContext.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                httpContext.Response.Headers["Retry-After"] = "60";
                await httpContext.Response.WriteAsync("Rate limit exceeded. Please try again later.");

                // Short-circuit: do not call next(context)
            }
        }
    }
}
