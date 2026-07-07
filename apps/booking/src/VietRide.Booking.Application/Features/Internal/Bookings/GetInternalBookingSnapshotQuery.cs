using VietRide.Shared.Application.Cqrs;

namespace VietRide.Booking.Application.Features.Internal.Bookings;

public sealed record GetInternalBookingSnapshotQuery(Guid BookingId)
    : IQuery<InternalBookingSnapshotDto>;
