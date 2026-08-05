using FluentAssertions;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Exceptions;
using VietRide.Booking.Application.Features.Bookings;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.UnitTests.Features.Bookings;

public sealed class ShuttleDistancePolicyTests
{
    [Theory]
    [InlineData(4_999)]
    [InlineData(5_000)]
    public void Allows_distance_at_or_below_limit(int distanceMeters)
        => ShuttleDistancePolicy.Resolve(new ShuttleRoadDistanceOutcome.Success(distanceMeters))
            .Should().Be(distanceMeters);

    [Fact]
    public void Rejects_distance_above_limit()
        => FluentActions.Invoking(() => ShuttleDistancePolicy.Resolve(new ShuttleRoadDistanceOutcome.Success(5_001)))
            .Should().Throw<CodedValidationException>()
            .Which.ErrorCode.Should().Be("SHUTTLE_DISTANCE_EXCEEDED");

    [Fact]
    public void Fails_closed_when_provider_is_unavailable()
        => FluentActions.Invoking(() => ShuttleDistancePolicy.Resolve(new ShuttleRoadDistanceOutcome.Unavailable("timeout")))
            .Should().Throw<ShuttleDistanceUnavailableException>();

    [Fact]
    public void Preserves_trip_validation_rejection()
        => FluentActions.Invoking(() => ShuttleDistancePolicy.Resolve(
                new ShuttleRoadDistanceOutcome.Rejected(
                    "SHUTTLE_STATION_NOT_SUPPORTED",
                    "Station does not support shuttle service.")))
            .Should().Throw<CodedValidationException>()
            .Which.ErrorCode.Should().Be("SHUTTLE_STATION_NOT_SUPPORTED");
}
