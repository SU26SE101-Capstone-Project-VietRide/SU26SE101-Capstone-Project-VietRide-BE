using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.AutoRejectPendingParcel;

[SkipTransaction]
public sealed record AutoRejectPendingParcelCommand() : IRequest<int>;
