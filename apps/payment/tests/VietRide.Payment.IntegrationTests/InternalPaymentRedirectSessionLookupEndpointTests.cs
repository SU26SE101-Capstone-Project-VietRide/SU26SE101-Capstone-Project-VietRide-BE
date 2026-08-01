using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using VietRide.Payment.Application.Features.Internal.Payments.LookupRedirectSessions;

namespace VietRide.Payment.IntegrationTests;

public sealed class InternalPaymentRedirectSessionLookupEndpointTests
{
    private const string InternalJwtSecret = "test-secret-at-least-32-chars-long-xxxxx";
    private const string SignedUrl =
        "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?vnp_TxnRef=private&vnp_SecureHash=secret";

    [Fact]
    public async Task Lookup_WithInternalJwtAndNoIdempotencyHeader_ReturnsRawNoStoreBodyWithoutLoggingUrl()
    {
        var referenceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var result = new LookupRedirectSessionsResult(
            Guid.NewGuid(),
            "BOOKING",
            referenceId,
            350_000,
            DateTimeOffset.Parse("2026-08-01T09:00:00Z"),
            SignedUrl);
        var mediator = new CapturingMediator([result]);
        var logs = new CapturingLoggerProvider();
        await using var factory = CreateFactory(mediator, logs);
        using var client = factory.CreateClient();
        using var request = CreateRequest(userId, referenceId, includeAuth: true);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        document.RootElement[0].GetProperty("amount").GetInt64().Should().Be(350_000);
        document.RootElement[0].GetProperty("paymentRedirectUrl").GetString().Should().Be(SignedUrl);
        document.RootElement[0].TryGetProperty("success", out _).Should().BeFalse();
        mediator.LastQuery.Should().NotBeNull();
        mediator.LastQuery!.UserId.Should().Be(userId);
        mediator.LastQuery.References.Should().ContainSingle()
            .Which.Should().Be(new LookupRedirectSessionsQuery.Reference("BOOKING", referenceId));
        logs.Messages.Should().NotContain(message => message.Contains(SignedUrl, StringComparison.Ordinal));
        logs.Messages.Should().NotContain(message => message.Contains("vnp_SecureHash", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Lookup_WithoutInternalJwt_ReturnsUnauthorizedAndDoesNotDispatch()
    {
        var mediator = new CapturingMediator([]);
        var logs = new CapturingLoggerProvider();
        await using var factory = CreateFactory(mediator, logs);
        using var client = factory.CreateClient();
        using var request = CreateRequest(Guid.NewGuid(), Guid.NewGuid(), includeAuth: false);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        mediator.LastQuery.Should().BeNull();
    }

    private static WebApplicationFactory<Program> CreateFactory(
        CapturingMediator mediator,
        CapturingLoggerProvider logs)
        => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                Environment.SetEnvironmentVariable("INTERNAL_JWT_SECRET", InternalJwtSecret);
                builder.UseSetting("INTERNAL_JWT_SECRET", InternalJwtSecret);
                builder.UseSetting(
                    "ConnectionStrings:Default",
                    "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres");
                builder.UseEnvironment("Testing");
                builder.ConfigureLogging(logging => logging.AddProvider(logs));
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IMediator>();
                    services.RemoveAll<ISender>();
                    services.RemoveAll<IPublisher>();
                    services.RemoveAll<IConnectionMultiplexer>();
                    services.AddSingleton<IMediator>(mediator);
                    services.AddSingleton<ISender>(mediator);
                    services.AddSingleton<IPublisher>(mediator);
                    services.AddSingleton<IConnectionMultiplexer>(InMemoryIdempotencyRedis.Create());
                });
            });

    private static HttpRequestMessage CreateRequest(Guid userId, Guid referenceId, bool includeAuth)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/payments/redirect-sessions/lookup")
        {
            Content = JsonContent.Create(new
            {
                userId,
                references = new[] { new { referenceType = "BOOKING", referenceId } },
            }),
        };
        if (includeAuth)
        {
            request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt()}");
        }

        return request;
    }

    private static string CreateInternalJwt()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(InternalJwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims: [new Claim("sub", "booking")],
            notBefore: DateTime.UtcNow.AddSeconds(-5),
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class CapturingMediator(IReadOnlyList<LookupRedirectSessionsResult> response) : IMediator
    {
        public LookupRedirectSessionsQuery? LastQuery { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            LastQuery = request.Should().BeOfType<LookupRedirectSessionsQuery>().Subject;
            return Task.FromResult((TResponse)(object)response);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException(request.GetType().Name);

        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => EmptyAsync<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default)
            => EmptyAsync<object?>();

        private static async IAsyncEnumerable<TResponse> EmptyAsync<TResponse>()
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);
        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentBag<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
                => messages.Add(formatter(state, exception));
        }
    }
}
