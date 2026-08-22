using MediatR;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed class GetShuttlePassengerContactsQueryHandler
    : IRequestHandler<GetShuttlePassengerContactsQuery, ShuttlePassengerContactResponse>
{
    private readonly IShuttleDispatchService _service;

    public GetShuttlePassengerContactsQueryHandler(IShuttleDispatchService service)
    {
        _service = service;
    }

    public Task<ShuttlePassengerContactResponse> Handle(
        GetShuttlePassengerContactsQuery request,
        CancellationToken cancellationToken)
        => _service.GetPassengerContactsAsync(
            request.OperatorId,
            request.ShuttleTripId,
            cancellationToken);
}
