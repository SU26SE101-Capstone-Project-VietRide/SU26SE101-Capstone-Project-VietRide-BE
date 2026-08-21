using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed record HandleTripStopDepartedWithPendingCommand(
    Guid TripId,
    Guid StopId,
    DateTimeOffset DepartedAt) : IRequest<int>;
