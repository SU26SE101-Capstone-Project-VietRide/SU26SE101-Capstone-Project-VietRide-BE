using System.Text.Json;
using System.Text.Json.Serialization;

namespace VietRide.Booking.Api.Controllers.Requests;

public sealed class ResolvePendingActionRequest
{
    public string? Action { get; init; }
    public string? Note { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtraFields { get; init; }
}
