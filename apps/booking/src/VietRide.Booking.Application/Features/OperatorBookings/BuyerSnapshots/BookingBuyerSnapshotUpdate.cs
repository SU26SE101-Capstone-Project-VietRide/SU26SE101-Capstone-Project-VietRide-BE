using VietRide.Booking.Application.Abstractions.ServiceClients;

namespace VietRide.Booking.Application.Features.OperatorBookings.BuyerSnapshots;

public sealed record BookingBuyerSnapshotUpdate(
    Guid BookingId,
    BookingBuyerSnapshotProfile Profile);
