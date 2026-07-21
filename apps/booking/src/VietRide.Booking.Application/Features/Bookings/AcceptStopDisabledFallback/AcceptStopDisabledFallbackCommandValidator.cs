using FluentValidation;

namespace VietRide.Booking.Application.Features.Bookings.AcceptStopDisabledFallback;

public sealed class AcceptStopDisabledFallbackCommandValidator : AbstractValidator<AcceptStopDisabledFallbackCommand>
{
    public AcceptStopDisabledFallbackCommandValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.ActionId).NotEmpty();
        RuleFor(x => x.PassengerUserId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty();
    }
}
