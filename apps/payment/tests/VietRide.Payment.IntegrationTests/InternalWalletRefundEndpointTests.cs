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
using VietRide.Payment.Application.Exceptions;
using VietRide.Payment.Application.Features.Internal.Wallets.RefundToWallet;

namespace VietRide.Payment.IntegrationTests;

public sealed class InternalWalletRefundEndpointTests
{
    private const string InternalJwtSecret = "test-secret-at-least-32-chars-long-xxxxx";

    [Fact]
    public async Task Refund_WithValidInternalJwt_ReturnsEnvelopeAndSendsCommandWithIdempotencyKey()
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
        doc.RootElement.GetProperty("data").GetProperty("walletTransactionId").GetGuid().Should().NotBeEmpty();
        doc.RootElement.GetProperty("data").GetProperty("balanceAfter").GetInt64().Should().Be(1_175_000);
        mediator.SendCount.Should().Be(1);
        var command = mediator.LastCommand.Should().BeOfType<RefundToWalletCommand>().Subject;
        command.IdempotencyKey.Should().Be(request.Headers.GetValues("Idempotency-Key").Single());
        command.ReferenceType.Should().Be("BOOKING_REFUND");
        command.Amount.Should().Be(175_000);
    }

    [Fact]
    public async Task Refund_WithoutIdempotencyKey_ReturnsValidationEnvelopeBeforeSending()
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

    [Fact]
    public async Task Refund_WhenPlatformWalletUnderflows_ReturnsGlobalCodedExceptionEnvelope()
    {
        var mediator = new CapturingMediator
        {
            ExceptionToThrow = new PlatformWalletInsufficientBalanceException("Platform wallet balance is insufficient."),
        };
        await using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();
        var request = CreateRequest(idempotencyKey: Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("PLATFORM_WALLET_INSUFFICIENT_BALANCE");
        mediator.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task Refund_ReplaySameKeyAndBody_ReturnsCachedEnvelopeWithoutSendingAgain()
    {
        var mediator = new CapturingMediator();
        await using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();
        var idempotencyKey = Guid.NewGuid().ToString();
        var first = CreateRequest(idempotencyKey);
        var second = CreateRequest(idempotencyKey);

        var firstResponse = await client.SendAsync(first);
        var secondResponse = await client.SendAsync(second);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        var replayBody = await secondResponse.Content.ReadAsStringAsync();
        replayBody.Should().Be(firstBody);
        using var document = JsonDocument.Parse(firstBody);
        document.RootElement.GetProperty("meta").GetProperty("timestamp").GetString()
            .Should().MatchRegex(@"\.\d{7}Z$");
        mediator.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task Refund_ReusedKeyWithDifferentBody_ReturnsMismatchEnvelopeWithoutSendingAgain()
    {
        var mediator = new CapturingMediator();
        await using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();
        var idempotencyKey = Guid.NewGuid().ToString();
        var first = CreateRequest(idempotencyKey, amount: 175_000);
        var second = CreateRequest(idempotencyKey, amount: 176_000);

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
        long amount = 175_000,
        bool includeAuth = true)
    {
        var bookingId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var userId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/wallet/refund")
        {
            Content = JsonContent.Create(new
            {
                userId,
                amount,
                referenceType = "BOOKING_REFUND",
                referenceId = bookingId,
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
        public Exception? ExceptionToThrow { get; init; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastCommand = request;
            SendCount++;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            if (request is RefundToWalletCommand)
            {
                var response = new RefundToWalletResult(Guid.NewGuid(), 1_175_000);
                return Task.FromResult((TResponse)(object)response);
            }

            throw new NotSupportedException(request.GetType().Name);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken)
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
