using VietRide.Parcel.Application.Features.Parcels.AssistantTripParcels;
using VietRide.Parcel.Application.Features.Reliability.ReadModels;

namespace VietRide.Parcel.Application.Features.Parcels.AssistantActions;

public sealed record AssistantParcelActionStateResponse(
    Guid ParcelId,
    string ParcelCode,
    string Status,
    ReliabilityLocationResponse DropoffLocation,
    AssistantParcelPaymentStateResponse PaymentState,
    AssistantParcelIdentityHintsResponse IdentityCheckHints);
