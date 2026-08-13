using System.Text.Json;

namespace VietRide.Trip.Application.Features.Vehicles;

internal static class SeatLayoutJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static SeatLayoutDto Deserialize(JsonElement json)
    {
        try
        {
            var layout = json.Deserialize<SeatLayoutDto>(Options);
            if (layout is null || layout.Seats is null || layout.Aisles is null)
            {
                throw new InvalidOperationException("Stored vehicle seat layout is invalid.");
            }

            return layout;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Stored vehicle seat layout is invalid.", exception);
        }
    }

    public static JsonElement Serialize(SeatLayoutDto? layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return JsonSerializer.SerializeToElement(layout, Options);
    }
}
