namespace VietRide.Parcel.Application.Features.Parcels.InternalDetail;

public sealed record InternalParcelResponse(
    Guid ParcelId,
    Guid TripId,
    string Status,
    Guid SenderUserId,
    Guid? RecipientUserId,
    Guid OperatorId,
    Guid? DropoffStopId);
