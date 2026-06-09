using MediatR;

namespace VietRide.Trip.Application.Features.Internal.Stations;

public sealed record GetStationByIdQuery(Guid Id) : IRequest<InternalStationDto>;
