using System.Text.Json.Serialization;

namespace VietRide.Booking.Infrastructure.Messaging;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TripVehicleSubstitutedMapping
{
    [JsonRequired] public Guid BookingId { get; init; }
    [JsonRequired] public Guid PassengerId { get; init; }
    [JsonRequired] public string? OriginalSeatNumber { get; init; }
    [JsonRequired] public string? NewSeatNumber { get; init; }
    [JsonRequired] public string OriginalBoardingStatus { get; init; } = string.Empty;
}
