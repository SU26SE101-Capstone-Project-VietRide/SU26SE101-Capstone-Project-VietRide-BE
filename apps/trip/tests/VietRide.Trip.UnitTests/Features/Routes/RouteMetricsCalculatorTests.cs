using FluentAssertions;
using VietRide.Trip.Application.Common.Geometry;

namespace VietRide.Trip.UnitTests.Features.Routes;

public sealed class RouteMetricsCalculatorTests
{
    [Fact]
    public void Calculate_UsesHaversineAndCoachSpeed()
    {
        var result = RouteMetricsCalculator.Calculate([
            new GeoPoint(0d, 0d),
            new GeoPoint(0d, 1d),
        ]);

        result.DistanceKm.Should().BeApproximately(111.20m, 0.02m);
        result.DurationMinutes.Should().Be(122);
    }

    [Fact]
    public void Project_ReturnsCumulativeMetricsAtNearestSegment()
    {
        var result = RouteMetricsCalculator.Project(
            new GeoPoint(0.01d, 0.5d),
            [new GeoPoint(0d, 0d), new GeoPoint(0d, 1d)]);

        result.DistanceKm.Should().BeApproximately(55.60m, 0.02m);
        result.DurationMinutes.Should().Be(61);
    }
}
