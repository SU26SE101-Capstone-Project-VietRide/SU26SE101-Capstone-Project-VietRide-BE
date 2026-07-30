using MediatR;

namespace VietRide.Parcel.Application.Features.Parcels.TripEvents;

public sealed record HandleVehicleSubstitutedCommand(
    Guid EventId,
    Guid OldTripId,
    Guid NewTripId,
    Guid OperatorId,
    string Reason) : IRequest<int>;
