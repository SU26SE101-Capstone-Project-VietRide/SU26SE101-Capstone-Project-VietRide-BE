using MediatR;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed class PreviewShuttleRouteQueryHandler
    : IRequestHandler<PreviewShuttleRouteQuery, ShuttleRoutePreviewResult>
{
    private readonly IShuttleRoutePreviewService previewService;

    public PreviewShuttleRouteQueryHandler(IShuttleRoutePreviewService previewService)
    {
        this.previewService = previewService;
    }

    public Task<ShuttleRoutePreviewResult> Handle(
        PreviewShuttleRouteQuery request,
        CancellationToken cancellationToken) =>
        previewService.PreviewAsync(
            new ShuttleRoutePreviewInput(
                request.OperatorId,
                request.MainTripId,
                request.Direction,
                request.ScheduledDepartureTime,
                request.OrderedBookingIds),
            cancellationToken);
}
