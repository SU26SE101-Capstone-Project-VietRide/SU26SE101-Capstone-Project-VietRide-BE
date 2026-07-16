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
using VietRide.Payment.Application.Features.Internal.Payments.BatchChargePayment;

namespace VietRide.Payment.IntegrationTests;

public sealed class InternalPaymentsBatchChargeEndpointTests
{
    private const string InternalJwtSecret = "test-secret-at-least-32-chars-long-xxxxx";

    [Fact]
    public async Task BatchCharge_WithValidInternalJwt_ReturnsRawDtoAndSendsCommandWithIdempotencyKey()
    {
        var mediator = new CapturingMediator();
        await using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();
        var request = CreateRequest(idempotencyKey: $"idem-{Guid.NewGuid():N}");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("success", out _).Should().BeFalse("/internal/* success responses are raw DTOs");
        doc.RootElement.GetProperty("payments").GetArrayLength().Should().Be(2);
        mediator.SendCount.Should().Be(1);
        var command = mediator.LastCommand.Should().BeOfType<BatchChargePaymentCommand>().Subject;
        command.IdempotencyKey.Should().Be(request.Headers.GetValues("Idempotency-Key").Single());
        command.Method.Should().Be("WALLET");
        command.Items.Should().OnlyContain(x => x.ReferenceType == "BOOKING");
    }

    [Fact]
    public async Task BatchCharge_ReplaySameKeyAndBody_ReturnsCachedRawDtoWithoutSendingAgain()
    {
        var mediator = new CapturingMediator();
        await using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();
        var idempotencyKey = $"idem-{Guid.NewGuid():N}";
        var first = CreateRequest(idempotencyKey);
        var second = CreateRequest(idempotencyKey);

        var firstResponse = await client.SendAsync(first);
        var secondResponse = await client.SendAsync(second);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await secondResponse.Content.ReadAsStringAsync()).Should().Be(await firstResponse.Content.ReadAsStringAsync());
        mediator.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task BatchCharge_ReusedKeyWithDifferentBody_ReturnsMismatchEnvelopeWithoutSendingAgain()
    {
        var mediator = new CapturingMediator();
        await using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();
        var idempotencyKey = $"idem-{Guid.NewGuid():N}";
        var first = CreateRequest(idempotencyKey, amount1: 80_000, amount2: 120_000);
        var second = CreateRequest(idempotencyKey, amount1: 80_000, amount2: 121_000);

        var firstResponse = await client.SendAsync(first);
        var secondResponse = await client.SendAsync(second);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await secondResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("IDEMPOTENCY_KEY_MISMATCH");
        mediator.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task BatchCharge_WithoutInternalJwt_ReturnsUnauthorizedEnvelope()
    {
        var mediator = new CapturingMediator();
        await using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();
        var request = CreateRequest(idempotencyKey: $"idem-{Guid.NewGuid():N}", includeAuth: false);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        mediator.SendCount.Should().Be(0);
    }

    [Fact]
    public async Task BatchCharge_WithoutIdempotencyKey_ReturnsValidationEnvelopeBeforeSending()
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
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
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

    private static HttpRequestMessage CreateRequest(
        string? idempotencyKey,
        long amount1 = 80_000,
        long amount2 = 120_000,
        bool includeAuth = true)
    {
        var bookingId1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var bookingId2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var userId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/payments/batch-charge")
        {
            Content = JsonContent.Create(new
            {
                userId,
                method = "WALLET",
                items = new[]
                {
                    new { referenceType = "BOOKING", referenceId = bookingId1, amount = amount1 },
                    new { referenceType = "BOOKING", referenceId = bookingId2, amount = amount2 },
                },
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

            if (request is BatchChargePaymentCommand command)
            {
                var response = new BatchChargePaymentResult(command.Items.Select(item => new BatchChargePaymentResult.Item(
                    Guid.NewGuid(),
                    item.ReferenceType,
                    item.ReferenceId,
                    "SUCCEEDED",
                    null)).ToList());

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
