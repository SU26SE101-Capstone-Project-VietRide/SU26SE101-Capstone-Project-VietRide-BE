namespace VietRide.Trip.Application.Common.Geometry;

public static class RouteMetricsCalculator
{
    private const double EarthRadiusKm = 6371.0088d;
    private const double AverageCoachSpeedKmh = 55d;

    public static (decimal DistanceKm, int DurationMinutes) Calculate(IReadOnlyList<GeoPoint> polyline)
    {
        if (polyline.Count < 2)
            throw new ArgumentException("Polyline must contain at least two points.", nameof(polyline));

        var distanceKm = 0d;
        for (var index = 0; index < polyline.Count - 1; index++)
            distanceKm += HaversineKm(polyline[index], polyline[index + 1]);

        return (
            Math.Round((decimal)distanceKm, 2, MidpointRounding.AwayFromZero),
            (int)Math.Ceiling(distanceKm / AverageCoachSpeedKmh * 60d));
    }

    public static (decimal DistanceKm, int DurationMinutes) Project(
        GeoPoint point,
        IReadOnlyList<GeoPoint> polyline)
    {
        if (polyline.Count < 2)
            throw new ArgumentException("Polyline must contain at least two points.", nameof(polyline));

        var cumulativeKm = 0d;
        var bestDistanceMeters = double.MaxValue;
        var bestCumulativeKm = 0d;
        for (var index = 0; index < polyline.Count - 1; index++)
        {
            var start = polyline[index];
            var end = polyline[index + 1];
            var segmentKm = HaversineKm(start, end);
            var fraction = ProjectionFraction(point, start, end);
            var projected = new GeoPoint(
                start.Latitude + ((end.Latitude - start.Latitude) * fraction),
                start.Longitude + ((end.Longitude - start.Longitude) * fraction));
            var distanceMeters = HaversineKm(point, projected) * 1000d;
            if (distanceMeters < bestDistanceMeters)
            {
                bestDistanceMeters = distanceMeters;
                bestCumulativeKm = cumulativeKm + (segmentKm * fraction);
            }

            cumulativeKm += segmentKm;
        }

        return (
            Math.Round((decimal)bestCumulativeKm, 2, MidpointRounding.AwayFromZero),
            (int)Math.Ceiling(bestCumulativeKm / AverageCoachSpeedKmh * 60d));
    }

    private static double ProjectionFraction(GeoPoint point, GeoPoint start, GeoPoint end)
    {
        var latitudeRadians = start.Latitude * Math.PI / 180d;
        var scale = Math.Cos(latitudeRadians);
        var pointX = (point.Longitude - start.Longitude) * scale;
        var pointY = point.Latitude - start.Latitude;
        var segmentX = (end.Longitude - start.Longitude) * scale;
        var segmentY = end.Latitude - start.Latitude;
        var squaredLength = (segmentX * segmentX) + (segmentY * segmentY);
        if (squaredLength == 0d)
            return 0d;
        return Math.Clamp(((pointX * segmentX) + (pointY * segmentY)) / squaredLength, 0d, 1d);
    }

    private static double HaversineKm(GeoPoint first, GeoPoint second)
    {
        var latitudeDelta = ToRadians(second.Latitude - first.Latitude);
        var longitudeDelta = ToRadians(second.Longitude - first.Longitude);
        var firstLatitude = ToRadians(first.Latitude);
        var secondLatitude = ToRadians(second.Latitude);
        var value = Math.Pow(Math.Sin(latitudeDelta / 2d), 2d)
            + (Math.Cos(firstLatitude) * Math.Cos(secondLatitude) * Math.Pow(Math.Sin(longitudeDelta / 2d), 2d));
        return EarthRadiusKm * 2d * Math.Atan2(Math.Sqrt(value), Math.Sqrt(1d - value));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
