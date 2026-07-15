using MediatR;
using VietRide.Trip.Application.Features.Trips.GetTripDetail;

namespace VietRide.Trip.Application.Features.Trips.EditTrip;

public sealed record EditTripCommand(
    Guid TripId,
    Guid OperatorId,
    Guid ActorUserId,
    string RequestId,
    bool BaseFareSpecified,
    long? BaseFare,
    bool NotesSpecified,
    string? Notes,
    bool VehicleIdSpecified,
    Guid? VehicleId,
    bool RouteIdSpecified,
    Guid? RouteId) : IRequest<TripDetailDto>;
