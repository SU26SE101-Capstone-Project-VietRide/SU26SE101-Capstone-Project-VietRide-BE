using MediatR;
namespace VietRide.Trip.Application.Features.Stations;
public sealed record GetStationQuery(Guid StationId) : IRequest<StationDto>;
