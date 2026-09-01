using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Application.Features.Internal.Trips.ParcelAvailability;

public sealed record SearchParcelAvailableTripsQuery(
    Guid OriginStationId,
    Guid? DestinationStationId,
    DateOnly DepartureDate,
    decimal EstimatedWeightKg,
    decimal EstimatedVolumeM3,
    string SizeCategory,
    int Page,
    int PageSize,
    IReadOnlyCollection<Guid>? EligibleRouteIds = null,
    Guid? DropoffStopId = null,
    string? DestinationProvinceCode = null,
    string? DestinationLocationCode = null) : IRequest<PagedResult<ParcelTripAvailabilityItemDto>>;
