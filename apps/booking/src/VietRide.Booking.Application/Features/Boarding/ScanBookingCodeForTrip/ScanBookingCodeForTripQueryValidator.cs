using FluentValidation;

namespace VietRide.Booking.Application.Features.Boarding.ScanBookingCodeForTrip;

public sealed class ScanBookingCodeForTripQueryValidator
    : AbstractValidator<ScanBookingCodeForTripQuery>
{
    private const string BookingCodePattern = @"^VR-\d{8}-[A-Z2-7]{8}$";

    public ScanBookingCodeForTripQueryValidator()
    {
        RuleFor(query => query.TripId).NotEmpty();
        RuleFor(query => query.CallerUserId).NotEmpty();
        RuleFor(query => query.BookingCode)
            .NotEmpty()
            .Matches(BookingCodePattern);
    }
}
