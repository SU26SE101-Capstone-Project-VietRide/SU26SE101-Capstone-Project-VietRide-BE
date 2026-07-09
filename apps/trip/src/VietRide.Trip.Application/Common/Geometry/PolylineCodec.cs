namespace VietRide.Trip.Application.Common.Geometry;

public static class PolylineCodec
{
    public static IReadOnlyList<GeoPoint> Decode(string encodedPolyline)
    {
        ArgumentNullException.ThrowIfNull(encodedPolyline);

        var points = new List<GeoPoint>();
        var index = 0;
        long latitude = 0;
        long longitude = 0;

        while (index < encodedPolyline.Length)
        {
            latitude = checked(latitude + DecodeValue(encodedPolyline, ref index));
            longitude = checked(longitude + DecodeValue(encodedPolyline, ref index));
            points.Add(new GeoPoint(latitude / 100_000d, longitude / 100_000d));
        }

        return points;
    }

    private static long DecodeValue(string encodedPolyline, ref int index)
    {
        long result = 0;
        var shift = 0;

        while (true)
        {
            if (index >= encodedPolyline.Length || shift > 60)
            {
                throw new FormatException("Encoded polyline is truncated or overflows its coordinate range.");
            }

            var character = encodedPolyline[index++];
            if (character is < '?' or > '~')
            {
                throw new FormatException("Encoded polyline contains an invalid character.");
            }

            var value = character - 63;
            result |= (long)(value & 0x1f) << shift;
            shift += 5;

            if (value < 0x20)
            {
                break;
            }
        }

        return (result & 1) == 0 ? result >> 1 : ~(result >> 1);
    }
}
