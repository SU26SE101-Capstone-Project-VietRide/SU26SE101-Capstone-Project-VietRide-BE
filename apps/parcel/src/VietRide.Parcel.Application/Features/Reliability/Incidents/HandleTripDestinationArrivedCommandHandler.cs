using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed class HandleTripDestinationArrivedCommandHandler
    : IRequestHandler<HandleTripDestinationArrivedCommand, int>
{
    public Task<int> Handle(
        HandleTripDestinationArrivedCommand command,
        CancellationToken cancellationToken)
    {
        // Reaching the destination opens the normal terminal unload window. A parcel that is
        // still LOADED/IN_TRANSIT at this instant is expected, so this event must not quarantine
        // it. The trip.completed handler remains the fallback that opens a search incident when
        // the crew finishes the trip without unloading the parcel.
        return Task.FromResult(0);
    }
}
