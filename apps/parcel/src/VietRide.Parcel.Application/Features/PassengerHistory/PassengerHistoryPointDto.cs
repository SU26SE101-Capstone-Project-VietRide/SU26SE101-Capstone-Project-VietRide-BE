namespace VietRide.Parcel.Application.Features.PassengerHistory;

public sealed record PassengerHistoryPointDto(
    string Type,
    Guid Id,
    string? DisplayName,
    string? Address,
    DateTimeOffset? PlannedAt);
