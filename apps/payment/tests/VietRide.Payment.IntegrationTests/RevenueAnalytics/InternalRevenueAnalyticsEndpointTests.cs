using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using VietRide.Payment.Application.Features.Internal.Revenue.RevenueSummary;

namespace VietRide.Payment.IntegrationTests.RevenueAnalytics;

public sealed class InternalRevenueAnalyticsEndpointTests
{
    private const string InternalJwtSecret = "revenue-test-secret-at-least-32-chars-long";

    [Fact]
    public async Task InternalJwt_ReturnsRawAdminAndOperatorSummariesWithoutGatewayEnvelope()
    {
        var operatorId = Guid.NewGuid();
        var mediator = new CapturingMediator(operatorId);
        await using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();

        using var adminResponse = await SendInternalAsync(
            client,
            "/internal/v1/revenue/admin-summary?from=2026-07-01&to=2026-07-31");
        using var operatorResponse = await SendInternalAsync(
            client,
            $"/internal/v1/revenue/operators/{operatorId:D}/summary?from=2026-07-01&to=2026-07-31");

        adminResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        operatorResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var adminDocument = JsonDocument.Parse(await adminResponse.Content.ReadAsStringAsync());
        using var operatorDocument = JsonDocument.Parse(await operatorResponse.Content.ReadAsStringAsync());
        adminDocument.RootElement.TryGetProperty("success", out _).Should().BeFalse();
        adminDocument.RootElement.GetProperty("totalProjectRevenueVnd").GetInt64().Should().Be(1_000);
        adminDocument.RootElement.GetProperty("paidToOperatorsVnd").GetInt64().Should().Be(400);
        operatorDocument.RootElement.TryGetProperty("success", out _).Should().BeFalse();
        operatorDocument.RootElement.GetProperty("netParcelRevenueVnd").GetInt64().Should().Be(300);
        operatorDocument.RootElement.GetProperty("grossParcelRevenueVnd").GetInt64().Should().Be(350);
        operatorDocument.RootElement.GetProperty("parcelRefundsVnd").GetInt64().Should().Be(-50);
        mediator.AdminQuery.Should().Be(new GetInternalAdminRevenueSummaryQuery("2026-07-01", "2026-07-31"));
        mediator.OperatorQuery.Should().Be(new GetInternalOperatorRevenueSummaryQuery(
            operatorId,
            "2026-07-01",
            "2026-07-31"));
    }

    [Fact]
    public async Task MissingInternalJwt_IsUnauthorized()
    {
        await using var factory = CreateFactory(new CapturingMediator(Guid.NewGuid()));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/internal/v1/revenue/admin-summary?from=2026-07-01&to=2026-07-31");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<HttpResponseMessage> SendInternalAsync(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt()}");
        return await client.SendAsync(request);
    }

    private static WebApplicationFactory<Program> CreateFactory(CapturingMediator mediator)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            Environment.SetEnvironmentVariable("INTERNAL_JWT_SECRET", InternalJwtSecret);
            builder.UseSetting("INTERNAL_JWT_SECRET", InternalJwtSecret);
            builder.UseSetting(
                "ConnectionStrings:Default",
                "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres");
            builder.UseEnvironment("Testing");
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

    private static string CreateInternalJwt()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(InternalJwtSecret));
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims: [new Claim("sub", "booking")],
            notBefore: DateTime.UtcNow.AddSeconds(-5),
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class CapturingMediator : IMediator
    {
        private readonly Guid operatorId;

        public CapturingMediator(Guid operatorId)
        {
            this.operatorId = operatorId;
        }

        public GetInternalAdminRevenueSummaryQuery? AdminQuery { get; private set; }
        public GetInternalOperatorRevenueSummaryQuery? OperatorQuery { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            if (request is GetInternalAdminRevenueSummaryQuery admin)
            {
                AdminQuery = admin;
                var response = new InternalAdminRevenueSummaryResult(
                    Period(), 1_000, 700, 500, 200, 300, 400, DateTime.UtcNow);
                return Task.FromResult((TResponse)(object)response);
            }

            if (request is GetInternalOperatorRevenueSummaryQuery operatorQuery)
            {
                OperatorQuery = operatorQuery;
                var response = new InternalOperatorRevenueSummaryResult(
                    Period(), operatorId, 800, 500, 300, 350, -50, DateTime.UtcNow);
                return Task.FromResult((TResponse)(object)response);
            }

            throw new NotSupportedException(request.GetType().Name);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException(request.GetType().Name);
        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => Empty<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => Empty<object?>();

        private static InternalRevenuePeriod Period()
            => new(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), "Asia/Ho_Chi_Minh");

        private static async IAsyncEnumerable<T> Empty<T>()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
