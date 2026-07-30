using System.Text.Json.Serialization;

namespace VietRide.Parcel.Infrastructure.Messaging;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TripVehicleSubstitutionMapping
{
    [JsonRequired]
    public Guid BookingId { get; init; }
    [JsonRequired]
    public Guid PassengerId { get; init; }
    public string? OriginalSeatNumber { get; init; }
    public string? NewSeatNumber { get; init; }
    [JsonRequired]
    public string OriginalBoardingStatus { get; init; } = string.Empty;
}
