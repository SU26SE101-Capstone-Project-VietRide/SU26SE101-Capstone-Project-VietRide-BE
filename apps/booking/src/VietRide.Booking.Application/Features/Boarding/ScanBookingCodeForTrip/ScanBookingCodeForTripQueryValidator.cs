using FluentValidation;

namespace VietRide.Booking.Application.Features.Boarding.ScanBookingCodeForTrip;

public sealed class ScanBookingCodeForTripQueryValidator
    : AbstractValidator<ScanBookingCodeForTripQuery>
{
    private const string BookingCodePattern = @"^VR-\d{8}-[A-Z2-7]{8}$";
    private const string TicketCodePattern = @"^VT-\d{8}-[0-9A-HJ-NP-TV-Z]{8}$";

    public ScanBookingCodeForTripQueryValidator()
    {
        RuleFor(query => query.TripId).NotEmpty();
        RuleFor(query => query.CallerUserId).NotEmpty();

        RuleFor(query => query)
            .Must(query =>
                !string.IsNullOrWhiteSpace(query.TicketCode)
                ^ !string.IsNullOrWhiteSpace(query.BookingCode))
            .WithMessage("Exactly one of ticketCode or bookingCode is required.");

        When(query => !string.IsNullOrWhiteSpace(query.TicketCode), () =>
        {
            RuleFor(query => query.TicketCode!)
                .Matches(TicketCodePattern);
        });

        When(query => !string.IsNullOrWhiteSpace(query.BookingCode), () =>
        {
            RuleFor(query => query.BookingCode!)
                .Matches(BookingCodePattern);
        });
    }
}
