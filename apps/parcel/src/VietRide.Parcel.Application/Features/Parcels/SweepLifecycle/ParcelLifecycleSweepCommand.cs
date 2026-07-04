using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.SweepLifecycle;

[SkipTransaction]
public sealed record ParcelLifecycleSweepCommand : IRequest<int>;
