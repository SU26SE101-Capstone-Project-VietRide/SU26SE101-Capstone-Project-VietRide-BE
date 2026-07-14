using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Features.Invoices;
using VietRide.Payment.Application.Features.Management;

namespace VietRide.Payment.IntegrationTests;

public sealed class InvoiceEndpointsTests
{
    private static readonly Guid InvoiceId = Guid.Parse("38000000-0000-0000-0000-000000000701");
    private static readonly Guid OperatorId = Guid.Parse("38000000-0000-0000-0000-000000000702");
    private static readonly Guid UserId = Guid.Parse("38000000-0000-0000-0000-000000000703");

    [Fact]
    public async Task AdminRetry_WithIdempotencyKey_ReturnsAcceptedEnvelopeAndSameKeyReplay()
    {
        var mediator = new CapturingMediator();
        await using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();
        using var first = CreateRequest(HttpMethod.Post, $"/v1/admin/invoices/{InvoiceId:D}/retry", "SYSTEM_ADMIN");
        first.Headers.TryAddWithoutValidation("Idempotency-Key", "invoice-retry-test-key");

        var firstResponse = await client.SendAsync(first);
        using var replay = CreateRequest(HttpMethod.Post, $"/v1/admin/invoices/{InvoiceId:D}/retry", "SYSTEM_ADMIN");
        replay.Headers.TryAddWithoutValidation("Idempotency-Key", "invoice-retry-test-key");
        var replayResponse = await client.SendAsync(replay);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await replayResponse.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("data").GetProperty("pdfGenerationStatus").GetString().Should().Be("PENDING");
        mediator.SendCount.Should().Be(1);
        mediator.LastRequest.Should().BeOfType<RetryInvoiceCommand>()
            .Which.InvoiceId.Should().Be(InvoiceId);
    }

    [Fact]
    public async Task AdminRetry_WithoutIdempotencyKey_ReturnsExactValidationCode()
    {
        var mediator = new CapturingMediator();
        await using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateRequest(HttpMethod.Post, $"/v1/admin/invoices/{InvoiceId:D}/retry", "SYSTEM_ADMIN");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("IDEMPOTENCY_KEY_REQUIRED");
        mediator.SendCount.Should().Be(0);
    }

    [Fact]
    public async Task OperatorDownload_UsesTrustedClaimsAndReturnsApiResponse()
    {
        var mediator = new CapturingMediator();
        await using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateRequest(HttpMethod.Get, $"/v1/operator/invoices/{InvoiceId:D}/download", "OPERATOR_ADMIN");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("data").GetProperty("downloadUrl").GetString()
            .Should().StartWith("https://storage.googleapis.test/");
        var query = mediator.LastRequest.Should().BeOfType<DownloadInvoiceQuery>().Subject;
        query.InvoiceId.Should().Be(InvoiceId);
        query.OperatorId.Should().Be(OperatorId);
        query.UserId.Should().Be(UserId);
    }

    [Fact]
    public async Task OperatorWallet_UsesTrustedOperatorClaim()
    {
        var mediator = new CapturingMediator();
        await using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateRequest(HttpMethod.Get, "/v1/operator/wallet", "OPERATOR_STAFF");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        mediator.LastRequest.Should().BeOfType<GetOperatorWalletQuery>()
            .Which.OperatorId.Should().Be(OperatorId);
    }

    [Fact]
    public async Task AdminPlatformAdjustment_RequiresIdempotencyKey()
    {
        var mediator = new CapturingMediator();
        await using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateRequest(HttpMethod.Post, "/v1/admin/platform-wallet/adjust", "SYSTEM_ADMIN");
        request.Content = JsonContent.Create(new { type = "CREDIT", amount = 100_000, note = "Correction" });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        mediator.SendCount.Should().Be(0);
    }

    [Fact]
    public async Task AdminOperatorAdjustment_WithKey_UsesRouteOperatorAndActorClaims()
    {
        var mediator = new CapturingMediator();
        await using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateRequest(HttpMethod.Post,
            $"/v1/admin/operators/{OperatorId:D}/wallet/adjust", "SYSTEM_ADMIN");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "operator-adjust-test-key");
        request.Content = JsonContent.Create(new { type = "CREDIT", amount = 100_000, note = "Correction" });

