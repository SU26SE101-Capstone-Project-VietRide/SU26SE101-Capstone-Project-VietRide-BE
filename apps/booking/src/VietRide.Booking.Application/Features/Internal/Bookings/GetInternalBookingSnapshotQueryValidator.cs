using FluentValidation;

namespace VietRide.Booking.Application.Features.Internal.Bookings;

public sealed class GetInternalBookingSnapshotQueryValidator
    : AbstractValidator<GetInternalBookingSnapshotQuery>
{
    public GetInternalBookingSnapshotQueryValidator()
    {
        RuleFor(query => query.BookingId).NotEmpty();
    }
}
