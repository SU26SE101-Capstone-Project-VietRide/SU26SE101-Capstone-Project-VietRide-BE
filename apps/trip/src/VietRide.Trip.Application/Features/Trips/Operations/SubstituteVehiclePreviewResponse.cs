namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed record SubstituteVehiclePreviewResponse(
    Guid TripId,
    Guid ReplacementVehicleId,
    string PreviewToken,
    IReadOnlyList<SubstituteVehicleSeatPreview> Passengers,
    IReadOnlyList<string> AvailableSeatNumbers);
