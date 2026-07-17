using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using StackExchange.Redis;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Bookings.ResolvePendingAction;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests.ResolvePendingAction;

public sealed class Day23ResolveScheduleActionAuthorizationTests
    : IClassFixture<Day23ResolveScheduleActionWebApplicationFactory>
{
    private readonly Day23ResolveScheduleActionWebApplicationFactory _factory;

    public Day23ResolveScheduleActionAuthorizationTests(Day23ResolveScheduleActionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task NonPassengerIsRejectedBeforeRepositoriesAreRead()
    {
        _factory.Reset();
        var client = _factory.CreateAuthenticatedClient(Guid.NewGuid(), "OPERATOR_STAFF");

        var response = await client.SendAsync(BuildRequest(Guid.NewGuid(), Guid.NewGuid(), "ACCEPTED"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertCodeAsync(response, "FORBIDDEN");
        await _factory.PendingActions.DidNotReceiveWithAnyArgs().GetByIdForUpdateAsync(default, default);
    }

    [Fact]
    public async Task NonOwnerIsMaskedAsBookingNotFound()
    {
        _factory.Reset();
        var ownerId = Guid.NewGuid();
        var booking = _factory.Arrange("MEDIUM", ownerId);
        var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.SendAsync(BuildRequest(booking.Booking.Id, booking.Action.Id, "ACCEPTED"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertCodeAsync(response, "BOOKING_NOT_FOUND");
    }

    [Fact]
    public async Task MissingTokenReturnsAuthTokenInvalid()
    {
        var response = await _factory.CreateClient().SendAsync(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), "ACCEPTED"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertCodeAsync(response, "AUTH_TOKEN_INVALID");
    }

    [Fact]
    public async Task OwnerAuthorizedMissingActionReturnsPendingActionNotFound()
    {
        _factory.Reset();
        var ownerId = Guid.NewGuid();
        var arranged = _factory.Arrange("MEDIUM", ownerId);
        var missingActionId = Guid.NewGuid();
        _factory.PendingActions.GetByIdForUpdateAsync(missingActionId, Arg.Any<CancellationToken>())
            .Returns((BookingPendingAction?)null);

        var response = await _factory.CreateAuthenticatedClient(ownerId).SendAsync(
            BuildRequest(arranged.Booking.Id, missingActionId, "ACCEPTED"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertCodeAsync(response, "BOOKING_PENDING_ACTION_NOT_FOUND");
    }

    [Fact]
    public async Task ActionBookingMismatchIsMaskedAsBookingNotFound()
    {
        _factory.Reset();
        var ownerId = Guid.NewGuid();
        var arranged = _factory.Arrange("MEDIUM", ownerId);
        typeof(BookingPendingAction).GetProperty(nameof(BookingPendingAction.BookingId))!
            .SetValue(arranged.Action, Guid.NewGuid());

        var response = await _factory.CreateAuthenticatedClient(ownerId).SendAsync(
            BuildRequest(arranged.Booking.Id, arranged.Action.Id, "ACCEPTED"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertCodeAsync(response, "BOOKING_NOT_FOUND");
    }

    internal static HttpRequestMessage BuildRequest(Guid bookingId, Guid actionId, string action)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/bookings/{bookingId}/pending-actions/{actionId}/resolve")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { action }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        return request;
    }

    internal static async Task AssertCodeAsync(HttpResponseMessage response, string code)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(code);
    }
}

public sealed class Day23ResolveScheduleActionWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string TestSecret = "day23-resolve-secret-at-least-32-chars";
    internal static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-17T08:00:00Z");

    public IBookingPendingActionRepository PendingActions { get; } = Substitute.For<IBookingPendingActionRepository>();
    public IBookingRepository Bookings { get; } = Substitute.For<IBookingRepository>();
    public IBookingStatusHistoryRepository History { get; } = Substitute.For<IBookingStatusHistoryRepository>();
    public IIntegrationEventOutbox Outbox { get; } = Substitute.For<IIntegrationEventOutbox>();
    public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

    public void Reset()
    {
        PendingActions.ClearReceivedCalls();
        Bookings.ClearReceivedCalls();
        History.ClearReceivedCalls();
        Outbox.ClearReceivedCalls();
        UnitOfWork.ClearReceivedCalls();
    }

    public (BookingEntity Booking, BookingPendingAction Action) Arrange(
        string severity,
        Guid passengerId,
        long amount = 100_001)
    {
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(Now), passengerId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            null, null, null, Money.FromRaw(amount), Money.Zero, Money.FromRaw(amount),
            tripSnapshotDeparture: Now.AddHours(10));
        booking.Confirm(Now.AddHours(-1));
        var percent = severity == "MEDIUM" ? 50 : 100;
        var refund = (long)Math.Round(amount * (percent / 100m), 0, MidpointRounding.AwayFromZero);
        var action = BookingPendingAction.Create(
            booking.Id,
            BookingPendingActionReason.SCHEDULE_CHANGE,
            Now.AddHours(1),
            Enum.Parse<BookingPendingActionSeverity>(severity),
            JsonSerializer.Serialize(new
            {
                sourceEventId = Guid.NewGuid(),
                oldDeparture = Now.AddHours(8),
                newDeparture = Now.AddHours(11),
                severity,
                initialDeadline = Now.AddHours(1),
                terminalDeadline = severity == "MAJOR" ? Now.AddHours(2) : (DateTimeOffset?)null,
                refundBasisAmount = amount,
                refundPercent = percent,
                refundAmount = refund,
            }));
        PendingActions.GetByIdForUpdateAsync(action.Id, Arg.Any<CancellationToken>()).Returns(action);
        Bookings.FindByIdForUpdateAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        return (booking, action);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("INTERNAL_JWT_SECRET", TestSecret);
        builder.UseSetting("INTERNAL_JWT_SECRET", TestSecret);
        builder.UseSetting("ConnectionStrings:Default", "Host=localhost;Database=test;Username=test;Password=test");
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(PendingActions);
            services.AddSingleton(Bookings);
            services.AddSingleton(History);
            services.AddSingleton(Outbox);
            services.AddSingleton(UnitOfWork);
            UnitOfWork.ExecuteInTransactionAsync(
                    Arg.Any<Func<Task<ResolvePendingActionResult>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<Func<Task<ResolvePendingActionResult>>>()());
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(Now);
            services.AddSingleton(clock);
            services.AddSingleton<IConnectionMultiplexer>(InMemoryIdempotencyRedis.Create());
        });
    }

    public HttpClient CreateAuthenticatedClient(Guid userId, string role = "PASSENGER")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Internal-Auth", $"Bearer {MintJwt(userId, role)}");
        return client;
    }

    private static string MintJwt(Guid userId, string role)
    {
        var now = DateTimeOffset.UtcNow;
        var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["iss"] = "vietride-gateway",
            ["aud"] = "vietride-internal",
            ["sub"] = userId.ToString(),
            ["role"] = role,
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddSeconds(120).ToUnixTimeSeconds(),
        }));
        var input = $"{header}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
        return $"{input}.{Encode(hmac.ComputeHash(Encoding.UTF8.GetBytes(input)))}";
    }

    private static string Encode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
