using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Internal.Trips.Tracking;

public sealed class ListOperatorTrackingTripsHandler
    : IRequestHandler<ListOperatorTrackingTripsQuery, IReadOnlyList<OperatorTrackingTripDto>>
{
    private readonly ITripRepository trips;

    public ListOperatorTrackingTripsHandler(ITripRepository trips) => this.trips = trips;

    public Task<IReadOnlyList<OperatorTrackingTripDto>> Handle(
        ListOperatorTrackingTripsQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TripStatus? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<TripStatus>(request.Status.Trim(), true, out var parsedStatus))
                throw new CodedValidationException("VALIDATION_ERROR", "Unsupported Trip status.");
            status = parsedStatus;
        }

        var query = trips.QueryNoTracking().Where(trip => trip.OperatorId == request.OperatorId);
        if (status.HasValue)
            query = query.Where(trip => trip.Status == status.Value);
        IReadOnlyList<OperatorTrackingTripDto> result = query
            .OrderBy(trip => trip.Id)
            .Take(100)
            .Select(trip => new OperatorTrackingTripDto(trip.Id, trip.Status.ToString()))
            .ToArray();
        return Task.FromResult(result);
    }
}
