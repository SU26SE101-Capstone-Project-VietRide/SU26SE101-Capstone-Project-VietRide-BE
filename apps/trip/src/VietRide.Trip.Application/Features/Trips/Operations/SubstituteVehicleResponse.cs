namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed record SubstituteVehicleResponse(
    Guid SubstitutionId,
    Guid OldTripId,
    string OldTripStatus,
    Guid NewTripId,
    string NewTripStatus,
    DateTimeOffset NewTripDepartureDateTime,
    string TransferStatus,
    int AffectedBookingCount,
    int AffectedPassengerCount,
    int PendingSeatAssignmentCount);
