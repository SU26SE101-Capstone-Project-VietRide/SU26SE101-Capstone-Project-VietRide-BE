namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed record DisruptNoSubstitutionResponse(
    Guid TripId,
    string Status,
    decimal TraveledRatio);
