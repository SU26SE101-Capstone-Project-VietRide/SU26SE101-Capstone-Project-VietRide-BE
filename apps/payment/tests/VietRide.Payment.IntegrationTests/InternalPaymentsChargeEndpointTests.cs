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
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using StackExchange.Redis;
using VietRide.Payment.Application.Features.Internal.Payments.ChargePayment;

namespace VietRide.Payment.IntegrationTests;

public sealed class InternalPaymentsChargeEndpointTests
{
    private const string InternalJwtSecret = "test-secret-at-least-32-chars-long-xxxxx";

    [Fact]
    public async Task Charge_WithValidInternalJwt_ReturnsEnvelopeAndSendsCommandWithIdempotencyKey()
    {
        var mediator = new CapturingMediator();
        await using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();
        var request = CreateRequest(idempotencyKey: Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be((int)HttpStatusCode.OK);
        doc.RootElement.GetProperty("data").GetProperty("status").GetString().Should().Be("SUCCEEDED");
        doc.RootElement.GetProperty("data").GetProperty("paymentRedirectUrl").ValueKind.Should().Be(JsonValueKind.Null);
        doc.RootElement.GetProperty("meta").GetProperty("traceId").GetString().Should().NotBeNull();
        mediator.SendCount.Should().Be(1);
        var command = mediator.LastCommand.Should().BeOfType<ChargePaymentCommand>().Subject;
        command.IdempotencyKey.Should().Be(request.Headers.GetValues("Idempotency-Key").Single());
        command.ReferenceType.Should().Be("BOOKING");
        command.Method.Should().Be("WALLET");
        command.Amount.Should().Be(350_000);
    }

    [Fact]
    public async Task Charge_WithoutIdempotencyKey_ReturnsValidationEnvelopeBeforeSending()
    {
        var mediator = new CapturingMediator();
        await using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();
        var request = CreateRequest(idempotencyKey: null);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("IDEMPOTENCY_KEY_REQUIRED");
        mediator.SendCount.Should().Be(0);
    }

    private static WebApplicationFactory<Program> CreateFactory(CapturingMediator mediator)
        => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                Environment.SetEnvironmentVariable("INTERNAL_JWT_SECRET", InternalJwtSecret);
                builder.UseSetting("INTERNAL_JWT_SECRET", InternalJwtSecret);
                builder.UseSetting("ConnectionStrings:Default", "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres");
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

    private static HttpRequestMessage CreateRequest(string? idempotencyKey, bool includeAuth = true)
    {
        var bookingId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var userId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/payments/charge")
        {
            Content = JsonContent.Create(new
            {
                referenceType = "BOOKING",
                referenceId = bookingId,
                userId,
                amount = 350_000,
                method = "WALLET",
            }),
        };

        if (includeAuth)
        {
            request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt()}");
        }

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
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
            claims: [new Claim("sub", "gateway")],
            notBefore: DateTime.UtcNow.AddSeconds(-5),
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class CapturingMediator : IMediator
    {
        public object? LastCommand { get; private set; }
        public int SendCount { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastCommand = request;
            SendCount++;

            if (request is ChargePaymentCommand)
            {
                var response = new ChargePaymentResult(Guid.NewGuid(), "SUCCEEDED", null);
                return Task.FromResult((TResponse)(object)response);
            }

            throw new NotSupportedException(request.GetType().Name);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            LastCommand = request;
            SendCount++;
            return Task.FromResult<object?>(null);
        }

        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => EmptyAsync<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => EmptyAsync<object?>();

        private static async IAsyncEnumerable<TResponse> EmptyAsync<TResponse>()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
