using MediatR;

namespace VietRide.Trip.Application.Features.Stations;

public sealed record DeleteAdminStationCommand(Guid StationId) : IRequest<StationDto>;
