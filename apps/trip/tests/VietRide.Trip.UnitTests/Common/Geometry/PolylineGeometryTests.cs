using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Common.Geometry;

namespace VietRide.Trip.UnitTests.Common.Geometry;

public sealed class PolylineGeometryTests
{
    [Fact]
    public void Decode_ReturnsGoogleReferencePoints()
    {
        var points = PolylineCodec.Decode("_p~iF~ps|U_ulLnnqC_mqNvxq`@");

        points.Should().Equal(
            new GeoPoint(38.5, -120.2),
            new GeoPoint(40.7, -120.95),
            new GeoPoint(43.252, -126.453));
    }

    [Theory]
    [InlineData("_")]
    [InlineData("\u0080")]
    [InlineData("~~~~~~~~~~~~?")]
    public void Decode_ThrowsForMalformedInput(string encodedPolyline)
    {
        var act = () => PolylineCodec.Decode(encodedPolyline);

        var exception = act.Should().Throw<Exception>().Which;
        (exception is FormatException || exception is OverflowException).Should().BeTrue();
    }

    [Fact]
    public void DecodeAndValidate_RejectsPolylineWithFewerThanTwoPoints()
    {
        var act = () => RouteGeometryValidator.DecodeAndValidate("??");

        act.Should().Throw<CodedValidationException>()
            .Which.ErrorCode.Should().Be("ROUTE_GEOMETRY_INVALID");
    }

    [Fact]
    public void PointToPolylineMeters_ReturnsNearZeroForPointOnSegment()
    {
        var distance = GeoDistance.PointToPolylineMeters(
            new GeoPoint(10.75, 106.6),
            [new GeoPoint(10.7, 106.6), new GeoPoint(10.8, 106.6)]);

        distance.Should().BeApproximately(0d, 0.001d);
    }

    [Fact]
    public void PointToPolylineMeters_MatchesTrackingProjectionNearFiveHundredMeterThreshold()
    {
        var distance = GeoDistance.PointToPolylineMeters(
            new GeoPoint(5.5, 0.5046),
            [new GeoPoint(3, 0), new GeoPoint(8, 1)]);

        distance.Should().BeApproximately(501.466d, 0.001d);
    }

    [Fact]
    public void ValidateWaypoints_ReportsStopAndStationIdsSeparately()
    {
        var stopId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var stationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var polyline = new[] { new GeoPoint(10.7, 106.6), new GeoPoint(10.8, 106.6) };

        var act = () => RouteGeometryValidator.ValidateWaypoints(
            polyline,
            [(stopId, new GeoPoint(11.7, 106.6))],
            [(stationId, new GeoPoint(11.8, 106.6))]);

        var exception = act.Should().Throw<CodedValidationException>().Which;
        exception.ErrorCode.Should().Be("ROUTE_GEOMETRY_STOP_MISMATCH");
        exception.Errors.Should().Contain(error => error.Field == "stopIds" && error.Message.Contains(stopId.ToString()));
        exception.Errors.Should().Contain(error => error.Field == "stationIds" && error.Message.Contains(stationId.ToString()));
    }
}