        var response = await client.SendAsync(request);
        using var replay = CreateRequest(HttpMethod.Post,
            $"/v1/admin/operators/{OperatorId:D}/wallet/adjust", "SYSTEM_ADMIN");
        replay.Headers.TryAddWithoutValidation("Idempotency-Key", "operator-adjust-test-key");
        replay.Content = JsonContent.Create(new { type = "CREDIT", amount = 100_000, note = "Correction" });
        var replayResponse = await client.SendAsync(replay);
        using var mismatch = CreateRequest(HttpMethod.Post,
            $"/v1/admin/operators/{OperatorId:D}/wallet/adjust", "SYSTEM_ADMIN");
        mismatch.Headers.TryAddWithoutValidation("Idempotency-Key", "operator-adjust-test-key");
        mismatch.Content = JsonContent.Create(new { type = "CREDIT", amount = 200_000, note = "Correction" });
        var mismatchResponse = await client.SendAsync(mismatch);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        mismatchResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        mediator.SendCount.Should().Be(1);
        var command = mediator.LastRequest.Should().BeOfType<AdjustOperatorWalletCommand>().Subject;
        command.OperatorId.Should().Be(OperatorId);
        command.ActorUserId.Should().Be(UserId);
    }

    [Fact]
    public async Task InvoiceList_OperatorStaff_IsForbiddenWithoutCallingApplication()
    {
        var mediator = new CapturingMediator();
        await using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateRequest(HttpMethod.Get, "/v1/operator/invoices", "OPERATOR_STAFF");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        mediator.SendCount.Should().Be(0);
    }

    [Fact]
    public async Task PlatformWallet_OperatorAdmin_IsForbiddenWithoutCallingApplication()
    {
        var mediator = new CapturingMediator();
        await using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateRequest(HttpMethod.Get, "/v1/admin/platform-wallet", "OPERATOR_ADMIN");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        mediator.SendCount.Should().Be(0);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string role)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("X-Test-Role", role);
        return request;
    }

    private static WebApplicationFactory<Program> CreateFactory(CapturingMediator mediator)
        => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting(
                    "ConnectionStrings:Default",
                    "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres");
                builder.UseSetting(
                    "INTERNAL_JWT_SECRET",
                    "day38-invoice-endpoint-test-secret-at-least-32-characters");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IMediator>();
                    services.RemoveAll<ISender>();
                    services.RemoveAll<IPublisher>();
                    services.RemoveAll<IConnectionMultiplexer>();
                    services.AddSingleton<IMediator>(mediator);
                    services.AddSingleton<ISender>(mediator);
                    services.AddSingleton<IPublisher>(mediator);
                    services.AddSingleton(FakeRedis());
                    services.AddAuthentication(options =>
                        {
                            options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                            options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                        })
                        .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                            TestAuthenticationHandler.SchemeName,
                            _ => { });
                });
            });

    private static IConnectionMultiplexer FakeRedis()
    {
        var cache = new Dictionary<string, RedisValue>(StringComparer.Ordinal);
        var db = Substitute.For<IDatabase>();
        db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(call => cache.TryGetValue(call.ArgAt<RedisKey>(0).ToString(), out var value)
                ? value
                : RedisValue.Null);
        db.StringSetAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<When>(),
                Arg.Any<CommandFlags>())
            .Returns(call =>
            {
                var key = call.ArgAt<RedisKey>(0).ToString();
                if (call.ArgAt<When>(3) == When.NotExists && cache.ContainsKey(key))
                    return false;
                cache[key] = call.ArgAt<RedisValue>(1);
                return true;
            });
        var mux = Substitute.For<IConnectionMultiplexer>();
        mux.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(db);
        return mux;
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "InvoiceTest";

        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            if (string.IsNullOrWhiteSpace(role))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[]
            {
                new Claim("sub", UserId.ToString("D")),
                new Claim("operator_id", OperatorId.ToString("D")),
                new Claim(ClaimTypes.Role, role),
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }

    private sealed class CapturingMediator : IMediator
    {
        public object? LastRequest { get; private set; }
        public int SendCount { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            SendCount++;
            object response = request switch
            {
                RetryInvoiceCommand command => new RetryInvoiceResult(command.InvoiceId, "PENDING", 2),
                DownloadInvoiceQuery => new InvoiceDownloadUrl(
                    "https://storage.googleapis.test/signed?token=redacted",
                    new DateTimeOffset(2026, 7, 14, 2, 0, 0, TimeSpan.Zero)),
                GetOperatorWalletQuery query => new OperatorWalletDto(query.OperatorId, 100_000, 0, 0,
                    new DateTimeOffset(2026, 7, 14, 2, 0, 0, TimeSpan.Zero)),
                AdjustOperatorWalletCommand command => new AdjustmentResult(Guid.NewGuid(), command.Request.Type,
                    command.Request.Amount, 0, command.Request.Amount, "ADJUSTMENT", null,
                    command.Request.Note, new DateTimeOffset(2026, 7, 14, 2, 0, 0, TimeSpan.Zero)),
                _ => throw new NotSupportedException(request.GetType().Name),
            };
            return Task.FromResult((TResponse)response);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException(request.GetType().Name);
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => EmptyAsync<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => EmptyAsync<object?>();

        private static async IAsyncEnumerable<T> EmptyAsync<T>()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
