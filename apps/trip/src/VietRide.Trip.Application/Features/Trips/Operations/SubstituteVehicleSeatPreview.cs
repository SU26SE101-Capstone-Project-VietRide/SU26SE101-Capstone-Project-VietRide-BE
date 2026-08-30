namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed record SubstituteVehicleSeatPreview(
    Guid BookingId,
    Guid PassengerId,
    string? OriginalSeatNumber,
    string? ProposedSeatNumber,
    bool RequiresAdminSelection,
    IReadOnlyList<string> AlternativeSeatNumbers);
