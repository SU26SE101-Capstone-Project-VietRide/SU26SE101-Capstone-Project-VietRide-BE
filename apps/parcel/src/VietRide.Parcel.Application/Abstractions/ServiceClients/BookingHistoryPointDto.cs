namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record BookingHistoryPointDto(
    string Type,
    Guid Id,
    string? DisplayName,
    string? Address,
    DateTimeOffset? PlannedAt);
