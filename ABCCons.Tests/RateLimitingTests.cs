using ABCCons.Function.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Xunit;

namespace ABCCons.Tests
{
    public class RateLimitingTests
    {
        private class MockInvocationFeatures : IInvocationFeatures
        {
            private readonly Dictionary<Type, object> _features = new();

            public object? this[Type key]
            {
                get => _features.TryGetValue(key, out var val) ? val : null;
                set { if (value != null) _features[key] = value; }
            }

            public T? Get<T>()
            {
                return _features.TryGetValue(typeof(T), out var val) ? (T)val : default;
            }

            public void Set<T>(T? instance)
            {
                if (instance != null) _features[typeof(T)] = instance;
            }

            public IEnumerator<KeyValuePair<Type, object>> GetEnumerator() => _features.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => _features.GetEnumerator();
        }

        private class MockFunctionContext : FunctionContext
        {
            public override string InvocationId => Guid.NewGuid().ToString();
            public override string FunctionId => Guid.NewGuid().ToString();
            public override TraceContext TraceContext => null!;
            public override BindingContext BindingContext => null!;
            public override IServiceProvider InstanceServices { get; set; } = null!;
            public override FunctionDefinition FunctionDefinition => null!;
            public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();
            public override IInvocationFeatures Features { get; } = new MockInvocationFeatures();
            public override RetryContext RetryContext => null!;
        }

        /// <summary>
        /// Test subclass that overrides GetHttpContext to bypass the SDK's
        /// internal IHttpCoordinator infrastructure (which requires a full
        /// ASP.NET Core integration pipeline to populate the HttpContext).
        /// </summary>
        private class TestableRateLimitingMiddleware : RateLimitingMiddleware
        {
            private readonly Func<FunctionContext, HttpContext?> _httpContextResolver;

            public TestableRateLimitingMiddleware(
                IConfiguration configuration,
                Func<FunctionContext, HttpContext?> httpContextResolver)
                : base(configuration, NullLogger<RateLimitingMiddleware>.Instance)
            {
                _httpContextResolver = httpContextResolver;
            }

            protected override HttpContext? GetHttpContext(FunctionContext context)
            {
                return _httpContextResolver(context);
            }
        }

        private IConfiguration CreateConfiguration(int permitLimit, int windowSeconds)
        {
            var settings = new Dictionary<string, string?>
            {
                { "RateLimiting:PermitLimit", permitLimit.ToString() },
                { "RateLimiting:WindowSeconds", windowSeconds.ToString() },
                { "RateLimiting:QueueLimit", "0" } // No queue to fail fast in tests
            };

            return new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();
        }

        [Fact]
        public async Task Invoke_ShouldPassThrough_UnderRateLimit()
        {
            // Arrange
            var config = CreateConfiguration(5, 60);
            var httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");

            var middleware = new TestableRateLimitingMiddleware(
                config,
                _ => httpContext);

            var context = new MockFunctionContext();

            bool nextCalled = false;
            FunctionExecutionDelegate next = (ctx) =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            // Act
            await middleware.Invoke(context, next);

            // Assert
            Assert.True(nextCalled);
            Assert.Equal(200, httpContext.Response.StatusCode);
        }

