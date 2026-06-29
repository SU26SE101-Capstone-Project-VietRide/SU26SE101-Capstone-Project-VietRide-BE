using VietRide.Shared.Application.Cqrs;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.Parcels.AvailableTrips;

public sealed record AvailableTripsQuery(
    Guid OriginStationId,
    Guid DestinationStationId,
    DateOnly DepartureDate,
    decimal EstimatedWeightKg,
    string SizeCategory,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<AvailableTripResponse>>;
