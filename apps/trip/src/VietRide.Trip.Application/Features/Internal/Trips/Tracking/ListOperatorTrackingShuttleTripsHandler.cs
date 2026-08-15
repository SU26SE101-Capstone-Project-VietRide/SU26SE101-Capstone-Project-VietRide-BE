using MediatR;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Internal.Trips.Tracking;

public sealed class ListOperatorTrackingShuttleTripsHandler
    : IRequestHandler<ListOperatorTrackingShuttleTripsQuery, IReadOnlyList<OperatorTrackingShuttleTripDto>>
{
    private readonly IShuttleDispatchService service;

    public ListOperatorTrackingShuttleTripsHandler(IShuttleDispatchService service) => this.service = service;

    public Task<IReadOnlyList<OperatorTrackingShuttleTripDto>> Handle(
        ListOperatorTrackingShuttleTripsQuery request,
        CancellationToken cancellationToken)
        => service.GetTrackingProjectionAsync(request.OperatorId, cancellationToken);
}
