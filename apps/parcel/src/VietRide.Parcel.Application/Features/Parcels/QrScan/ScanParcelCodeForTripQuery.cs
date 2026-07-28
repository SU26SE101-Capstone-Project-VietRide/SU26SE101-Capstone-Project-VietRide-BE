using VietRide.Shared.Application.Cqrs;

namespace VietRide.Parcel.Application.Features.Parcels.QrScan;

public sealed record ScanParcelCodeForTripQuery(
    Guid TripId,
    string ParcelCode,
    Guid AssistantUserId,
    Guid OperatorId) : IQuery<ScanParcelCodeForTripResult>;
