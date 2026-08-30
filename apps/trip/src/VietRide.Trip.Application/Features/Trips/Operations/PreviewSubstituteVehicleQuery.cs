using MediatR;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed record PreviewSubstituteVehicleQuery(
    Guid TripId,
    Guid OperatorId,
    Guid ReplacementVehicleId) : IRequest<SubstituteVehiclePreviewResponse>;
