using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Polly.CircuitBreaker;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Exceptions;
using VietRide.Booking.Infrastructure.DependencyInjection;
using VietRide.Booking.Infrastructure.Http;
using VietRide.Shared.Http.Handlers;

namespace VietRide.Booking.UnitTests.Infrastructure;

public sealed class IdentityUserServiceClientTests
{
    [Theory]
    [InlineData("0901234567")]
    [InlineData(" +84901234567 ")]
    public async Task Lookup_NormalizesAndEscapesCanonicalPhone(string phone)
    {
        var id = Guid.NewGuid();
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, $"{{\"userId\":\"{id}\"}}"));
        var result = await CreateClient(handler).GetUserIdByPhoneAsync(phone);
        Assert.Equal(id, result);
        Assert.Equal("?phone=%2B84901234567", handler.LastRequest!.RequestUri!.Query);
    }

    [Theory]
    [InlineData("090 123 4567")]
    [InlineData("090-123-4567")]
    [InlineData("(090)1234567")]
    public async Task Lookup_RejectsInternalSeparatorsWithoutCallingIdentity(string phone)
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException());
        await Assert.ThrowsAsync<ArgumentException>(() => CreateClient(handler).GetUserIdByPhoneAsync(phone));
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task Lookup_ExpectedResourceNotFound_ReturnsNull()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, "{\"error\":{\"code\":\"RESOURCE_NOT_FOUND\"}}"));
        Assert.Null(await CreateClient(handler).GetUserIdByPhoneAsync("0901234567"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Lookup_AllOtherHttpFailures_MapToUpstreamUnavailable(HttpStatusCode status)
    {
        var handler = new StubHandler(_ => Json(status, "{}"));
        await Assert.ThrowsAsync<BookingUpstreamUnavailableException>(
            () => CreateClient(handler).GetUserIdByPhoneAsync("0901234567"));
    }

    [Fact]
    public async Task Lookup_TransportFailure_MapsToUpstreamUnavailable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("down"));
        await Assert.ThrowsAsync<BookingUpstreamUnavailableException>(
            () => CreateClient(handler).GetUserIdByPhoneAsync("0901234567"));
    }

    [Fact]
    public async Task Lookup_InvalidJson_MapsToUpstreamUnavailable()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "not-json"));
        await Assert.ThrowsAsync<BookingUpstreamUnavailableException>(
            () => CreateClient(handler).GetUserIdByPhoneAsync("0901234567"));
    }

    [Theory]
    [InlineData("{\"userId\":null}")]
    [InlineData("{\"userId\":\"\"}")]
    public async Task Lookup_NullOrEmptyUserId_MapsToUpstreamUnavailable(string body)
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, body));
        await Assert.ThrowsAsync<BookingUpstreamUnavailableException>(
            () => CreateClient(handler).GetUserIdByPhoneAsync("0901234567"));
    }

    [Fact]
    public async Task Lookup_Timeout_MapsToUpstreamUnavailable()
    {
        var handler = new StubHandler(_ => throw new TaskCanceledException("timeout"));
        await Assert.ThrowsAsync<BookingUpstreamUnavailableException>(
            () => CreateClient(handler).GetUserIdByPhoneAsync("0901234567"));
    }

    [Fact]
    public async Task Lookup_OpenCircuit_MapsToUpstreamUnavailable()
    {
        var handler = new StubHandler(_ => throw new BrokenCircuitException("open"));
        await Assert.ThrowsAsync<BookingUpstreamUnavailableException>(
            () => CreateClient(handler).GetUserIdByPhoneAsync("0901234567"));
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task IdentityRetryPolicy_DoesNotRetryAny4xx(HttpStatusCode status)
    {
        var attempts = 0;
        var policy = InfrastructureServiceCollectionExtensions.CreateIdentityUserRetryPolicy(
            delayProvider: _ => TimeSpan.Zero);

        await policy.ExecuteAsync(() =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(status));
        });

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task IdentityRetryPolicy_Retries5xx()
    {
        var attempts = 0;
        var policy = InfrastructureServiceCollectionExtensions.CreateIdentityUserRetryPolicy(
            delayProvider: _ => TimeSpan.Zero);

        var response = await policy.ExecuteAsync(() =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });

        Assert.Equal(4, attempts);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task IdentityRetryPolicy_RetriesNetworkFailures()
    {
        var attempts = 0;
        var policy = InfrastructureServiceCollectionExtensions.CreateIdentityUserRetryPolicy(
            delayProvider: _ => TimeSpan.Zero);

        await Assert.ThrowsAsync<HttpRequestException>(() => policy.ExecuteAsync(() =>
        {
            attempts++;
            return Task.FromException<HttpResponseMessage>(new HttpRequestException("down"));
        }));

        Assert.Equal(4, attempts);
    }

    [Fact]
    public void IdentityRetryPolicy_DefaultDelaysMatchBsotSchedule()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(200),
            InfrastructureServiceCollectionExtensions.GetIdentityUserRetryDelay(1));
        Assert.Equal(TimeSpan.FromMilliseconds(500),
            InfrastructureServiceCollectionExtensions.GetIdentityUserRetryDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(1),
            InfrastructureServiceCollectionExtensions.GetIdentityUserRetryDelay(3));
    }

    [Fact]
    public void AddInfrastructure_RegistersConfiguredIdentityUserClientAndPolicyPipeline()
    {
        const string clientName = nameof(IIdentityUserServiceClient);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:BaseUrl"] = "http://identity:5001",
                ["Trip:UseDevStub"] = "true",
                ["Identity:UseDevStub"] = "true",
                ["Payment:UseDevStub"] = "true",
                ["InternalJwt:Secret"] = "test-internal-jwt-secret-at-least-32-characters",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration, registerConsumers: false);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<IdentityUserServiceClient>(
            provider.GetRequiredService<IIdentityUserServiceClient>());
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);
        Assert.Equal(new Uri("http://identity:5001"), client.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(5), client.Timeout);

        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(clientName);
        var pipelineTypes = GetPipelineTypes(handler);
        Assert.Contains(typeof(CorrelationIdDelegatingHandler), pipelineTypes);
        Assert.Contains(typeof(InternalJwtDelegatingHandler), pipelineTypes);
        Assert.Equal(2, pipelineTypes.Count(type => type.Name == "PolicyHttpMessageHandler"));
    }

    [Fact]
    public async Task Lookup_CallerCancellation_PropagatesUnchanged()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = new StubHandler(_ => throw new OperationCanceledException(cts.Token));
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateClient(handler).GetUserIdByPhoneAsync("0901234567", cts.Token));
        Assert.Equal(cts.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task GetUsers_ProjectsCompleteBuyerSnapshot()
    {
        var userId = Guid.NewGuid();
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, $$"""
            [{"id":"{{userId:D}}","displayName":" Buyer Name ","phone":" 0900000000 ","email":" buyer@example.test ","avatarUrl":" https://example.test/avatar.jpg ","deleted":false}]
            """));

        var result = await CreateClient(handler).GetUsersAsync([userId]);

        var buyer = Assert.Single(result).Value;
        Assert.Equal(userId, buyer.UserId);
        Assert.Equal("Buyer Name", buyer.DisplayName);
        Assert.Equal("0900000000", buyer.Phone);
        Assert.Equal("buyer@example.test", buyer.Email);
        Assert.Equal("https://example.test/avatar.jpg", buyer.AvatarUrl);
        Assert.False(buyer.Deleted);
    }

    [Fact]
    public async Task GetUsers_DeletedBuyerIsRedactedEvenWhenIdentityReturnsPii()
    {
        var userId = Guid.NewGuid();
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, $$"""
            [{"id":"{{userId:D}}","displayName":"Leaked Name","phone":"0900000000","email":"leaked@example.test","avatarUrl":"https://example.test/leaked.jpg","deleted":true}]
            """));

        var buyer = (await CreateClient(handler).GetUsersAsync([userId]))[userId];

        Assert.Equal("Người dùng đã xóa", buyer.DisplayName);
        Assert.Null(buyer.Phone);
        Assert.Null(buyer.Email);
        Assert.Null(buyer.AvatarUrl);
        Assert.True(buyer.Deleted);
    }

    [Fact]
    public async Task GetUsers_ChunksOneHundredAndOneDistinctIdsIntoOneHundredAndOne()
    {
        var userIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray();
        var handler = new StubHandler(request =>
        {
            var ids = request.RequestUri!.Query
                .TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => Guid.Parse(part["ids=".Length..]));
            var body = "[" + string.Join(",", ids.Select(id =>
                $$"""{"id":"{{id:D}}","displayName":"Buyer","deleted":false}""")) + "]";
            return Json(HttpStatusCode.OK, body);
        });

        var result = await CreateClient(handler).GetUsersAsync(userIds);

        Assert.Equal(101, result.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(
            new[] { 100, 1 },
            handler.Requests.Select(request => request.RequestUri!.Query
                .Split("ids=", StringSplitOptions.None).Length - 1));
    }

    private static IdentityUserServiceClient CreateClient(HttpMessageHandler handler)
        => new(new HttpClient(handler) { BaseAddress = new Uri("http://identity") });

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static IReadOnlyList<Type> GetPipelineTypes(HttpMessageHandler handler)
    {
        var types = new List<Type>();
        for (var current = handler; current is not null; current = (current as DelegatingHandler)?.InnerHandler)
            types.Add(current.GetType());

        return types;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            Requests.Add(request);
            return Task.FromResult(response(request));
        }
    }
}
