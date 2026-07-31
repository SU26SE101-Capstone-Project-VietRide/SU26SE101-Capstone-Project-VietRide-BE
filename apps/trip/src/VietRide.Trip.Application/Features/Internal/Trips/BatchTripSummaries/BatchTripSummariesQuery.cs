using VietRide.Shared.Application.Cqrs;

namespace VietRide.Trip.Application.Features.Internal.Trips.BatchTripSummaries;

public sealed record BatchTripSummariesQuery(
    IReadOnlyList<Guid> TripIds) : IQuery<IReadOnlyList<InternalTripSummaryDto>>;
