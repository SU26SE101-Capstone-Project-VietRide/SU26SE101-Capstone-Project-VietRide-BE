using VietRide.Parcel.Application.Abstractions.ServiceClients;

namespace VietRide.Parcel.Application.Features.Parcels.DisplaySnapshots;

public sealed record ParcelTripDisplaySnapshotUpdate(
    Guid ParcelId,
    Guid ExpectedTripId,
    TripSummarySnapshot Summary);
