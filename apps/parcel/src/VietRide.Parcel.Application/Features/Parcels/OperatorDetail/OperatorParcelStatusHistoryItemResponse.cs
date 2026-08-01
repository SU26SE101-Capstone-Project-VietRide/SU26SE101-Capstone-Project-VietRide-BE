namespace VietRide.Parcel.Application.Features.Parcels.OperatorDetail;

public sealed record OperatorParcelStatusHistoryItemResponse(
    string Status,
    DateTimeOffset OccurredAt,
    string ActorType,
    Guid? ActorId,
    string Source,
    string? Reason);
