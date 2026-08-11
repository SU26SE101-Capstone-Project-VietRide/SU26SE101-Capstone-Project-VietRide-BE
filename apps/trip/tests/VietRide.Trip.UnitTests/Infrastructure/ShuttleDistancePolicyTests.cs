using FluentAssertions;
using VietRide.Trip.Infrastructure.Services;

namespace VietRide.Trip.UnitTests.Infrastructure;

public sealed class ShuttleDistancePolicyTests
{
    [Fact]
    public void Default_limit_is_10_km()
    {
        ShuttleDistancePolicy.DefaultMaxDistanceKm.Should().Be(10);
        ShuttleDistancePolicy.DefaultMaxDistanceMeters.Should().Be(10_000);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(9_999, true)]
    [InlineData(10_000, true)]
    [InlineData(10_001, false)]
    public void Default_limit_is_inclusive(int distanceMeters, bool expected)
        => ShuttleDistancePolicy.IsWithinDefaultLimit(distanceMeters).Should().Be(expected);
}
