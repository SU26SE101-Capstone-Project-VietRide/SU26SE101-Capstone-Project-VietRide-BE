using MediatR;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed class GetDriverShuttleAssignmentsHandler
    : IRequestHandler<GetDriverShuttleAssignmentsQuery, ShuttleDriverAssignmentPage>
{
    private readonly IShuttleDispatchService service;

    public GetDriverShuttleAssignmentsHandler(IShuttleDispatchService service)
    {
        this.service = service;
    }

    public Task<ShuttleDriverAssignmentPage> Handle(
        GetDriverShuttleAssignmentsQuery request,
        CancellationToken cancellationToken)
        => service.GetDriverAssignmentsAsync(
            request.DriverUserId,
            request.From,
            request.To,
            cancellationToken);
}