        [Fact]
        public async Task Invoke_ShouldBlock_WhenRateLimitExceeded()
        {
            // Arrange — max 2 requests per window, no queue
            var config = CreateConfiguration(2, 60);

            // Each invocation gets its own FunctionContext + HttpContext,
            // but all share the same client IP so the rate limiter groups them.
            HttpContext CreateHttpContextForIp(string ip)
            {
                var hc = new DefaultHttpContext();
                hc.Connection.RemoteIpAddress = IPAddress.Parse(ip);
                hc.Response.Body = new MemoryStream();
                return hc;
            }

            // Map: each FunctionContext -> its HttpContext
            var contextMap = new Dictionary<FunctionContext, HttpContext>();

            var middleware = new TestableRateLimitingMiddleware(
                config,
                ctx => contextMap.TryGetValue(ctx, out var hc) ? hc : null);

            int nextCalls = 0;
            FunctionExecutionDelegate next = (ctx) =>
            {
                nextCalls++;
                return Task.CompletedTask;
            };

            // Request 1: should pass
            var ctx1 = new MockFunctionContext();
            var hc1 = CreateHttpContextForIp("192.168.1.1");
            contextMap[ctx1] = hc1;
            await middleware.Invoke(ctx1, next);
            Assert.Equal(1, nextCalls);
            Assert.Equal(200, hc1.Response.StatusCode);

            // Request 2: should pass
            var ctx2 = new MockFunctionContext();
            var hc2 = CreateHttpContextForIp("192.168.1.1");
            contextMap[ctx2] = hc2;
            await middleware.Invoke(ctx2, next);
            Assert.Equal(2, nextCalls);
            Assert.Equal(200, hc2.Response.StatusCode);

            // Request 3: should be blocked (429)
            var ctx3 = new MockFunctionContext();
            var hc3 = CreateHttpContextForIp("192.168.1.1");
            contextMap[ctx3] = hc3;
            await middleware.Invoke(ctx3, next);
            Assert.Equal(2, nextCalls); // next was NOT called for the 3rd request
            Assert.Equal(429, hc3.Response.StatusCode);
        }

        [Fact]
        public async Task Invoke_ShouldBypassRateLimiter_WhenNonHttpTrigger()
        {
            // Arrange
            var config = CreateConfiguration(5, 60);

            // Return null HttpContext to simulate non-HTTP trigger
            var middleware = new TestableRateLimitingMiddleware(
                config,
                _ => null);

            var context = new MockFunctionContext();

            bool nextCalled = false;
            FunctionExecutionDelegate next = (ctx) =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            // Act
            await middleware.Invoke(context, next);

            // Assert
            Assert.True(nextCalled);
        }

        [Fact]
        public async Task Invoke_ShouldRateLimitPerIp()
        {
            // Arrange — max 1 request per window
            var config = CreateConfiguration(1, 60);

            var contextMap = new Dictionary<FunctionContext, HttpContext>();
            var middleware = new TestableRateLimitingMiddleware(
                config,
                ctx => contextMap.TryGetValue(ctx, out var hc) ? hc : null);

            int nextCalls = 0;
            FunctionExecutionDelegate next = (ctx) =>
            {
                nextCalls++;
                return Task.CompletedTask;
            };

            // Request from IP A — should pass
            var ctx1 = new MockFunctionContext();
            var hc1 = new DefaultHttpContext();
            hc1.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
            contextMap[ctx1] = hc1;
            await middleware.Invoke(ctx1, next);
            Assert.Equal(1, nextCalls);

            // Request from IP B — should pass (different partition)
            var ctx2 = new MockFunctionContext();
            var hc2 = new DefaultHttpContext();
            hc2.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.2");
            contextMap[ctx2] = hc2;
            await middleware.Invoke(ctx2, next);
            Assert.Equal(2, nextCalls);

            // Second request from IP A — should be blocked
            var ctx3 = new MockFunctionContext();
            var hc3 = new DefaultHttpContext();
            hc3.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
            hc3.Response.Body = new MemoryStream();
            contextMap[ctx3] = hc3;
            await middleware.Invoke(ctx3, next);
            Assert.Equal(2, nextCalls); // still 2, 3rd was blocked
            Assert.Equal(429, hc3.Response.StatusCode);

            // Second request from IP B — should also be blocked
            var ctx4 = new MockFunctionContext();
            var hc4 = new DefaultHttpContext();
            hc4.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.2");
            hc4.Response.Body = new MemoryStream();
            contextMap[ctx4] = hc4;
            await middleware.Invoke(ctx4, next);
            Assert.Equal(2, nextCalls); // still 2
            Assert.Equal(429, hc4.Response.StatusCode);
        }
    }
}
