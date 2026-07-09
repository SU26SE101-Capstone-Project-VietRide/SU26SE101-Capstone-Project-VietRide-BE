using System.Globalization;
using System.Text;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Trip.Application.Common.Geometry;

public static class RouteGeometryValidator
{
    public const int MaximumEncodedBytes = 102_400;
    public const int MaximumPointCount = 10_000;
    public const double MaximumWaypointDistanceMeters = 500d;

    public static IReadOnlyList<GeoPoint> DecodeAndValidate(string encodedPolyline)
    {
        if (Encoding.UTF8.GetByteCount(encodedPolyline) > MaximumEncodedBytes)
        {
            throw new CodedValidationException(
                "ROUTE_GEOMETRY_TOO_LARGE",
                "Route geometry cannot exceed 100 KiB.",
                [new ValidationError("pathPolyline", "Route geometry cannot exceed 100 KiB.")]);
        }

        IReadOnlyList<GeoPoint> points;
        try
        {
            points = PolylineCodec.Decode(encodedPolyline);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            throw InvalidGeometry();
        }

        if (points.Count is < 2 or > MaximumPointCount
            || points.Any(point =>
                !double.IsFinite(point.Latitude)
                || !double.IsFinite(point.Longitude)
                || point.Latitude is < -90d or > 90d
                || point.Longitude is < -180d or > 180d))
        {
            throw InvalidGeometry();
        }

        return points;
    }

    public static void ValidateWaypoints(
        IReadOnlyList<GeoPoint> polyline,
        IEnumerable<(Guid Id, GeoPoint Point)> stops,
        IEnumerable<(Guid Id, GeoPoint Point)> stations)
    {
        var mismatchedStopIds = FindMismatches(polyline, stops);
        var mismatchedStationIds = FindMismatches(polyline, stations);
        if (mismatchedStopIds.Count == 0 && mismatchedStationIds.Count == 0)
        {
            return;
        }

        var errors = new List<ValidationError>();
        if (mismatchedStopIds.Count > 0)
        {
            errors.Add(new ValidationError("stopIds", JoinIds(mismatchedStopIds)));
        }

        if (mismatchedStationIds.Count > 0)
        {
            errors.Add(new ValidationError("stationIds", JoinIds(mismatchedStationIds)));
        }

        throw new CodedValidationException(
            "ROUTE_GEOMETRY_STOP_MISMATCH",
            "One or more route stops or stations are farther than 500 meters from the route geometry.",
            errors);
    }

    private static IReadOnlyList<Guid> FindMismatches(
        IReadOnlyList<GeoPoint> polyline,
        IEnumerable<(Guid Id, GeoPoint Point)> waypoints)
        => waypoints
            .Where(waypoint => GeoDistance.PointToPolylineMeters(waypoint.Point, polyline) > MaximumWaypointDistanceMeters)
            .Select(waypoint => waypoint.Id)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

    private static string JoinIds(IEnumerable<Guid> ids)
        => string.Join(',', ids.Select(id => id.ToString("D", CultureInfo.InvariantCulture)));

    private static CodedValidationException InvalidGeometry()
        => new(
            "ROUTE_GEOMETRY_INVALID",
            "Route geometry is not a valid Google encoded polyline with 2 to 10,000 points.",
            [new ValidationError("pathPolyline", "Route geometry is invalid.")]);
}
