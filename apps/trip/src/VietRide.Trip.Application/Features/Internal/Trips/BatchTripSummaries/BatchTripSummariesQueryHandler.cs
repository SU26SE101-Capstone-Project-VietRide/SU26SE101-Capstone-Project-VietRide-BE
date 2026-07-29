using MediatR;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Internal.Trips.BatchTripSummaries;

public sealed class BatchTripSummariesQueryHandler
    : IRequestHandler<BatchTripSummariesQuery, IReadOnlyList<InternalTripSummaryDto>>
{
    private readonly ITripRepository repository;

    public BatchTripSummariesQueryHandler(ITripRepository repository)
    {
        this.repository = repository;
    }

    public Task<IReadOnlyList<InternalTripSummaryDto>> Handle(
        BatchTripSummariesQuery request,
        CancellationToken cancellationToken)
        => repository.ListSummariesByIdsAsync(request.TripIds, cancellationToken);
}
