using MediatR;

namespace VietRide.Trip.Application.Features.Stations;

public sealed record GetAdminStationQuery(Guid StationId) : IRequest<StationDto>;
