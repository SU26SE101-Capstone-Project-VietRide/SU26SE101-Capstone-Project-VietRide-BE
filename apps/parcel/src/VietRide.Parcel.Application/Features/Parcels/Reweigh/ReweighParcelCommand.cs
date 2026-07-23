using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.Reweigh;

[SkipTransaction]
public sealed record ReweighParcelCommand(
    Guid ParcelId,
    Guid OperatorId,
    decimal ActualLengthCm,
    decimal ActualWidthCm,
    decimal ActualHeightCm,
    decimal ActualWeightKg,
    string ActualSizeCategory,
    string PaymentMethod,
    string? IdempotencyKey = null) : IRequest<ReweighParcelResponse>;
