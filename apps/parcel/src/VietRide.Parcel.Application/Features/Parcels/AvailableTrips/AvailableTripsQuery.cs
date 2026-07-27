using VietRide.Shared.Application.Cqrs;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.Parcels.AvailableTrips;

public sealed record AvailableTripsQuery(
    Guid OriginStationId,
    Guid DestinationStationId,
    DateOnly DepartureDate,
    decimal LengthCm,
    decimal WidthCm,
    decimal HeightCm,
    decimal EstimatedWeightKg,
    string? SizeCategory,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<AvailableTripResponse>>
{
    public AvailableTripsQuery(
        Guid originStationId,
        Guid destinationStationId,
        DateOnly departureDate,
        decimal estimatedWeightKg,
        string? sizeCategory,
        int page = 1,
        int pageSize = 20)
        : this(
            originStationId,
            destinationStationId,
            departureDate,
            LengthCm: 1m,
            WidthCm: 1m,
            HeightCm: 1m,
            estimatedWeightKg,
            sizeCategory,
            page,
            pageSize)
    {
    }
}
