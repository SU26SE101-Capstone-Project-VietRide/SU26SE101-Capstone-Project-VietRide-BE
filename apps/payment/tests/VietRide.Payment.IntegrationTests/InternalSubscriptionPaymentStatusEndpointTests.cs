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
using VietRide.Payment.Application.Features.Internal.Payments.GetSubscriptionPaymentStatuses;

namespace VietRide.Payment.IntegrationTests;

public sealed class InternalSubscriptionPaymentStatusEndpointTests
{
    private const string InternalJwtSecret = "test-secret-at-least-32-chars-long-xxxxx";

    [Fact]
    public async Task GetStatuses_WithValidInternalJwt_ReturnsRawList()
    {
        var upgradeAttemptId = Guid.NewGuid();
        var expected = new SubscriptionPaymentStatusDto(
            Guid.NewGuid(),
            upgradeAttemptId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SUCCEEDED",
            39_900_000,
            "VNPAY",
            "YEARLY",
            DateTimeOffset.Parse("2026-07-22T09:00:00Z"),
            DateTimeOffset.Parse("2027-07-22T09:00:00Z"),
            DateTimeOffset.Parse("2026-07-22T09:05:00Z"),
            DateTimeOffset.Parse("2026-07-22T09:15:00Z"));
        var mediator = new CapturingMediator(expected);
        await using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/internal/v1/payments/subscription-status?upgradeAttemptId={upgradeAttemptId:D}");
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt()}");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        document.RootElement.GetArrayLength().Should().Be(1);
        document.RootElement[0].GetProperty("paymentId").GetGuid().Should().Be(expected.PaymentId);
        document.RootElement[0].TryGetProperty("success", out _).Should().BeFalse();
        mediator.LastQuery.Should().NotBeNull();
        mediator.LastQuery!.UpgradeAttemptIds.Should().ContainSingle().Which.Should().Be(upgradeAttemptId);
    }

    private static WebApplicationFactory<Program> CreateFactory(CapturingMediator mediator)
        => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
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
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims: [new Claim("sub", "identity")],
            notBefore: DateTime.UtcNow.AddSeconds(-5),
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class CapturingMediator : IMediator
    {
        private readonly SubscriptionPaymentStatusDto _response;

        public CapturingMediator(SubscriptionPaymentStatusDto response)
        {
            _response = response;
        }

        public GetSubscriptionPaymentStatusesQuery? LastQuery { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            if (request is GetSubscriptionPaymentStatusesQuery query)
            {
                LastQuery = query;
                IReadOnlyList<SubscriptionPaymentStatusDto> result = [_response];
                return Task.FromResult((TResponse)(object)result);
            }

            throw new NotSupportedException(request.GetType().Name);
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
}
