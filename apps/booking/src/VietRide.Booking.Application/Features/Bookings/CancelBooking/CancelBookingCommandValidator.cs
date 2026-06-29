using FluentValidation;
using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.Application.Features.Bookings.CancelBooking;

/// <summary>
/// Input-shape validation for <see cref="CancelBookingCommand"/>.
/// Business checks (owner, booking status, trip status) live in the handler.
/// </summary>
public sealed class CancelBookingCommandValidator : AbstractValidator<CancelBookingCommand>
{
    public CancelBookingCommandValidator()
    {
        RuleFor(x => x.BookingId)
            .NotEmpty();

        RuleFor(x => x.PassengerUserId)
            .NotEmpty();

        RuleFor(x => x.IdempotencyKey)
            .NotEmpty();

        RuleFor(x => x.Reason)
            .NotEmpty()
            .Equal(BookingCancellationReason.USER_INITIATED.ToString())
            .WithMessage("reason must be USER_INITIATED.");
    }
}
