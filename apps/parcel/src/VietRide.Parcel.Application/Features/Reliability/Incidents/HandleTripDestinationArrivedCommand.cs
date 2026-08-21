using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed record HandleTripDestinationArrivedCommand(
    Guid TripId,
    Guid DestinationStationId,
    DateTimeOffset ArrivedAt) : IRequest<int>;
