namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record ParcelTripAvailabilityFilter(
    Guid OriginStationId,
    Guid? DestinationStationId,
    Guid? DropoffStopId,
    string? DestinationProvinceCode,
    string? DestinationLocationCode);
