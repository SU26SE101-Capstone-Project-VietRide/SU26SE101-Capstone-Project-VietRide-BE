using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.SendDeliveryPendingConfirmReminders;

[SkipTransaction]
public sealed record SendDeliveryPendingConfirmRemindersCommand : IRequest<int>;
