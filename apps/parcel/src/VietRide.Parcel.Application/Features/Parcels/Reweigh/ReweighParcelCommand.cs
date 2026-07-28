using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.Reweigh;

[SkipTransaction]
public sealed record ReweighParcelCommand(
    Guid ParcelId,
    Guid OperatorId,
    Guid AssistantUserId,
    decimal ActualLengthCm,
    decimal ActualWidthCm,
    decimal ActualHeightCm,
    decimal ActualWeightKg,
    string? IdempotencyKey = null) : IRequest<ReweighParcelResponse>
{
    public ReweighParcelCommand(
        Guid parcelId,
        Guid operatorId,
        decimal actualLengthCm,
        decimal actualWidthCm,
        decimal actualHeightCm,
        decimal actualWeightKg,
        string actualSizeCategory,
        string paymentMethod,
        string? idempotencyKey = null)
        : this(
            parcelId,
            operatorId,
            Guid.Empty,
            actualLengthCm,
            actualWidthCm,
            actualHeightCm,
            actualWeightKg,
            idempotencyKey)
    {
    }
}
