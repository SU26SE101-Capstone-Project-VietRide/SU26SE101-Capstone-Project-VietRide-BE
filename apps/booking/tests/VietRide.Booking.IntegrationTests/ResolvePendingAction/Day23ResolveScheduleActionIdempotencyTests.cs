using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Domain.Entities;

namespace VietRide.Booking.IntegrationTests.ResolvePendingAction;

public sealed class Day23ResolveScheduleActionIdempotencyTests
    : IClassFixture<Day23ResolveScheduleActionWebApplicationFactory>
{
    private readonly Day23ResolveScheduleActionWebApplicationFactory _factory;

    public Day23ResolveScheduleActionIdempotencyTests(Day23ResolveScheduleActionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task SameKeyAndBodyReplaysOriginalBytesAfterActionIsTerminal()
    {
        _factory.Reset();
        var passengerId = Guid.NewGuid();
        var arranged = _factory.Arrange("MEDIUM", passengerId);
        var client = _factory.CreateAuthenticatedClient(passengerId);
        var key = Guid.NewGuid().ToString("D");
        using var firstRequest = Build(arranged.Booking.Id, arranged.Action.Id, key, "ACCEPTED");
        using var secondRequest = Build(arranged.Booking.Id, arranged.Action.Id, key, "ACCEPTED");

        var first = await client.SendAsync(firstRequest);
        var firstBytes = await first.Content.ReadAsByteArrayAsync();
        var second = await client.SendAsync(secondRequest);
        var secondBytes = await second.Content.ReadAsByteArrayAsync();

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        secondBytes.Should().Equal(firstBytes);
    }

    [Fact]
    public async Task MissingAndMalformedKeysUseCanonical422Codes()
    {
        var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var missing = new HttpRequestMessage(HttpMethod.Post, $"/v1/bookings/{Guid.NewGuid()}/pending-actions/{Guid.NewGuid()}/resolve")
        {
            Content = new StringContent("{\"action\":\"ACCEPTED\"}", Encoding.UTF8, "application/json"),
        };
        var missingResponse = await client.SendAsync(missing);
        missingResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await Day23ResolveScheduleActionAuthorizationTests.AssertCodeAsync(missingResponse, "IDEMPOTENCY_KEY_REQUIRED");

        using var malformed = Build(Guid.NewGuid(), Guid.NewGuid(), "not-a-v4", "ACCEPTED");
        var malformedResponse = await client.SendAsync(malformed);
        malformedResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await Day23ResolveScheduleActionAuthorizationTests.AssertCodeAsync(malformedResponse, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task InFlightSameFingerprintReturnsPendingAndDifferentBodyReturnsMismatch()
    {
        _factory.Reset();
        var passengerId = Guid.NewGuid();
        var arranged = _factory.Arrange("MEDIUM", passengerId);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable CS8620 // NSubstitute erases nullable annotations when inferring async Returns.
        _factory.PendingActions.GetByIdForUpdateAsync(arranged.Action.Id, Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                entered.TrySetResult();
                await release.Task;
                BookingPendingAction? result = arranged.Action;
                return result;
            });
#pragma warning restore CS8620
        var client = _factory.CreateAuthenticatedClient(passengerId);
        var key = Guid.NewGuid().ToString("D");
        var first = client.SendAsync(Build(arranged.Booking.Id, arranged.Action.Id, key, "ACCEPTED"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            var pending = await client.SendAsync(Build(arranged.Booking.Id, arranged.Action.Id, key, "ACCEPTED"));
            pending.StatusCode.Should().Be(HttpStatusCode.Conflict);
            await Day23ResolveScheduleActionAuthorizationTests.AssertCodeAsync(
                pending,
                "IDEMPOTENCY_REQUEST_PENDING");

            var mismatch = await client.SendAsync(Build(arranged.Booking.Id, arranged.Action.Id, key, "REJECTED"));
            mismatch.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            await Day23ResolveScheduleActionAuthorizationTests.AssertCodeAsync(
                mismatch,
                "IDEMPOTENCY_KEY_MISMATCH");
        }
        finally
        {
            release.TrySetResult();
        }

        (await first).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("{\"action\":\"ACCEPTED\",\"selectedStopId\":\"11111111-1111-4111-8111-111111111111\"}")]
    [InlineData("{\"action\":\"ACCEPTED\",\"unexpected\":true}")]
    public async Task ExtraRequestFieldsReturnValidationError(string body)
    {
        var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        using var request = BuildRaw(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString("D"), body);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await Day23ResolveScheduleActionAuthorizationTests.AssertCodeAsync(response, "VALIDATION_ERROR");
    }

    [Theory]
    [InlineData("not-a-booking-id", "11111111-1111-4111-8111-111111111111")]
    [InlineData("11111111-1111-4111-8111-111111111111", "not-an-action-id")]
    public async Task MalformedRouteUuidReturnsValidationError(string bookingId, string actionId)
    {
        var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        using var request = BuildRaw(
            bookingId,
            actionId,
            Guid.NewGuid().ToString("D"),
            "{\"action\":\"ACCEPTED\"}");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await Day23ResolveScheduleActionAuthorizationTests.AssertCodeAsync(response, "VALIDATION_ERROR");
    }

    private static HttpRequestMessage Build(Guid bookingId, Guid actionId, string key, string action)
        => BuildRaw(
            bookingId.ToString(),
            actionId.ToString(),
            key,
            JsonSerializer.Serialize(new { action }));

    private static HttpRequestMessage BuildRaw(string bookingId, string actionId, string key, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/bookings/{bookingId}/pending-actions/{actionId}/resolve")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }
}
