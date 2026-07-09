namespace VietRide.Trip.Application.Common.Geometry;

public static class GeoDistance
{
    private const double MetersPerLatitudeDegree = 111_320d;

    public static double PointToPolylineMeters(GeoPoint point, IReadOnlyList<GeoPoint> polyline)
    {
        if (polyline.Count < 2)
        {
            throw new ArgumentException("Polyline must contain at least two points.", nameof(polyline));
        }

        var minimumDistance = double.MaxValue;
        for (var index = 0; index < polyline.Count - 1; index++)
        {
            minimumDistance = Math.Min(
                minimumDistance,
                PointToSegmentMeters(point, polyline[index], polyline[index + 1]));
        }

        return minimumDistance;
    }

    private static double PointToSegmentMeters(GeoPoint point, GeoPoint start, GeoPoint end)
    {
        var referenceLatitudeRadians = start.Latitude * Math.PI / 180d;
        var longitudeScale = MetersPerLatitudeDegree * Math.Cos(referenceLatitudeRadians);

        var pointX = (point.Longitude - start.Longitude) * longitudeScale;
        var pointY = (point.Latitude - start.Latitude) * MetersPerLatitudeDegree;
        var segmentX = (end.Longitude - start.Longitude) * longitudeScale;
        var segmentY = (end.Latitude - start.Latitude) * MetersPerLatitudeDegree;
        var segmentLengthSquared = (segmentX * segmentX) + (segmentY * segmentY);
        var projection = segmentLengthSquared == 0d
            ? 0d
            : ((pointX * segmentX) + (pointY * segmentY)) / segmentLengthSquared;
        projection = Math.Clamp(projection, 0d, 1d);

        var deltaX = pointX - (projection * segmentX);
        var deltaY = pointY - (projection * segmentY);
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }
}
