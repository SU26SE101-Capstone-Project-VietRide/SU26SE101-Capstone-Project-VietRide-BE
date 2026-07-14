using MediatR;

namespace VietRide.Trip.Application.Features.Stops;

public sealed record GetAdminStopQuery(Guid StopId) : IRequest<StopDto>;
