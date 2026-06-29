using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.Reweigh;

[SkipTransaction]
public sealed record ReweighParcelCommand(
    Guid ParcelId,
    Guid OperatorId,
    decimal ActualWeightKg,
    string ActualSizeCategory,
    string PaymentMethod) : IRequest<ReweighParcelResponse>;
