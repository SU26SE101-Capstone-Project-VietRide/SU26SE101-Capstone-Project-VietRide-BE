using MediatR;

namespace VietRide.Trip.Application.Features.Internal.Stops;

public sealed record GetStopByIdQuery(Guid Id) : IRequest<InternalStopDto>;
